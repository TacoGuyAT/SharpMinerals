using System.Security.Cryptography;

namespace SharpMinerals.Network.Buffers;

/// <summary>
/// AES/CFB8 stream cipher over an inner stream — the legacy (1.5.2 / protocol 61) transport encryption.
/// Per the protocol, the 16-byte shared secret is used as BOTH the AES key and the IV.
/// <para/>
/// CFB8 is implemented manually over AES-ECB (one block encryption per byte) rather than via
/// <see cref="CipherMode.CFB"/>: .NET's built-in CFB transform reports a 16-byte block size and
/// mishandles non-block-aligned stream data, whereas this is a true byte-granular stream cipher.
/// Each direction keeps its own 16-byte shift register; the ciphertext byte is always fed back.
/// </summary>
public sealed class AesCfb8Stream : Stream {
    readonly Stream inner;
    readonly ICryptoTransform ecb;     // stateless AES-ECB single-block encryptor (used for both directions)
    readonly byte[] readRegister;      // shift register for decryption (reads)
    readonly byte[] writeRegister;     // shift register for encryption (writes)
    readonly byte[] keyStream = new byte[16];

    public AesCfb8Stream(Stream inner, byte[] sharedSecret) {
        this.inner = inner;
        var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;     // we drive the CFB8 feedback ourselves
        aes.Padding = PaddingMode.None;
        aes.Key = sharedSecret;
        ecb = aes.CreateEncryptor();
        readRegister = (byte[])sharedSecret.Clone();   // IV = key
        writeRegister = (byte[])sharedSecret.Clone();
    }

    // CFB8 step: keystream = AES-ECB(register)[0]; output = input XOR keystream; then shift the
    // register left one byte and append the CIPHERTEXT byte (which feeds back in both directions).
    byte Step(byte[] register, byte input, bool encrypting) {
        ecb.TransformBlock(register, 0, 16, keyStream, 0);
        byte output = (byte)(input ^ keyStream[0]);
        byte cipherByte = encrypting ? output : input; // encrypt: c is the output; decrypt: c is the input
        Array.Copy(register, 1, register, 0, 15);
        register[15] = cipherByte;
        return output;
    }

    public override int Read(byte[] buffer, int offset, int count) {
        int n = inner.Read(buffer, offset, count);
        for (int i = 0; i < n; i++)
            buffer[offset + i] = Step(readRegister, buffer[offset + i], encrypting: false);
        return n;
    }

    public override void Write(byte[] buffer, int offset, int count) {
        if (count == 0) return;
        var cipher = new byte[count];
        for (int i = 0; i < count; i++)
            cipher[i] = Step(writeRegister, buffer[offset + i], encrypting: true);
        inner.Write(cipher, 0, count);
    }

    public override void Flush() => inner.Flush();

    protected override void Dispose(bool disposing) {
        if (disposing) {
            ecb.Dispose();
            inner.Dispose();
        }
        base.Dispose(disposing);
    }

    public override bool CanRead => inner.CanRead;
    public override bool CanWrite => inner.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

package sharptester.mixin;

import io.netty.channel.ChannelHandlerContext;
import net.minecraft.network.ClientConnection;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

import java.io.FileWriter;
import java.io.PrintWriter;

/**
 * Logs the full stack trace of any exception that reaches the network pipeline —
 * notably the DecoderException that crashes the client on a malformed packet.
 * The disconnect screen only shows the message; this captures the whole cause chain.
 */
@Mixin(ClientConnection.class)
public class ClientConnectionMixin {
    private static final String LOG = "C:/Users/apotr/source/repos/SharpTester/crash.log";

    @Inject(method = "exceptionCaught", at = @At("HEAD"))
    private void sharptester$logException(ChannelHandlerContext context, Throwable throwable, CallbackInfo ci) {
        try (PrintWriter w = new PrintWriter(new FileWriter(LOG, true))) {
            w.println("===== exceptionCaught =====");
            throwable.printStackTrace(w);
            w.println();
        } catch (Exception ignored) {
        }
        System.err.println("[SharpTester] network exception: " + throwable);
    }
}

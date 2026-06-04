using Microsoft.Extensions.Logging;
using SharpMinerals.Commands;
using System.Globalization;
using System.Text;
#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace SharpMinerals.CLI;

/// <summary>
/// The single owner of the host console's stdout, coordinating the interactive input line with asynchronous log
/// writes so a log line never lands in the middle of a half-typed command. ZLogger is pointed at
/// <see cref="LogStream"/> (see <see cref="LoggingSetup"/>); the input loop calls <see cref="ReadCommandLine"/>.
/// Every write goes through one lock, so the logger's background flush thread and the input thread never interleave.
///
/// When a <see cref="Suggester"/> is wired it shows the selected completion as inline gray "ghost" text after the
/// caret and, when several candidates match, lists them as a gray single-row dropdown below the line. Tab (Shift+Tab)
/// types the selected candidate into the line and cycles in place on repeat; Space accepts and adds the separator,
/// advancing to the next token's suggestions.
///
/// <see cref="Start"/> owns the host-side stdin loop: a background thread reads each line via
/// <see cref="ReadCommandLine"/> and submits it through a <see cref="ServerSender"/> (a '/'-prefixed line as a
/// command, anything else as chat), logging whatever the sender receives.
///
/// The interactive editor is active only on a real TTY. When stdin/stdout are redirected (e.g. the piped
/// <c>tail -f console.txt | server</c> harness) there is no live prompt to corrupt, so <see cref="ReadCommandLine"/>
/// falls back to plain <see cref="Console.ReadLine"/> and ZLogger keeps its normal console sink; the editor and
/// repaint logic are bypassed entirely (see <see cref="IsInteractive"/>).
/// </summary>
internal sealed partial class ConsoleRenderer {
    static readonly ILogger Log = Logging.For("Console");

    // Explicit bytes rather than string literals so no raw ESC control char lives in the source.
    static readonly byte[] CsiOpen = [0x1B, (byte)'['];                          // ESC[ : start a control sequence
    static readonly byte[] ClearBelow = [0x1B, (byte)'[', (byte)'0', (byte)'J']; // ESC[0J: clear cursor -> end of screen (input row + dropdown)
    static readonly byte[] HideCursor = [0x1B, (byte)'[', (byte)'?', (byte)'2', (byte)'5', (byte)'l']; // ESC[?25l
    static readonly byte[] ShowCursor = [0x1B, (byte)'[', (byte)'?', (byte)'2', (byte)'5', (byte)'h']; // ESC[?25h
    static readonly byte[] GrayOn = [0x1B, (byte)'[', (byte)'9', (byte)'0', (byte)'m'];  // ESC[90m: bright black (gray)
    static readonly byte[] GrayOff = [0x1B, (byte)'[', (byte)'3', (byte)'9', (byte)'m']; // ESC[39m: default foreground
    static readonly byte[] ReverseOn = [0x1B, (byte)'[', (byte)'7', (byte)'m'];          // ESC[7m: reverse video (selected)
    static readonly byte[] ReverseOff = [0x1B, (byte)'[', (byte)'2', (byte)'7', (byte)'m']; // ESC[27m

    const int PromptCols = 2;       // visible width of `prompt` ("# "), used by the horizontal-scroll width budget
    const string Ellipsis = "..."; // gray marker shown on a clipped side of a horizontally-scrolled input line
    const int ScrollOff = 4;        // keep the caret this many cols from a text-region edge before scrolling (tunable)

    readonly object gate = new();
    readonly Stream stdout = Console.OpenStandardOutput();
    readonly StringBuilder buffer = new();
    readonly byte[] prompt = "# "u8.ToArray(); // '#' = the C-sharp sign, on brand for SharpMinerals

    int cursor;       // caret position as a char index into buffer (0..buffer.Length); moves in grapheme clusters
    int scrollStart;  // leftmost visible char index (a grapheme boundary) when the line is wider than the window
    readonly List<string> ghostCandidates = []; // full prefix-match tokens for the token at the caret (the cycle menu)
    int ghostStart;    // buffer index where that token begins (Tab replaces from here)
    int ghostTypedLen; // length of the already-typed prefix; the gray PREVIEW remainder = candidate[ghostTypedLen..]
    int ghostIndex;    // selected candidate
    bool completing;   // true once Tab has typed a candidate into the buffer (next Tab replaces it; any edit resets it)
    string ghost = ""; // inline gray preview shown BEFORE Tab (empty once completing: the candidate is typed in)
    bool inputActive; // false until the prompt is live, so server-startup logs print without a stray prompt

    /// <summary>Both stdin and stdout are a real terminal: drive the line editor. Otherwise the caller should fall
    /// back to <see cref="Console.ReadLine"/> and the standard ZLogger console sink (this renderer stays idle).</summary>
    public bool IsInteractive { get; } = !Console.IsInputRedirected && !Console.IsOutputRedirected;

    /// <summary>The stream ZLogger writes formatted log bytes into; each write is serialized with the input line.
    /// Only wire this up in interactive mode.</summary>
    public Stream LogStream { get; }

    /// <summary>Optional completion source. When set, the selected candidate is shown as gray ghost text after the
    /// caret and the full candidate set as a gray single-row dropdown below the line; Tab (Shift+Tab) types/cycles,
    /// Space accepts. Unset = no suggestions. Wire e.g.:
    /// <c>console.Suggester = new CommandSuggestionProvider(server.CommandDispatcher, server.Sender);</c></summary>
    public ISuggestionProvider? Suggester { get; set; }

    public ConsoleRenderer() {
        LogStream = new RendererStream(this);
#if WINDOWS
        EnableVirtualTerminal(); // conhost needs ENABLE_VIRTUAL_TERMINAL_PROCESSING or our ESC[ sequences print literally
#endif
    }

    /// <summary>Writes one batch of already-formatted log bytes, repainting the prompt around it when a live input
    /// line is present. Called by ZLogger's flush thread via <see cref="LogStream"/>.</summary>
    void WriteLog(ReadOnlySpan<byte> formatted) {
        lock (gate) {
            if (inputActive) {
                stdout.Write("\r"u8);
                stdout.Write(ClearBelow);  // wipe the half-typed input line AND the suggestion dropdown below it
                stdout.Write(formatted);   // the log scrolls in its place (ZLogger lines end in a newline)
                Redraw();                  // the input line (and suggestions) reappear below the log
            } else {
                stdout.Write(formatted);   // no live prompt yet: straight through
            }
            stdout.Flush();
        }
    }

    /// <summary>Blocks until the user submits a line. Interactive: a key-by-key editor that survives concurrent log
    /// writes. Non-interactive: plain <see cref="Console.ReadLine"/>. Returns null at end of input.</summary>
    public string? ReadCommandLine() {
        if (!IsInteractive) return Console.ReadLine();

        lock (gate) { inputActive = true; UpdateGhost(); Redraw(); stdout.Flush(); }

        while (true) {
            ConsoleKeyInfo key;
            try { key = Console.ReadKey(intercept: true); } // blocks WITHOUT the lock, so logs can write meanwhile
            catch (InvalidOperationException) { return null; } // stdin closed mid-run

            lock (gate) {
                bool ctrl = (key.Modifiers & ConsoleModifiers.Control) != 0; // Ctrl+Arrow / Ctrl+Backspace/Delete = by word
                switch (key.Key) {
                    case ConsoleKey.Enter:
                        var line = buffer.ToString();
                        ClearGhost();
                        Redraw();               // repaint WITHOUT the ghost/menu so the committed line is clean
                        stdout.Write("\r\n"u8); // commit the typed line; output now flows below it
                        buffer.Clear();
                        cursor = 0;
                        scrollStart = 0;
                        stdout.Flush();
                        return line;
                    case ConsoleKey.Backspace: // delete left: a word with Ctrl, else one grapheme cluster
                        if (cursor > 0) {
                            int start = ctrl ? PrevWord(buffer.ToString(), cursor) : PrevBoundary(buffer.ToString(), cursor);
                            buffer.Remove(start, cursor - start);
                            cursor = start;
                            UpdateGhost(); Redraw(); stdout.Flush();
                        }
                        break;
                    case ConsoleKey.Delete: // delete right: a word with Ctrl, else one grapheme cluster
                        if (cursor < buffer.Length) {
                            int end = ctrl ? NextWord(buffer.ToString(), cursor) : NextBoundary(buffer.ToString(), cursor);
                            buffer.Remove(cursor, end - cursor);
                            UpdateGhost(); Redraw(); stdout.Flush();
                        }
                        break;
                    case ConsoleKey.LeftArrow: // move left: a word with Ctrl, else one grapheme cluster
                        if (cursor > 0) {
                            cursor = ctrl ? PrevWord(buffer.ToString(), cursor) : PrevBoundary(buffer.ToString(), cursor);
                            UpdateGhost(); Redraw(); stdout.Flush();
                        }
                        break;
                    case ConsoleKey.RightArrow: // move right only (no longer accepts the suggestion)
                        if (cursor < buffer.Length) {
                            cursor = ctrl ? NextWord(buffer.ToString(), cursor) : NextBoundary(buffer.ToString(), cursor);
                            UpdateGhost(); Redraw(); stdout.Flush();
                        }
                        break;
                    case ConsoleKey.Home:
                        if (cursor != 0) { cursor = 0; UpdateGhost(); Redraw(); stdout.Flush(); }
                        break;
                    case ConsoleKey.End:
                        if (cursor != buffer.Length) { cursor = buffer.Length; UpdateGhost(); Redraw(); stdout.Flush(); }
                        break;
                    case ConsoleKey.Tab: // type the selected candidate; repeat to cycle (Shift+Tab backward)
                        TabComplete((key.Modifiers & ConsoleModifiers.Shift) != 0 ? -1 : +1);
                        break;
                    case ConsoleKey.Spacebar:
                        // Accept the selected suggestion (if any) at the caret, then insert the space - this both
                        // completes the current token and advances suggestions to the next one.
                        if (cursor == buffer.Length && ghost.Length > 0) { buffer.Append(ghost); cursor = buffer.Length; }
                        buffer.Insert(cursor, ' ');
                        cursor++;
                        UpdateGhost(); Redraw(); stdout.Flush();
                        break;
                    default:
                        if (!char.IsControl(key.KeyChar)) { // a printable char: insert at the caret
                            buffer.Insert(cursor, key.KeyChar);
                            cursor++;
                            // A keystroke changes the suggestion dropdown (its own row(s) below the line), so always
                            // re-shape the whole line + menu. Redraw hides the cursor and overwrites in place, so this
                            // is still flicker-free without the old single-line suffix fast path.
                            UpdateGhost();
                            Redraw();
                            stdout.Flush();
                        }
                        break;
                }
            }
        }
    }

    /// <summary>Wires <paramref name="sender"/>'s replies to the log and starts the stdin read+dispatch loop on a
    /// background thread (so it never keeps the process alive). The sender lives in core; only this byte-level I/O is
    /// CLI. Call once after the server is up and commands are registered.</summary>
    public void Start(ServerSender sender) {
        sender.MessageReceived += m => Log.LogInformation("{Message}", ChatAnsi.Render(m));
        new Thread(() => Run(sender)) { Name = "Console Input", IsBackground = true }.Start();
    }

    void Run(ServerSender sender) {
        string? line;
        while ((line = ReadCommandLine()) is not null) {
            if (sender.Server is not { IsRunning: true }) break; // stop acting on input once shutting down

            if (line.StartsWith('/')) {
                try {
                    sender.RunCommandAsync(line[1..]).GetAwaiter().GetResult();
                } catch (Exception ex) {
                    Log.LogWarning("command error: {Message}", ex.Message);
                }
            } else {
                sender.SendMessage(line);
            }
        }
    }

    // Caller holds the gate. Recomputes the completion state: the full prefix-matching candidate tokens (the menu)
    // and the inline gray remainder of the selected one. Only when the caret is at the end of the buffer (a ghost is
    // a pure suffix) and a provider is wired; otherwise everything clears.
    void UpdateGhost() {
        ClearGhost();
        if (Suggester is null || cursor != buffer.Length) return;
        var input = buffer.ToString();
        var result = Suggester.Suggest(input);
        if (result.Matches.Count == 0) return;
        if (result.Start + result.Length != input.Length) return; // the completed token must end at the caret
        ghostStart = result.Start;
        ghostTypedLen = result.Length;
        var typed = input.Substring(result.Start, result.Length);
        foreach (var match in result.Matches)
            if (match.Length > typed.Length && match.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                ghostCandidates.Add(match); // keep the full token (for the menu); the inline ghost is its remainder
        if (ghostCandidates.Count > 0) ghost = ghostCandidates[0][ghostTypedLen..];
    }

    void ClearGhost() { 
        ghostCandidates.Clear(); 
        ghostIndex = 0; 
        ghostStart = 0; 
        ghostTypedLen = 0; 
        completing = false;
        ghost = ""; 
    }

    // Caller holds the gate. Tab/Shift+Tab: type the selected candidate into the buffer, then cycle on each repeat.
    // The first press inserts the current selection (index 0); subsequent presses replace it with the next/previous
    // candidate. The list stays frozen while you Tab; any edit recomputes it (and resets `completing`).
    void TabComplete(int dir) {
        if (ghostCandidates.Count == 0) return;
        if (completing) ghostIndex = (ghostIndex + dir + ghostCandidates.Count) % ghostCandidates.Count;
        else completing = true; // first Tab types the previewed candidate (index 0)
        buffer.Remove(ghostStart, cursor - ghostStart);
        buffer.Insert(ghostStart, ghostCandidates[ghostIndex]);
        cursor = ghostStart + ghostCandidates[ghostIndex].Length;
        ghost = ""; // the candidate is now typed in the buffer; no gray remainder (but the menu still shows)
        Redraw();
        stdout.Flush();
    }

    // Caller holds the gate. Repaints the input row (prompt + horizontally-scrolled buffer + inline gray ghost) and,
    // when several candidates match, the suggestion menu as a single-row dropdown below - so long namespaced
    // suggestions get the full window width instead of the sliver left after the typed text. Leaves the terminal
    // cursor AT the caret. The input row never wraps (it is clipped to the content width and scrolled to keep the
    // caret visible); when the line is scrolled it shows a gray "..." on the clipped side(s). Width is in grapheme
    // clusters (1 col each) - exact for the ASCII command line, approximate for wide CJK/emoji. Cursor placement is
    // RELATIVE (move up N rows, then right N cols) rather than DECSC/DECRC, because writing the dropdown near the
    // screen bottom scrolls the buffer and would invalidate an absolute saved position. ClearBelow at the top wipes
    // the prior frame (line + dropdown).
    void Redraw() {
        var text = buffer.ToString();
        int caret = System.Math.Clamp(cursor, 0, text.Length);
        int full = ContentWidth();                                  // prompt-to-edge width; only the ghost may use all of it
        int textMax = System.Math.Max(1, full - Ellipsis.Length);   // typed text always keeps a right "..."-width margin

        // Horizontal scroll with a small scroll-off. scrollStart (leftmost visible buffer index) PERSISTS between
        // redraws and only moves when the caret nears an edge, so the view stays put while you move/type inside it. The
        // left "..." OVERLAYS the first Ellipsis.Length columns of the text region - it does NOT add width - so the
        // visible text keeps the same screen columns whether or not it shows; scrolling just slides the text under a
        // fixed "..." one column at a time, with no sideways jump. The right "..." lives in the permanently-reserved
        // right margin (full - textMax), so the text area `region` is the constant textMax either way. The buffer-end
        // clamps (`> region` right, `> 0` left) stop the scroll-off from opening a gap past the start/end.
        scrollStart = SnapBoundary(text, scrollStart);              // text may have changed since the last redraw
        if (Cols(text, 0, text.Length) <= textMax) scrollStart = 0; // whole line fits: never scroll
        int region = textMax;
        // keep the caret ScrollOff cols past the left "..." overlay...
        while (scrollStart > 0 && Cols(text, scrollStart, caret) < Ellipsis.Length + ScrollOff)
            scrollStart = PrevBoundary(text, scrollStart);
        // ...and ScrollOff cols from the right edge while text is still hidden to the right...
        while (scrollStart < caret && Cols(text, scrollStart, caret) > region - 1 - ScrollOff
               && Cols(text, scrollStart, text.Length) > region)
            scrollStart = NextBoundary(text, scrollStart);
        // ...and a hard guarantee the caret is on screen at all.
        while (scrollStart < caret && Cols(text, scrollStart, caret) >= region)
            scrollStart = NextBoundary(text, scrollStart);
        int s = scrollStart;
        bool leftMark = s > 0;
        bool rightMark = Advance(text, s, region) < text.Length;    // text hidden right -> "..." drawn in the reserved margin

        int caretCol = PromptCols + Cols(text, s, caret); // the left "..." overlays, so it does not shift the caret

        stdout.Write(HideCursor);
        stdout.Write("\r"u8);
        stdout.Write(ClearBelow);     // wipe the input row + any previous dropdown (cursor at col 0, nothing painted yet)
        stdout.Write(prompt);
        int used = 0;
        int drawStart = s;
        if (leftMark) { // overlay "..." across the first Ellipsis.Length cols, then skip the buffer cols beneath it
            stdout.Write(GrayOn); WriteText(Ellipsis); stdout.Write(GrayOff);
            used = Ellipsis.Length;
            drawStart = System.Math.Min(Advance(text, s, Ellipsis.Length), caret);
        }
        int headCols = Cols(text, drawStart, caret);
        WriteText(text[drawStart..caret]); used += headCols; // head: visible text left of the caret (under the overlay skipped)
        used += (region - used) - WriteClipped(text, caret, region - used); // tail, clipped to the rest of the text region
        if (rightMark) { stdout.Write(GrayOn); WriteText(Ellipsis); stdout.Write(GrayOff); } // fills the reserved right margin
        else if (ghost.Length > 0) { int gb = full - used; if (gb > 0) { stdout.Write(GrayOn); WriteClipped(ghost, 0, gb); stdout.Write(GrayOff); } } // ghost may run to the edge

        int rows = DrawMenuBelow();   // single-row dropdown below; returns how many rows it added (0 if no menu)

        if (rows > 0) MoveUp(rows);   // back up to the input row...
        stdout.Write("\r"u8);
        MoveRight(caretCol);          // ...and across to the caret
        stdout.Write(ShowCursor);
    }

    // Caller holds the gate, cursor at the end of the input row. Draws the candidate menu as a SINGLE-row dropdown
    // beneath the line and returns the rows it added (1 when shown, 0 when fewer than two candidates). Items are laid
    // out left to right and clipped to the window width - no wrapping; the visible window slides to keep the selected
    // item on the row, with gray `<`/`>` markers when candidates fall off either side. The selected item is reverse
    // video. The fixed one-row height keeps the dropdown from eating scrollback.
    int DrawMenuBelow() {
        int count = ghostCandidates.Count;
        if (count <= 1) return 0;
        int full = MenuWidth();

        stdout.Write("\r\n"u8); // drop onto a fresh row below the input line
        int from = MenuStart(full);
        int col = 0;
        if (from > 0) { stdout.Write(GrayOn); WriteText("< "); stdout.Write(GrayOff); col = 2; }
        int last = from - 1;
        for (int i = from; i < count; i++) {
            // One space of padding inside each item's styling, so the selected item reads as a button.
            var label = " " + ghostCandidates[i] + " ";
            int w = Cols(label, 0, label.Length);
            int sep = i > from ? 1 : 0;
            int reserve = i < count - 1 ? 2 : 0; // leave room for a trailing " >" when more candidates follow
            if (col + sep + w + reserve > full) break;
            if (sep > 0) { WriteText(" "); col++; }
            if (i == ghostIndex) { stdout.Write(ReverseOn); WriteText(label); stdout.Write(ReverseOff); }
            else { stdout.Write(GrayOn); WriteText(label); stdout.Write(GrayOff); }
            col += w; last = i;
        }
        if (last < count - 1) { stdout.Write(GrayOn); WriteText(" >"); stdout.Write(GrayOff); }
        return 1;
    }

    // Smallest start index that still leaves the selected item visible when the single menu row is filled from there.
    int MenuStart(int full) {
        int from = 0;
        while (from < ghostIndex && !SelectedFits(from, full)) from++;
        return from;
    }

    // Whether the selected item is reached before the row (filled from `from`, with the `< `/` >` markers reserved)
    // overflows `full` columns.
    bool SelectedFits(int from, int full) {
        int count = ghostCandidates.Count;
        int col = from > 0 ? 2 : 0; // leading "< "
        for (int i = from; i < count; i++) {
            var label = " " + ghostCandidates[i] + " ";
            int w = Cols(label, 0, label.Length);
            int sep = i > from ? 1 : 0;
            int reserve = i < count - 1 ? 2 : 0; // trailing " >"
            if (col + sep + w + reserve > full) return false;
            col += sep + w;
            if (i == ghostIndex) return true;
        }
        return true;
    }

    void WriteText(string s) { if (s.Length > 0) stdout.Write(Encoding.UTF8.GetBytes(s)); }

    // Relative cursor moves (ESC[<n>A up, ESC[<n>C right), built from explicit bytes so no raw ESC lives in source.
    void MoveUp(int n) { if (n > 0) WriteCsi(n, (byte)'A'); }
    void MoveRight(int n) { if (n > 0) WriteCsi(n, (byte)'C'); }
    void WriteCsi(int n, byte final) {
        stdout.Write(CsiOpen);
        stdout.Write(Encoding.ASCII.GetBytes(n.ToString(CultureInfo.InvariantCulture)));
        stdout.WriteByte(final);
    }

    // Writes s[from..] clipped to at most `budget` grapheme clusters (so it can't overflow the row), returning the
    // columns still free. Used for every piece right of the caret, where running off the edge would wrap.
    int WriteClipped(string s, int from, int budget) {
        if (budget <= 0 || from >= s.Length) return budget;
        int end = Advance(s, from, budget);
        WriteText(s[from..end]);
        return budget - Cols(s, from, end);
    }

    // The width available for the input row's buffer + inline ghost: the full window minus the prompt. We DO use the
    // last cell - with virtual-terminal processing the last-column write only sets a deferred-wrap flag, which the
    // cursor reposition at the end of Redraw clears before any further glyph, so it never actually wraps. Falls back
    // to 80 if the size is unavailable. Never less than 1.
    static int ContentWidth() => System.Math.Max(1, Window() - PromptCols);

    // The width available for the single-row dropdown below the line (it starts at column 0 = the full window).
    static int MenuWidth() => System.Math.Max(1, Window());

    static int Window() {
        int w;
        try { w = Console.WindowWidth; } catch (IOException) { w = 80; }
        return w <= 0 ? 80 : w;
    }

    // Grapheme-cluster (= column) count of s[from..to) and the char index `cols` clusters past `from`. Both treat one
    // cluster as one column, matching how the caret moves; from/to are expected to be grapheme boundaries.
    static int Cols(string s, int from, int to) {
        if (from >= to) return 0;
        int n = 0;
        var e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext()) { int i = e.ElementIndex; if (i >= to) break; if (i >= from) n++; }
        return n;
    }

    static int Advance(string s, int from, int cols) {
        if (cols <= 0) return from;
        int n = 0;
        var e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext()) { int i = e.ElementIndex; if (i < from) continue; if (n == cols) return i; n++; }
        return s.Length;
    }

    // Largest grapheme boundary <= index (clamped to [0, len]). Snaps a PERSISTED scrollStart back onto a valid
    // boundary, since the buffer may have been edited (shifting indices) since it was last set.
    static int SnapBoundary(string s, int index) {
        if (index <= 0) return 0;
        if (index >= s.Length) return s.Length;
        int b = 0;
        var e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext()) { int i = e.ElementIndex; if (i > index) break; b = i; }
        return b;
    }

    // Grapheme-cluster (UAX-#29 text element) boundaries around an index, so the caret and edits move one VISIBLE
    // character at a time: across surrogate-pair emoji, base+combining-marks, and ZWJ-joined emoji sequences.
    static int PrevBoundary(string s, int index) {
        int prev = 0;
        var elements = StringInfo.GetTextElementEnumerator(s);
        while (elements.MoveNext() && elements.ElementIndex < index) prev = elements.ElementIndex;
        return prev;
    }

    static int NextBoundary(string s, int index) {
        var elements = StringInfo.GetTextElementEnumerator(s);
        while (elements.MoveNext())
            if (elements.ElementIndex > index) return elements.ElementIndex;
        return s.Length;
    }

    // Word boundaries for Ctrl+Arrow / Ctrl+Backspace/Delete. A "word" is a maximal run of non-whitespace (the right
    // granularity for a command line: space-separated tokens). Landing positions sit on whitespace transitions, which
    // are always grapheme boundaries too. PrevWord: skip whitespace left, then the word, land at the word's start.
    static int PrevWord(string s, int index) {
        int j = index;
        while (j > 0 && char.IsWhiteSpace(s[j - 1])) j--;
        while (j > 0 && !char.IsWhiteSpace(s[j - 1])) j--;
        return j;
    }

    // NextWord: skip the current word, then the whitespace after it, land at the start of the next word (or the end).
    static int NextWord(string s, int index) {
        int j = index;
        while (j < s.Length && !char.IsWhiteSpace(s[j])) j++;
        while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
        return j;
    }

#if WINDOWS
    // -- Windows VT enable ----------------------------------------------------
    // Compiled in only on a Windows build (the WINDOWS constant, set in the .csproj when building on Windows). Classic
    // conhost doesn't interpret ANSI escapes unless ENABLE_VIRTUAL_TERMINAL_PROCESSING is set, and the interactive
    // path bypasses AddZLoggerConsole (which would have set it), so we enable it ourselves. Other platforms' terminals
    // interpret escapes natively, so this whole section is dropped there - no runtime OS check needed.
    static void EnableVirtualTerminal() {
        nint handle = GetStdHandle(-11); // STD_OUTPUT_HANDLE
        if (handle == 0 || handle == -1) return; // stdout isn't a console handle (redirected/piped)
        if (GetConsoleMode(handle, out uint mode))
            SetConsoleMode(handle, mode | 0x0004); // ENABLE_VIRTUAL_TERMINAL_PROCESSING
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
#endif

    /// <summary>Write-only stream adapter handing ZLogger's formatted bytes to <see cref="WriteLog"/>.</summary>
    sealed class RendererStream(ConsoleRenderer owner) : Stream {
        public override bool CanWrite => true;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override void Write(ReadOnlySpan<byte> buffer) => owner.WriteLog(buffer);
        public override void Write(byte[] buffer, int offset, int count) => owner.WriteLog(buffer.AsSpan(offset, count));
        public override void Flush() { }
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

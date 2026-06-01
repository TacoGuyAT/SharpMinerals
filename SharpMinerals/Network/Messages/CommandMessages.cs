using SharpMinerals.Commands;

namespace SharpMinerals.Network.Messages;

/// <summary>
/// Commands / "Declare Commands" (0x10): the command graph for a player, so the client can tab-complete
/// locally and knows which arguments to ask the server about. Carries the command <see cref="Source"/>; the
/// codec walks that source's dispatcher Brigadier tree, filtered (via <c>.Requires</c>) to what the source
/// may run.
/// </summary>
public sealed record DeclareCommandsS2C(CommandContext Source) : IMessage;

/// <summary>
/// Command Suggestions Request (0x09): the client asks for completions of <paramref name="Text"/> (everything
/// left of the cursor, without the leading '/') under <paramref name="TransactionId"/>.
/// </summary>
public sealed record CommandSuggestionsRequestC2S(int TransactionId, string Text) : IMessage;

/// <summary>
/// Command Suggestions Response (0x0F): completions for <paramref name="TransactionId"/> that replace the
/// span [<paramref name="Start"/>, <paramref name="Start"/> + <paramref name="Length"/>) of the request text.
/// (Tooltips are supported by the protocol but we don't send any.)
/// </summary>
public sealed record CommandSuggestionsResponseS2C(int TransactionId, int Start, int Length, IReadOnlyList<string> Matches) : IMessage;

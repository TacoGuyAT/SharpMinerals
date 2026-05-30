namespace SharpMinerals.Network.Messages;

/// <summary>
/// A test-harness command pushed to the client over the <c>sharptester:cmd</c>
/// Custom Payload channel (e.g. <c>break 0 4 0</c>, <c>mine grass_block 16</c>,
/// <c>goto 200 0</c>, <c>stop</c>, <c>exit</c>). Lets test scenarios be driven from
/// the server without recompiling or restarting the client mod.
/// </summary>
public sealed record TestCommandS2C(string Command) : IMessage;

/// <summary>
/// A serverbound Custom Payload (plugin message). Carries the client's brand and
/// the harness's command results on the <c>sharptester:cmd</c> channel.
/// </summary>
public sealed record CustomPayloadC2S(string Channel, byte[] Data) : IMessage;

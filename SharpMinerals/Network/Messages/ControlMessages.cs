namespace SharpMinerals.Network.Messages;

/// <summary>A test-harness command pushed to the client over the <c>sharptester:cmd</c> Custom Payload channel.</summary>
public sealed record TestCommandS2C(string Command) : IMessage;

/// <summary>A serverbound Custom Payload (plugin message): the client's brand and harness command results.</summary>
public sealed record CustomPayloadC2S(string Channel, byte[] Data) : IMessage;

namespace SharpMinerals.Network.Messages;

/// <summary>The clientbound server brand, a Custom Payload on the <c>minecraft:brand</c> channel shown in F3 (else "null").</summary>
public sealed record BrandS2C(string Brand) : IMessage;

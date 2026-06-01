namespace SharpMinerals.Network.Messages;

/// <summary>
/// The clientbound server brand ("server vendor"), sent on join as a Custom Payload
/// on the <c>minecraft:brand</c> channel. The client reads it into its server-brand
/// field shown in the F3 debug screen; if never sent it shows as <c>null</c>.
/// </summary>
public sealed record BrandS2C(string Brand) : IMessage;

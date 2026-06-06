namespace SharpMinerals.Level.Generator.Features;

/// <summary>A decoration placed after terrain and surface - a tree, flora patch, or ore vein - registered by
/// any mod (vanilla or core) so both contribute features. Placement is deterministic and stateless: each
/// cube enumerates the feature origins in nearby cube-origins whose footprint can reach it and writes only
/// the overlapping part, so a feature straddling a border stitches together without any neighbour state.
///
/// This is the extension point; the feature phase defines the placement driver and the gather shader that
/// invokes it. Kept deliberately minimal until that design lands so the contract is not guessed prematurely.</summary>
public interface IFeature {
}

using SharpMinerals.Blocks.Descriptors;

namespace SharpMinerals.Blocks;

/// <summary>A serialized snapshot of a block's state schema - its <see cref="State"/> properties and their value
/// names, in order. Stored on disk with each stateful block so saved state can be MIGRATED BY NAME to the block's
/// current schema (keeping properties and values that still exist, scrapping the rest) instead of applied blindly
/// by position. Without it, a mod that reorders/renames a property - or a recovery remap to a different block -
/// would land stored values on the wrong properties.
///
/// Wire form: <c>name=v0,v1,..|name2=w0,w1,..</c> (empty for a stateless block). State property and value names
/// are bare identifiers, so the <c>|=,</c> delimiters never collide.</summary>
public static class StateSchema {
    /// <summary>The signature of <paramref name="block"/>'s current state schema (empty if it has no states).</summary>
    public static string Of(BlockType block) =>
        block.TryGet<StatesBlockDescriptor>(out var sp)
            ? string.Join('|', sp.States.Select(p => $"{p.Name}={string.Join(',', p.Values)}"))
            : "";

    /// <summary>Builds a <see cref="BlockState"/> for <paramref name="target"/> from <paramref name="values"/> that
    /// were saved positionally under <paramref name="signature"/>: each old property is matched to the target's by
    /// NAME and each value by value-name; properties or values that no longer exist are dropped. Mirrors
    /// <see cref="Of"/> when the schema is unchanged.</summary>
    public static BlockState Migrate(string signature, IReadOnlyList<int> values, BlockType target) {
        var state = new BlockState(target);
        if (signature.Length == 0 || !target.TryGet<StatesBlockDescriptor>(out var sp)) return state;

        var properties = signature.Split('|');
        for (int i = 0; i < properties.Length && i < values.Count; i++) {
            int eq = properties[i].IndexOf('=');
            if (eq < 0) continue;
            var name = properties[i][..eq];
            var oldValues = properties[i][(eq + 1)..].Split(',');
            int valueIndex = values[i];
            if (valueIndex < 0 || valueIndex >= oldValues.Length) continue; // out of range -> drop

            var property = sp.States.FirstOrDefault(p => p.Name == name);
            if (property is null) continue;                       // property gone -> scrap
            int mapped = property.IndexOf(oldValues[valueIndex]);
            if (mapped < 0) continue;                             // value gone -> scrap
            state.Set(property, mapped);
        }
        return state;
    }
}

using System.Diagnostics.CodeAnalysis;
using Brigadier.NET.ArgumentTypes;
using Brigadier.NET.Tree;
using SharpMinerals.Network.Buffers;
using SharpMinerals.Network.Messages;
using SharpMinerals.Commands;

namespace SharpMinerals.Network.Protocols.JE762.Codecs;

/// <summary>Commands / Declare Commands (0x10): serializes the dispatcher's Brigadier tree, filtered to the
/// message's source, into the 1.20.1 command-node graph.</summary>
internal sealed class DeclareCommandsS2CCodec : ICodec<DeclareCommandsS2C> {
    public void Encode(MinecraftStream s, DeclareCommandsS2C m) =>
        CommandTreeSerializer.Write(s, m.Source.Dispatcher.Brigadier.GetRoot(), m.Source);

    public DeclareCommandsS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("DeclareCommandsS2C is clientbound only.");
}

/// <summary>Command Suggestions Request (0x09, serverbound): transaction id + the text left of the cursor.</summary>
internal sealed class CommandSuggestionsRequestC2SCodec : ICodec<CommandSuggestionsRequestC2S> {
    public void Encode(MinecraftStream s, CommandSuggestionsRequestC2S m) {
        s.WriteVarInt(m.TransactionId);
        s.WriteString(m.Text);
    }

    public CommandSuggestionsRequestC2S Decode(MinecraftStream s) =>
        new(s.ReadVarInt(), s.ReadString(32500));
}

/// <summary>Command Suggestions Response (0x0F): the matches that replace [Start, Start+Length). No tooltips.</summary>
internal sealed class CommandSuggestionsResponseS2CCodec : ICodec<CommandSuggestionsResponseS2C> {
    public void Encode(MinecraftStream s, CommandSuggestionsResponseS2C m) {
        s.WriteVarInt(m.TransactionId);
        s.WriteVarInt(m.Start);
        s.WriteVarInt(m.Length);
        s.WriteVarInt(m.Matches.Count);
        foreach (var match in m.Matches) {
            s.WriteString(match);
            s.WriteBool(false); // has-tooltip: none
        }
    }

    public CommandSuggestionsResponseS2C Decode(MinecraftStream s) =>
        throw new NotSupportedException("CommandSuggestionsResponseS2C is clientbound only.");
}

/// <summary>
/// Writes a Brigadier command tree as the 1.20.1 "Commands" node graph: a flattened node array plus the root
/// index. Nodes failing <c>.Requires</c> are pruned. Arguments with custom suggestions are tagged
/// <c>minecraft:ask_server</c> so the client asks us instead of completing locally.
/// </summary>
static class CommandTreeSerializer {
    public static void Write(MinecraftStream s, CommandNode<SenderContext> root, SenderContext source) {
        // Flatten the reachable, usable nodes into an indexed list (depth-first from the root).
        var nodes = new List<CommandNode<SenderContext>>();
        var index = new Dictionary<CommandNode<SenderContext>, int>(ReferenceEqualityComparer.Instance);
        void Visit(CommandNode<SenderContext> node) {
            if (!index.TryAdd(node, nodes.Count)) return;
            nodes.Add(node);
            foreach (var child in node.Children)
                if (child.CanUse(source)) Visit(child);
        }
        Visit(root);

        s.WriteVarInt(nodes.Count);
        foreach (var node in nodes) WriteNode(s, node, index);
        s.WriteVarInt(index[root]);
    }

    // The argument extras (Type, CustomSuggestions) are read reflectively below to avoid naming the argument's
    // value type T (an open set). DynamicDependency roots those properties on every ArgumentCommandNode<,> so
    // trimming/AOT keeps them, and the suppression acknowledges the GetType()-flow the analyzer can't prove.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, "Brigadier.NET.Tree.ArgumentCommandNode`2", "Brigadier.NET")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "ArgumentCommandNode<,>.Type/CustomSuggestions are rooted via the DynamicDependency above.")]
    static void WriteNode(MinecraftStream s, CommandNode<SenderContext> node, Dictionary<CommandNode<SenderContext>, int> index) {
        int type = node switch {
            RootCommandNode<SenderContext> => 0,
            LiteralCommandNode<SenderContext> => 1,
            _ => 2, // argument
        };

        // Children that survived the requirement filter (i.e. made it into the index).
        var children = new List<int>();
        foreach (var child in node.Children)
            if (index.TryGetValue(child, out int ci)) children.Add(ci);

        // Argument extras are read reflectively so we don't need the argument's value type (T2) at compile time.
        object? argType = null, customSuggestions = null;
        if (type == 2) {
            var t = node.GetType();
            argType = t.GetProperty("Type")!.GetValue(node);
            customSuggestions = t.GetProperty("CustomSuggestions")!.GetValue(node);
        }
        bool hasRedirect = node.Redirect is not null && index.ContainsKey(node.Redirect);

        byte flags = (byte)type;
        if (node.Command is not null) flags |= 0x04;
        if (hasRedirect) flags |= 0x08;
        if (customSuggestions is not null) flags |= 0x10;
        s.WriteUByte(flags);

        s.WriteVarInt(children.Count);
        foreach (int ci in children) s.WriteVarInt(ci);

        if (hasRedirect) s.WriteVarInt(index[node.Redirect!]);

        if (type == 1) {
            s.WriteString(((LiteralCommandNode<SenderContext>)node).Literal);
        } else if (type == 2) {
            s.WriteString(node.Name);
            WriteParser(s, argType!);
            if (customSuggestions is not null) s.WriteString("minecraft:ask_server");
        }
    }

    static void WriteParser(MinecraftStream s, object argType) {
        switch (argType) {
            case BoolArgumentType:
                s.WriteVarInt(0); // brigadier:bool, no properties
                break;
            case FloatArgumentType:
                // Brigadier.NET's FloatArgumentType exposes no public bounds; advertise it as unbounded.
                s.WriteVarInt(1);
                s.WriteUByte(0);
                break;
            case DoubleArgumentType d:
                s.WriteVarInt(2);
                WriteRange(s, d.Minimum != double.MinValue, d.Maximum != double.MaxValue,
                    () => s.WriteDouble(d.Minimum), () => s.WriteDouble(d.Maximum));
                break;
            case IntegerArgumentType i:
                s.WriteVarInt(3);
                WriteRange(s, i.Minimum != int.MinValue, i.Maximum != int.MaxValue,
                    () => s.WriteInt(i.Minimum), () => s.WriteInt(i.Maximum));
                break;
            case LongArgumentType l:
                s.WriteVarInt(4);
                WriteRange(s, l.Minimum != long.MinValue, l.Maximum != long.MaxValue,
                    () => s.WriteLong(l.Minimum), () => s.WriteLong(l.Maximum));
                break;
            case StringArgumentType str:
                s.WriteVarInt(5);
                s.WriteVarInt((int)str.Type); // SingleWord=0, QuotablePhrase=1, GreedyPhrase=2 (matches the protocol)
                break;
            default:
                // Unknown/custom argument type: expose it as a greedy string so the client can at least parse it.
                s.WriteVarInt(5);
                s.WriteVarInt(2);
                break;
        }
    }

    // brigadier:integer/long/float/double properties: a flags byte (bit 0 = min present, bit 1 = max present)
    // followed by the present bounds.
    static void WriteRange(MinecraftStream s, bool hasMin, bool hasMax, Action writeMin, Action writeMax) {
        byte flags = 0;
        if (hasMin) flags |= 0x01;
        if (hasMax) flags |= 0x02;
        s.WriteUByte(flags);
        if (hasMin) writeMin();
        if (hasMax) writeMax();
    }
}

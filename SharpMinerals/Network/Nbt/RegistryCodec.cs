namespace SharpMinerals.Network.Nbt;

/// <summary>
/// Builds the "registry codec" NBT sent inside the Join Game packet. A 1.20.1
/// client refuses to join unless this compound describes the dimension type it
/// spawns into plus the biome, chat-type and damage-type registries. This is a
/// minimal-but-valid subset of vanilla's data — enough for the client to load the
/// world without a "Loading NBT data" decoder error.
/// See https://minecraft.wiki/w/Java_Edition_protocol#Login_(play) (registry codec).
/// </summary>
public static class RegistryCodec {
    /// <summary>The registry codec compound, built programmatically (no external data).</summary>
    public static NbtCompound Default { get; } = Build();

    static NbtCompound Build() => new NbtCompound()
        .Put("minecraft:dimension_type", DimensionTypeRegistry())
        .Put("minecraft:worldgen/biome", BiomeRegistry())
        .Put("minecraft:chat_type", ChatTypeRegistry())
        .Put("minecraft:damage_type", DamageTypeRegistry())
        // Present (1.20+) but no entries needed to join.
        .Put("minecraft:trim_pattern", EmptyRegistry("minecraft:trim_pattern"))
        .Put("minecraft:trim_material", EmptyRegistry("minecraft:trim_material"));

    // ── Generic registry shape: { type, value: [ { name, id, element } ] } ──
    static NbtCompound Registry(string type, NbtList value) =>
        new NbtCompound().Put("type", type).Put("value", value);

    static NbtCompound EmptyRegistry(string type) =>
        Registry(type, new NbtList(NbtTagType.Compound));

    static NbtCompound Entry(string name, int id, NbtCompound element) =>
        new NbtCompound().Put("name", name).Put("id", id).Put("element", element);

    static NbtList StringList(params string[] items) {
        var list = new NbtList(NbtTagType.String);
        foreach (var s in items) list.Add(new NbtString(s));
        return list;
    }

    // ── minecraft:dimension_type ────────────────────────────────────────────
    static NbtCompound DimensionTypeRegistry() {
        var value = new NbtList(NbtTagType.Compound);
        value.Add(Entry("minecraft:overworld", 0, Overworld()));
        return Registry("minecraft:dimension_type", value);
    }

    static NbtCompound Overworld() => new NbtCompound()
        .Put("piglin_safe", false)
        .Put("has_raids", true)
        // monster_spawn_light_level is an IntProvider — it must be the type-dispatch
        // compound form, NOT a bare int (a bare int fails to decode and stalls the join).
        .Put("monster_spawn_light_level", new NbtCompound()
            .Put("type", "minecraft:uniform")
            .Put("value", new NbtCompound().Put("min_inclusive", 0).Put("max_inclusive", 7)))
        .Put("monster_spawn_block_light_limit", 0)
        .Put("natural", true)
        .Put("ambient_light", 0.0f)
        .Put("infiniburn", "#minecraft:infiniburn_overworld")
        .Put("respawn_anchor_works", false)
        .Put("has_skylight", true)
        .Put("bed_works", true)
        .Put("effects", "minecraft:overworld")
        .Put("min_y", -64)
        .Put("height", 384)
        .Put("logical_height", 384)
        .Put("coordinate_scale", 1.0d)
        .Put("ultrawarm", false)
        .Put("has_ceiling", false);

    // ── minecraft:worldgen/biome ────────────────────────────────────────────
    // Badlands at id 0 (ChunkSerializer.BiomeId references it for the reddish tint);
    // minecraft:plains MUST also exist — the client's ClientChunkManager uses it as
    // the empty-chunk default biome (getOrThrow, or world construction fails).
    static NbtCompound BiomeRegistry() {
        var value = new NbtList(NbtTagType.Compound);
        value.Add(Entry("minecraft:badlands", 0, Biome(7254527, 2.0f, 0.0f, hasPrecipitation: false)));
        value.Add(Entry("minecraft:plains", 1, Biome(7907327, 0.8f, 0.4f, hasPrecipitation: true)));
        return Registry("minecraft:worldgen/biome", value);
    }

    static NbtCompound Biome(int skyColor, float temperature, float downfall, bool hasPrecipitation) => new NbtCompound()
        .Put("has_precipitation", hasPrecipitation)
        .Put("temperature", temperature)
        .Put("downfall", downfall)
        .Put("effects", new NbtCompound()
            .Put("sky_color", skyColor)
            .Put("water_fog_color", 329011)
            .Put("fog_color", 12638463)
            .Put("water_color", 4159204)
            .Put("mood_sound", new NbtCompound()
                .Put("tick_delay", 6000)
                .Put("offset", 2.0d)
                .Put("sound", "minecraft:ambient.cave")
                .Put("block_search_extent", 8)));

    // ── minecraft:chat_type ─────────────────────────────────────────────────
    static NbtCompound ChatTypeRegistry() {
        var value = new NbtList(NbtTagType.Compound);
        value.Add(Entry("minecraft:chat", 0, ChatType()));
        return Registry("minecraft:chat_type", value);
    }

    static NbtCompound ChatType() => new NbtCompound()
        .Put("chat", new NbtCompound()
            .Put("translation_key", "chat.type.text")
            .Put("parameters", StringList("sender", "content")))
        .Put("narration", new NbtCompound()
            .Put("translation_key", "chat.type.text.narrate")
            .Put("parameters", StringList("sender", "content")));

    // ── minecraft:damage_type ───────────────────────────────────────────────
    // The client's DamageSources builds a source for EVERY vanilla damage type with
    // getOrThrow(), so all must be present or world construction throws. A generic
    // element is fine — only the keys need to exist.
    static NbtCompound DamageTypeRegistry() {
        // Local (not a static field) to avoid a static-initialisation-order trap: the
        // Default builder runs before static fields declared below it would init.
        string[] names = {
            "arrow", "bad_respawn_point", "cactus", "cramming", "dragon_breath", "drown", "dry_out",
            "explosion", "fall", "falling_anvil", "falling_block", "falling_stalactite", "fireball",
            "fireworks", "fly_into_wall", "freeze", "generic", "generic_kill", "hot_floor", "in_fire",
            "in_wall", "indirect_magic", "lava", "lightning_bolt", "magic", "mob_attack", "mob_attack_no_aggro",
            "mob_projectile", "on_fire", "out_of_world", "outside_border", "player_attack", "player_explosion",
            "sonic_boom", "stalagmite", "starve", "sting", "sweet_berry_bush", "thorns", "thrown", "trident",
            "unattributed_fireball", "wither", "wither_skull",
        };

        var value = new NbtList(NbtTagType.Compound);
        for (int id = 0; id < names.Length; id++)
            value.Add(Entry($"minecraft:{names[id]}", id, DamageType(names[id])));
        return Registry("minecraft:damage_type", value);
    }

    static NbtCompound DamageType(string messageId) => new NbtCompound()
        .Put("message_id", messageId)
        .Put("scaling", "when_caused_by_living_non_player")
        .Put("exhaustion", 0.0f);
}

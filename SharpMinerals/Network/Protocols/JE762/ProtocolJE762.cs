using SharpMinerals.Network.Protocols.JE762.Codecs;

namespace SharpMinerals.Network.Protocols.JE762;

/// <summary>
/// Java Edition protocol 762 (Minecraft 1.19.4) — the base of the modern-Java delta chain. Packet ids in
/// <see cref="Cb"/>/<see cref="Sb"/> are shared by 1.19.4 AND 1.20.1 (the play packet table is byte-for-byte
/// identical between 762 and 763); only the block/item wire ids differ, which live in the <see cref="ITypeMapper"/>.
/// <see cref="ProtocolJE763"/> extends this and swaps in the 1.20.1 id deltas.
/// </summary>
// Not sealed: the modern-Java family is a delta chain — ProtocolJE763 (and a future ProtocolJE765) extend this.
public class ProtocolJE762 : ModernJavaProtocol {
    public override int Version => 762;
    public override string VersionName => "1.19.4";

    // 1.19.4 Chunk Data carries the trust-edges bool; 1.20 (ProtocolJE763) drops it.
    protected override bool ChunkDataHasTrustEdges => true;

    /// <summary>Clientbound packet ids (S2C).</summary>
    protected static class Cb {
        // Status
        public const int StatusResponse = 0x00;
        public const int PongResponse = 0x01;
        // Login
        public const int LoginDisconnect = 0x00;
        public const int LoginSuccess = 0x02;
        // Play
        public const int BundleDelimiter = 0x00; // bundle_delimiter
        public const int SpawnEntity = 0x01;      // spawn_entity
        public const int SetEntityVelocity = 0x54; // entity_velocity
        public const int CollectItem = 0x67;      // collect (item pickup animation)
        public const int SpawnPlayer = 0x03;      // named_entity_spawn
        public const int EntityAnimation = 0x04;  // animation
        public const int AckBlockChange = 0x06;   // acknowledge_player_digging  (NOT 0x05 = statistics)
        public const int BlockUpdate = 0x0A;      // block_change                (NOT 0x09 = block_action)
        public const int CustomPayload = 0x17;    // custom_payload (control channel)
        public const int KeepAlive = 0x23;        // keep_alive
        public const int ChunkData = 0x24;        // chunk_data_and_update_light
        public const int JoinGame = 0x28;         // login
        public const int Respawn = 0x41;          // respawn (world switch)
        public const int SyncPlayerPosition = 0x3C; // player_position
        public const int PlayerInfoRemove = 0x39; // player_remove
        public const int PlayerInfoUpdate = 0x3A; // player_info
        public const int RemoveEntities = 0x3E;   // entity_destroy
        public const int EntityHeadRotation = 0x42; // entity_head_rotation
        public const int SetCenterChunk = 0x4E;   // set_chunk_cache_center
        public const int SetDefaultSpawn = 0x50;  // spawn_position
        public const int SetEntityMetadata = 0x52; // entity_metadata
        public const int SetEquipment = 0x55;     // entity_equipment (held item + armour)
        public const int SetHealth = 0x57;        // update_health  (NOT 0x5A)
        public const int TeleportEntity = 0x68;   // entity_teleport
        public const int SystemChat = 0x64;       // system_chat
        public const int PlayerListHeaderFooter = 0x65; // playerlist_header (tab-list header + footer)
        public const int Commands = 0x10;          // declare_commands (the command tree)
        public const int CommandSuggestions = 0x0F; // command_suggestions response (tab-complete)
        // Play: containers / inventory
        public const int OpenScreen = 0x30;          // open_window
        public const int SetContainerContent = 0x12; // window_items
        public const int SetContainerSlot = 0x14;    // set_slot
        public const int CloseContainer = 0x11;      // close_window (clientbound)
        public const int SetHeldItem = 0x4D;         // held_item_slot (clientbound)
    }

    /// <summary>Serverbound packet ids (C2S).</summary>
    protected static class Sb {
        // Handshaking
        public const int Handshake = 0x00;
        // Status
        public const int StatusRequest = 0x00;
        public const int PingRequest = 0x01;
        // Login
        public const int LoginStart = 0x00;
        // Play
        public const int ConfirmTeleportation = 0x00; // teleport_confirm
        public const int ChatCommand = 0x04;      // chat_command
        public const int ChatMessage = 0x05;      // chat_message
        public const int CommandSuggestions = 0x09; // command_suggestions request (tab-complete)
        public const int InteractEntity = 0x10;   // use_entity (attack/interact)
        public const int CustomPayload = 0x0D;    // custom_payload (control channel + brand)
        public const int KeepAlive = 0x12;        // keep_alive
        public const int SetPlayerPosition = 0x14; // position
        public const int SetPlayerPositionRotation = 0x15; // position_look
        public const int SetPlayerRotation = 0x16; // look
        public const int PlayerAction = 0x1D;     // player_action (digging)
        public const int EntityAction = 0x1E;     // entity_action (sneak/sprint)
        public const int UseItemOn = 0x31;        // use_item_on (block placement)
        public const int SwingArm = 0x2F;         // arm_animation (swing)
        // Play: containers / inventory
        public const int ClickContainer = 0x0B;      // window_click
        public const int CloseContainer = 0x0C;      // close_window (serverbound)
        public const int SetHeldItem = 0x28;         // held_item_slot (serverbound)
        public const int SetCreativeModeSlot = 0x2B; // set_creative_slot
    }

    public ProtocolJE762() {
        // ── Handshaking ─────────────────────────────────────────────────────
        Register(ConnectionState.Handshaking, PacketDirection.Serverbound, Sb.Handshake, new HandshakeC2SCodec());

        // ── Status ──────────────────────────────────────────────────────────
        Register(ConnectionState.Status, PacketDirection.Serverbound, Sb.StatusRequest, new StatusRequestC2SCodec());
        Register(ConnectionState.Status, PacketDirection.Clientbound, Cb.StatusResponse, new StatusResponseS2CCodec());
        Register(ConnectionState.Status, PacketDirection.Serverbound, Sb.PingRequest, new PingRequestC2SCodec());
        Register(ConnectionState.Status, PacketDirection.Clientbound, Cb.PongResponse, new PongResponseS2CCodec());

        // ── Login ───────────────────────────────────────────────────────────
        Register(ConnectionState.Login, PacketDirection.Serverbound, Sb.LoginStart, new LoginStartC2SCodec());
        Register(ConnectionState.Login, PacketDirection.Clientbound, Cb.LoginDisconnect, new LoginDisconnectS2CCodec());
        Register(ConnectionState.Login, PacketDirection.Clientbound, Cb.LoginSuccess, new LoginSuccessS2CCodec());

        // ── Play: clientbound ───────────────────────────────────────────────
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.BundleDelimiter, new BundleDelimiterS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.SpawnEntity, new SpawnEntityS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.SetEntityVelocity, new SetEntityVelocityS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.CollectItem, new CollectItemS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.SpawnPlayer, new SpawnPlayerS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.PlayerInfoUpdate, new PlayerInfoUpdateS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.PlayerInfoRemove, new PlayerInfoRemoveS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.RemoveEntities, new RemoveEntitiesS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.TeleportEntity, new TeleportEntityS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.EntityHeadRotation, new EntityHeadRotationS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.EntityAnimation, new EntityAnimationS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.SystemChat, new SystemChatMessageS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.PlayerListHeaderFooter, new PlayerListHeaderFooterS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.Commands, new DeclareCommandsS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.CommandSuggestions, new CommandSuggestionsResponseS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.AckBlockChange, new AckBlockChangeS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.BlockUpdate, new BlockUpdateS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.CustomPayload, new BrandS2CCodec());
#if TEST_HARNESS
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.CustomPayload, new TestCommandS2CCodec());
#endif
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.KeepAlive, new KeepAliveS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.ChunkData, new ChunkDataS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.JoinGame, new JoinGameS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.Respawn, new RespawnS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.SyncPlayerPosition, new SynchronizePlayerPositionS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.SetCenterChunk, new SetCenterChunkS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.SetDefaultSpawn, new SetDefaultSpawnPositionS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.SetEntityMetadata, new SetItemEntityMetadataS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.SetEntityMetadata, new EntityFlagsS2CCodec()); // both encode to 0x52
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.SetEquipment, new SetEquipmentS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.SetHealth, new SetHealthS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.OpenScreen, new OpenScreenS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.SetContainerContent, new SetContainerContentS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.SetContainerSlot, new SetContainerSlotS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.CloseContainer, new CloseContainerS2CCodec());
        Register(ConnectionState.Play, PacketDirection.Clientbound, Cb.SetHeldItem, new SetHeldItemS2CCodec());

        // ── Play: serverbound ───────────────────────────────────────────────
#if TEST_HARNESS
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.CustomPayload, new CustomPayloadC2SCodec());
#endif
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.ChatCommand, new ChatCommandC2SCodec());
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.ChatMessage, new ChatMessageC2SCodec());
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.CommandSuggestions, new CommandSuggestionsRequestC2SCodec());
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.ConfirmTeleportation, new ConfirmTeleportationC2SCodec());
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.KeepAlive, new KeepAliveC2SCodec());
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.SetPlayerPosition, new SetPlayerPositionC2SCodec());
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.SetPlayerPositionRotation, new SetPlayerPositionAndRotationC2SCodec());
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.SetPlayerRotation, new SetPlayerRotationC2SCodec());
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.InteractEntity, new InteractEntityC2SCodec());
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.SwingArm, new SwingArmC2SCodec());
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.EntityAction, new EntityActionC2SCodec());
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.PlayerAction, new PlayerActionC2SCodec());
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.UseItemOn, new UseItemOnC2SCodec());
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.ClickContainer, new ClickContainerC2SCodec());
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.CloseContainer, new CloseContainerC2SCodec());
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.SetHeldItem, new SetHeldItemC2SCodec());
        Register(ConnectionState.Play, PacketDirection.Serverbound, Sb.SetCreativeModeSlot, new SetCreativeModeSlotC2SCodec());
    }
}

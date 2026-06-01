# Resources & Libraries

What SharpMinerals is built on: the toolchain, the libraries it ships, the
references the protocol implementation is written against, and — kept strictly
separate — the external tooling used only to test it.

## Toolchain

| | |
|---|---|
| .NET SDK | 10.0.101 |
| Target framework | `net8.0` (all projects) |
| Language options | `ImplicitUsings`, `Nullable`, `AllowUnsafeBlocks` enabled |
| Debug-only symbol | `TEST_HARNESS` — gates the self-test, the `dumpnbt`/`dumpchunk` CLIs, and the server→client control channel. Release strips it. |

Build & run:

```sh
dotnet build SharpMinerals/SharpMinerals.csproj -c Debug      # or -c Release
dotnet run --project SharpMinerals -- selftest               # in-process verification (Debug)
```

## SharpMinerals libraries (runtime dependencies)

| Package | Version | Used for |
|---|---|---|
| [Arch](https://github.com/genaray/Arch) | 2.1.0 | Entity-Component-System — every world entity lives in an Arch `World`. |
| [ZLogger](https://github.com/Cysharp/ZLogger) | 2.5.10 | Zero-allocation structured logging backend, in the host (console + daily rolling file). |
| Microsoft.Extensions.Logging | 8.0.0 | Logging abstraction the code is written against (`Logging.For(category)`). |

Built-in BCL, no package needed:

- `System.Text.Json` — chat-component and server-list-ping JSON.
- `System.Security.Cryptography` (MD5) — offline-mode v3 UUID from `OfflinePlayer:<name>`.
- `System.Buffers.Binary` — big-endian reads/writes in `MinecraftStream`/NBT.

### Project references

| Project | Role |
|---|---|
| `PrecisionTimer` (sibling repo) | High-resolution pacing for the 20 TPS game loop. `net8.0`, unsafe blocks, per-OS defines. |
| `SharpMinerals.Chat` | Chat message components only (`ChatComponent` and subclasses). `net8.0`, no external deps. |

> `SharpMinerals.Network.TCP` exists in the tree but is **dead/duplicate** code, not
> in the solution — the live transport is `SharpMinerals/Network/Tcp/`.

## Modding API (`SharpMinerals.Modding`)

A **HarmonyX-based modding API**, modelled on [`HarmonyMine`](../../HarmonyMine) but
patching this server's own managed code (no IKVM/JVM).

| Package | Version | Use |
|---|---|---|
| [HarmonyX](https://github.com/BepInEx/HarmonyX) | 2.13.0 | Per-mod `Harmony` instance for runtime patching (`Mod.Harmony`). |

A mod is a `Mod` subclass plus an assembly-level `[ModInfo(id, version, authors)]`.
Lifecycle, driven by `ModLoader`:

1. **`OnInitialize()`** — register content (`BlockRegistry.Register`, `ItemRegistry.Register`,
   `EntityRegistry.Register`) and apply Harmony patches. Runs **before** the protocols /
   type-mappers snapshot the palette.
2. **`OnServerStarted(Server)`** — server is up: register commands on `server.CommandDispatcher`,
   subscribe to events, set `server.MOTD`, etc.
3. **`OnServerStopping(Server)`** — shutdown cleanup.

Loading (in `SharpMinerals.CLI/Program.cs`): `ModLoader.LoadFrom(...)` for compiled-in /
consuming-assembly mods and `ModLoader.LoadDirectory("mods")` for file mods (`mods/*.dll`),
then `ModContent.Freeze()` seals the registries before protocols are built; `StartAll` /
`StopAll` run the runtime hooks. A modded block has no vanilla state id yet, so it renders
as **stone** (the type-mapper fallback) on a stock client — a per-block type-mapping
component (mod-chosen masquerade id) is the planned next step.

- `SharpMinerals.TestMod` — the `/test` harness command, packaged as a mod. Loaded only on
  Debug (CLI references it under `'$(Configuration)'=='Debug'`) and by the real-client test
  fixture (through the real `ModLoader`), so the shipped Release server has no `/test`.
- `SharpMinerals.SampleMod` — a worked example (custom MOTD + a `ruby_block` + a `/ruby`
  command). **File-loaded**: build it and drop `SharpMinerals.SampleMod.dll` into the
  server's `mods/` folder.

## Protocol references

The wire protocol is hand-written (no third-party protocol library) against:

- **[minecraft.wiki — Java Edition protocol](https://minecraft.wiki/w/Java_Edition_protocol)** — the primary spec for packet layouts, data types, the registry codec, NBT, and chunk format. Anchors the code cites:
  - `#Login_(play)` — Join Game / registry codec
  - `#Data_types` — VarInt / String / UUID / Position packing
  - `#Chunk_Data_and_Update_Light` — chunk serialization
  - [`NBT_format`](https://minecraft.wiki/w/NBT_format) — tag types and encoding
- **Yarn mappings** (`1.20.1+build.10`) — the primary source for client-side class/method/field names (`ClientConnection`, `DamageSources`, `ClientChunkManager`, …) when diagnosing why the real client rejects a packet.
- **[PrismarineJS minecraft-data](https://github.com/PrismarineJS/minecraft-data)** (`data/pc/1.20/protocol.json`) — a **secondary** cross-check for packet ids only.

Target wire version: **protocol 763 = Minecraft 1.20.1**. Ids are centralized in
`Network/Protocols/JE763/ProtocolJE763.cs`.

## Test harness — separate project, NOT a SharpMinerals dependency

These belong to the `SharpTester` Fabric mod (a different repository) used to drive a
real 1.20.1 client against the server. They are listed here only so the testing
toolchain is documented; **none of them are referenced by SharpMinerals**.

| Tool | Version | Role |
|---|---|---|
| Fabric Loom | 1.6.12 | Mod build/dev framework. |
| Yarn mappings | `1.20.1+build.10` | Deobfuscation for the mod. |
| Fabric API | — | Client/event hooks. |
| Baritone | — | Pathfinding/automation for test commands. |
| JDK (hotspot) | 17 | Gradle build runtime. |
| Gradle (wrapper) | 8.6 | Build tool. |

The mod adds a crash-capture mixin and a `sharptester:cmd` control channel; the
server drives it via the `test` command (gated under `TEST_HARNESS`). See the
project memory notes for the full harness workflow.

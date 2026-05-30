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
| [Serilog](https://serilog.net/) | 4.0.0 | Structured logging (core). |
| Serilog.Extensions.Logging | 8.0.0 | Bridges Serilog under `Microsoft.Extensions.Logging`. |
| Serilog.Sinks.Console | 6.0.0 | Console sink. |
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

## Reserved — not yet wired

| Package | Version | Intended use |
|---|---|---|
| [HarmonyX](https://github.com/BepInEx/HarmonyX) | 2.13.0 | A planned **Harmony-based modding API** (Mixin-like runtime patching). |

The empty `Modding/` folder (namespace `SharpMinerals.Modding`) is reserved for it.
The design follows [`HarmonyMine`](../../HarmonyMine) — a sibling project where mods
are a `Mod` base class plus `[ModInfo]`/`[Command]` attributes, discovered by
reflection and applied as Harmony patches. HarmonyMine patches a **Java** Minecraft
server through IKVM; the SharpMinerals version will patch its own managed server
directly. Nothing references HarmonyX in code yet — it is a forward-looking
dependency.

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

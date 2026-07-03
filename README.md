# SharpMinerals [![License: Apache 2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE) [![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/) [![Status: early / WIP](https://img.shields.io/badge/status-early%20%2F%20WIP-orange.svg)](#status) [![Discord](https://img.shields.io/discord/1513003669518422129?logo=discord)](https://discord.gg/2BdMk9MvE2)
**An experimental ECS-based voxel simulation engine and framework, primarily focusing on implementing a future-proof, extendable and moddable Minecraft: Java Edition server in C#.**

## Current state
While clients can connect to the server and interact with the world, keep in mind the project is in its very early stage. Player can connect, break/place blocks, use inventories, drop items, use commands, chat and it all will be saved onto disk. It's compatible with MCJE 1.19.4-1.20.1 using a version-agnostic protocol abstraction and we also have modding, including custom blocks and items.

I primarily design it with API in mind first, and optimizations later, but it doesn't mean we're running slow, especially since server supports NativeAOT. It should be easy and straight-forward to create and implement new content, constructing it from a pre-made components or making own ones if necessary. [Sample mod](SharpMinerals.SampleMod) got a few examples on how can you use existing ones and how to create your own object components.

## Priorities
- Gamemodes
- Better type mapper
- Refactor version-specific code into Vanilla mod
- Feature parity with Vanilla server
- Entity AI components
- Bedrock Edition support
- Runtime generated resource packs
- More async and multi-threading
- Many many more features and enhancements!

## Performance
While it's too early to talk about performance, I still find it impressive to be able to show you this demo, which runs an AOT build:
<video src="https://github.com/user-attachments/assets/5205b21e-dc05-4024-9465-b543e3c99eb5" controls/>

I will focus on performance after finishing basic feature set.

## Quick start
### Building from source
1. Install the [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).
2. Clone the repository:
   ```bash
   git clone https://github.com/TacoGuyAT/SharpMinerals.git
   cd SharpMinerals
   ```
3. Build the solution:
   ```bash
   dotnet build SharpMinerals.sln
   ```
4. Run the server:
   ```bash
   dotnet run --project SharpMinerals.CLI
   ```
- Or run build using NativeAOT:
	```bash
	dotnet publish -p:AOT=true
	```
5. Launch **Minecraft: Java Edition** (version **1.19.4–1.20.1**) and connect to `localhost:25565`.

## Contributions
To help this project you can open issues, submit code or support me with [a small donation](https://ko-fi.com/TacoGuyAT). 

## Credits
- [MiNET](https://github.com/NiclasOlofsson/MiNET), an amazing MCBE server written in C#. It was the biggest inspiration for this project (and perhaps, think of it as a love letter).
- [Arch](https://github.com/genaray/Arch), an awesome and fast ECS implementation in C#.
- [Tyler Kennedy](https://tkte.ch/), [#mcdevs](https://minecraft.wiki/w/Minecraft_Wiki:Projects/wiki.vg_merge/MCDevs) and [minecraft.wiki](https://minecraft.wiki/w/Java_Edition_protocol/Packets) for protocol documentation.
- [PrismarineJS](https://github.com/PrismarineJS/) for [minecraft-data](https://github.com/PrismarineJS/minecraft-data/), like blocks, items, entities, protocol and much more.
- [FabricMC](https://github.com/FabricMC/) for [Yarn](https://github.com/FabricMC/yarn) mappings
- [Henrik Kniberg's "Minecraft terrain generation in a nutshell"](https://www.youtube.com/watch?v=CSa5O6knuwI)
- [Shukoloton's "The History of Minecraft World Generation"](https://www.youtube.com/watch?v=0QLywbih-Wg)
- [HarmonyX](https://github.com/BepInEx/HarmonyX/) for runtime patching
- [RocksDB](https://github.com/facebook/rocksdb/) & its [C# wrapper](https://github.com/curiosity-ai/rocksdb-sharp)
- [ZLogger](https://github.com/Cysharp/ZLogger), [ZLinq](https://github.com/Cysharp/ZLinq) and [Kokuban](https://github.com/Cysharp/Kokuban) by Cysharp
- [Brigadier.NET](https://github.com/AtomicBlom/Brigadier.NET), a port of Mojang's Brigadier command parsing and dispatching library
- [FastCache](https://github.com/jitbit/fastcache)

## License

Apache License 2.0 — see [LICENSE](LICENSE) and [NOTICE](NOTICE).

> Minecraft is a trademark of Mojang Synergies AB. SharpMinerals is an independent project, not
> affiliated with or endorsed by Mojang or Microsoft.

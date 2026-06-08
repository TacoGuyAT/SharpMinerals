# SharpMinerals [![License: Apache 2.0](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE) [![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/) [![Status: early / WIP](https://img.shields.io/badge/status-early%20%2F%20WIP-orange.svg)](#status) [![Discord](https://img.shields.io/discord/1513003669518422129?logo=discord)](https://discord.gg/2BdMk9MvE2)
**An experimental ECS-based voxel simulation engine and framework, primarily focusing on implementing a future-proof, extendable and moddable Minecraft: Java Edition server in C#.**

## Current state
While clients can connect to the server and interact with the world, keep in mind the project is in its very early stage. Player can connect, break/place blocks, use inventories, drop items, use commands, chat and it all will be saved onto disk. It's compatible with MCJE 1.19.4-1.20.1 using a version-agnostic protocol abstraction and we also have modding, including custom blocks and items.

I primarily design it with API in mind first, and optimizations later, but it doesn't mean we're running slow, especially since server supports NativeAOT. It should be easy and straight-forward to create and implement new content, constructing it from a pre-made components or making own ones if necessary. [Sample mod](SharpMinerals.SampleMod) got a few examples on how can you use existing ones and how to create your own object components.

## Roadmap
- Feature parity with Vanilla server (see [#1](https://github.com/TacoGuyAT/SharpMinerals/issues/1))
- Runtime generated resource packs
- Bedrock Edition support
- More async and multi-threading

# Performance
TODO

## Quick start
TODO: download release, double click, connect

## Writing a mod
TODO

# Contributions
To help this project you can open issues, submit code or support me with [a small donation](). 

## Credits
- [MiNET](https://github.com/NiclasOlofsson/MiNET), an amazing MCBE server written in C#. It was the biggest inspiration for this project (and perhaps, think of it as a love letter).
- [Arch](https://github.com/genaray/Arch), an awesome and fast ECS implementation in C#.
- wiki.vg and [minecraft.wiki](https://minecraft.wiki/w/Java_Edition_protocol/Packets) for protocol documentation.
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

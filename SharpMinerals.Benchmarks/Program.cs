using BenchmarkDotNet.Running;
using SharpMinerals.Benchmarks;

// Prove the two strategies compute the identical field before timing them, so the numbers compare equal work.
Validation.AssertModesAgree();

// `dotnet run -c Release` runs every benchmark; pass --filter '*' or a name to narrow.
BenchmarkSwitcher.FromAssembly(typeof(Validation).Assembly).Run(args);

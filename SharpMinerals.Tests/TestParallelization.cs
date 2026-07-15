using Xunit;

// Several tests drive the server's join/world-switch flow, which fires fire-and-forget async-void column
// streaming that completes on background chunk-load threads. Running collections in parallel saturates the
// thread pool and makes that streaming's timing unpredictable (a test asserting on freshly-streamed columns
// could observe them late). Disabling cross-collection parallelism keeps the pool free so the in-test
// Settle() pump reliably drives the streaming to completion. Tests within a class still run sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

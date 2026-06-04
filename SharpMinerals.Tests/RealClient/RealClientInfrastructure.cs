#if TEST_HARNESS
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace SharpMinerals.Tests.RealClient;

/// <summary>The single collection that shares one <see cref="RealClientFixture"/> (one server, one connected
/// client) and runs its tests serially - there is only one client to drive.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RealClientCollection : ICollectionFixture<RealClientFixture> {
    public const string Name = "RealClient";
}

/// <summary>A <see cref="FactAttribute"/> that skips unless <c>SHARPMINERALS_REALCLIENT=1</c>, so the
/// real-client suite stays inert during a normal <c>dotnet test</c> (which has no client to wait for).</summary>
public sealed class RealClientFactAttribute : FactAttribute {
    public RealClientFactAttribute() {
        if (!RealClientFixture.Enabled)
            Skip = $"real-client test; set {RealClientFixture.EnableVar}=1 and connect a SharpTester client to run";
    }
}

/// <summary>Orders a class's tests by their <see cref="OrderAttribute"/> so the "delete the previous world"
/// chain is deterministic (unordered xUnit facts would make "previous" meaningless).</summary>
public sealed class OrderedTestCaseOrderer : ITestCaseOrderer {
    public const string Type = "SharpMinerals.Tests.RealClient.OrderedTestCaseOrderer";
    public const string Assembly = "SharpMinerals.Tests";

    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase =>
        testCases.OrderBy(tc =>
            tc.TestMethod.Method.GetCustomAttributes(typeof(OrderAttribute).AssemblyQualifiedName)
              .FirstOrDefault()?.GetNamedArgument<int>(nameof(OrderAttribute.Order)) ?? int.MaxValue);
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OrderAttribute : Attribute {
    public int Order { get; }
    public OrderAttribute(int order) => Order = order;
}
#endif

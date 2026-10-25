using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

// One host at a time: fixtures manage the DOOPASS_HOME environment variable,
// so test classes must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Doopass.Api.Tests;

/// <summary>
/// The vault store makes test flows stateful: numbered test method names
/// (T01..., T02...) pin the execution order inside a class; this orderer
/// sorts cases by name. Apply it per class with
/// <c>[TestCaseOrderer("Doopass.Api.Tests.AlphabeticalTestCaseOrderer", "Doopass.Api.Tests")]</c>.
/// </summary>
public sealed class AlphabeticalTestCaseOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase =>
        testCases.OrderBy(testCase => testCase.TestMethod.Method.Name, StringComparer.Ordinal);
}

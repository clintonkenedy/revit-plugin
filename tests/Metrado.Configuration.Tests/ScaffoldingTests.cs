namespace Metrado.Configuration.Tests;

/// <summary>
/// PR 1 delivers build wiring, not behaviour. Asserting that the harness is
/// actually bound to the project it covers means later PRs fail for real
/// reasons instead of for a reference that was never hooked up.
/// </summary>
public sealed class ScaffoldingTests
{
    [Fact]
    public void ProjectUnderTestIsPresentInTheTestOutput()
    {
        string assembly = Path.Combine(AppContext.BaseDirectory, "Metrado.Configuration.dll");

        Assert.True(File.Exists(assembly), $"Expected the project under test at {assembly}.");
    }
}

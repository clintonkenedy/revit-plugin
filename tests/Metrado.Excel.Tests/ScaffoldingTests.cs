namespace Metrado.Excel.Tests;

/// <summary>
/// PR 1 delivers build wiring, not behaviour. Besides binding the harness to
/// the project it covers, this pins that the ClosedXML dependency actually
/// lands next to it: decision D7 makes the deployed assembly closure a
/// correctness concern, and a package that never reaches the output folder is
/// the first way that closure breaks.
/// </summary>
public sealed class ScaffoldingTests
{
    [Theory]
    [InlineData("Metrado.Excel.dll")]
    [InlineData("ClosedXML.dll")]
    public void ExpectedAssemblyIsPresentInTheTestOutput(string fileName)
    {
        string assembly = Path.Combine(AppContext.BaseDirectory, fileName);

        Assert.True(File.Exists(assembly), $"Expected {fileName} at {assembly}.");
    }
}

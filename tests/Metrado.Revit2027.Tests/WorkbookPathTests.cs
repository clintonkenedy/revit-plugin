using System.Globalization;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins where the workbook goes: beside the model, under a name carrying the
/// export's date and time, and never onto an existing file. Estimators fill
/// unit prices into an exported workbook, so replacing one would destroy their
/// work.
/// </summary>
public sealed class WorkbookPathTests
{
    private static readonly DateTime Noon = new(2026, 9, 23, 14, 30, 5);

    private const string Model = @"C:\Projects\Office\Office Building.rvt";

    [Fact]
    public void TheWorkbookGoesBesideTheModelNamedForItAndTheExportTime()
    {
        Assert.Equal(@"C:\Projects\Office\Office Building - metrado 2026-09-23 1430.xlsx", WorkbookPath.For(Model, Noon, _ => false));
    }

    [Fact]
    public void AnExistingWorkbookIsNeverChosenAgain()
    {
        HashSet<string> taken =
        [
            @"C:\Projects\Office\Office Building - metrado 2026-09-23 1430.xlsx",
            @"C:\Projects\Office\Office Building - metrado 2026-09-23 1430 (2).xlsx",
        ];

        Assert.Equal(@"C:\Projects\Office\Office Building - metrado 2026-09-23 1430 (3).xlsx", WorkbookPath.For(Model, Noon, taken.Contains));
    }

    [Fact]
    public void TheWarningsListIsNamedForItsWorkbook()
    {
        Assert.Equal(
            @"C:\Projects\Office\Office Building - metrado 2026-09-23 1430 (2) - warnings.txt",
            WorkbookPath.WarningsFor(@"C:\Projects\Office\Office Building - metrado 2026-09-23 1430 (2).xlsx"));
    }

    /// <summary>A warnings list left without its workbook still holds the name: the pair is never split across two exports.</summary>
    [Fact]
    public void ALeftoverWarningsListAlsoTakesTheName()
    {
        HashSet<string> taken = [@"C:\Projects\Office\Office Building - metrado 2026-09-23 1430 - warnings.txt"];

        Assert.Equal(@"C:\Projects\Office\Office Building - metrado 2026-09-23 1430 (2).xlsx", WorkbookPath.For(Model, Noon, taken.Contains));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("BIM 360://Project/Office Building.rvt")]
    public void AModelWithNoLocalFolderHasNoWorkbookPath(string? modelPath)
    {
        Assert.Null(WorkbookPath.For(modelPath, Noon, _ => false));
    }

    /// <summary>The date is the Gregorian one whatever the machine's culture: a Persian locale would otherwise write 1405.</summary>
    [Fact]
    public void TheDateIgnoresTheMachinesCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fa-IR");

            Assert.EndsWith("metrado 2026-09-23 1430.xlsx", WorkbookPath.For(Model, Noon, _ => false));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}

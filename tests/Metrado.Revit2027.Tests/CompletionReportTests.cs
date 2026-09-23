using Metrado.Domain;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins what the completion dialog tells the estimator, which the spec
/// requires to be visible without opening the workbook: the exported total,
/// the unclassified count, the criteria in force with their thresholds and
/// modes, whether a criteria file was found, and the warnings.
/// </summary>
public sealed class CompletionReportTests
{
    private const string Workbook = @"C:\Projects\Office\Office Building - metrado 2026-09-23 1430.xlsx";

    [Fact]
    public void ItNamesTheWorkbookItWrote()
    {
        Assert.Contains(Workbook, CompletionReport.For(Report(lines: 4, unclassified: 1), Defaults(), Workbook).Summary);
    }

    /// <summary>
    /// The run report counts every line and the budget sheet counts the coded
    /// ones: the dialog names both, so "4" and "3" stop looking like a
    /// contradiction.
    /// </summary>
    [Fact]
    public void ItSplitsTheExportedLinesIntoCodedAndUnclassified()
    {
        string summary = CompletionReport.For(Report(lines: 4, unclassified: 1), Defaults(), Workbook).Summary;

        Assert.Contains("4 measurement lines", summary);
        Assert.Contains("3 coded into partidas", summary);
        Assert.Contains("1 unclassified", summary);
    }

    [Fact]
    public void AnEmptyRunSaysNothingMeasurableWasFound()
    {
        Assert.Contains("No measurable elements were found", CompletionReport.For(Report(lines: 0, unclassified: 0), Defaults(), Workbook).Summary);
    }

    [Fact]
    public void WithoutACriteriaFileItSaysSoAndThatDefaultsApplied()
    {
        string summary = CompletionReport.For(Report(lines: 1, unclassified: 0), Defaults(), Workbook).Summary;

        Assert.Contains("No criteria file was found", summary);
        Assert.Contains(CriteriaFileLocator.FileName, summary);
        Assert.Contains("built-in", summary);
    }

    [Fact]
    public void WithACriteriaFileItNamesTheFile()
    {
        EffectiveCriteria fromFile = new(CriteriaSet.Default, ConfigSource.File, @"C:\Projects\Office\metrado.criteria.json");

        string summary = CompletionReport.For(Report(lines: 1, unclassified: 0), fromFile, Workbook).Summary;

        Assert.Contains(@"C:\Projects\Office\metrado.criteria.json", summary);
        Assert.DoesNotContain("No criteria file was found", summary);
    }

    [Theory]
    [InlineData(BoundaryMode.Exclusive, "smaller than 1 m2")]
    [InlineData(BoundaryMode.Inclusive, "up to 1 m2")]
    public void EachCriterionShowsItsThresholdAndMode(BoundaryMode mode, string wording)
    {
        EffectiveCriteria criteria = new(
            CriteriaSet.Merge(CriteriaSet.Default, [new CategoryOverride("Walls", mode: mode)]).Value,
            ConfigSource.File,
            @"C:\Projects\Office\metrado.criteria.json");

        string summary = CompletionReport.For(Report(lines: 1, unclassified: 0), criteria, Workbook).Summary;

        Assert.Contains("Walls", summary);
        Assert.Contains(wording, summary);
    }

    [Fact]
    public void EveryWarningIsListedInTheDetails()
    {
        RunReport report = Report(lines: 2, unclassified: 0, warnings: ["first condition", "second condition"]);

        CompletionReport.Text text = CompletionReport.For(report, Defaults(), Workbook);

        Assert.Contains("2 warnings", text.Summary);
        Assert.Contains("first condition", text.Details);
        Assert.Contains("second condition", text.Details);
    }

    [Fact]
    public void ARunWithoutWarningsSaysSo()
    {
        Assert.Contains("No warnings", CompletionReport.For(Report(lines: 1, unclassified: 0), Defaults(), Workbook).Summary);
    }

    private static EffectiveCriteria Defaults() => new(CriteriaSet.Default, ConfigSource.BuiltInDefaults, path: null);

    private static RunReport Report(int lines, int unclassified, IReadOnlyList<string>? warnings = null) =>
        new(lines, unclassified, [],
            [.. (warnings ?? []).Select(condition => new ValidationWarning("id", "Walls", "Basic Wall", "Generic", condition))]);
}

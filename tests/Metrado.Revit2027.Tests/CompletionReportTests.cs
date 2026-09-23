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
    /// The budget sheet's first row reads "Measurement lines exported" and
    /// counts the coded lines. The dialog uses that phrase for the same
    /// number, and names the unclassified elements separately, so the two
    /// never give one label two values.
    /// </summary>
    [Fact]
    public void ItCountsExportedLinesAsTheBudgetSheetDoes()
    {
        string summary = CompletionReport.For(Report(lines: 4, unclassified: 1), Defaults(), Workbook).Summary;

        Assert.Contains("3 measurement lines exported to the budget sheet", summary);
        Assert.Contains("1 element listed as unclassified", summary);
        Assert.DoesNotContain("4 measurement lines", summary);
    }

    [Fact]
    public void OneLineIsNotCalledLines()
    {
        Assert.Contains("1 measurement line exported", CompletionReport.For(Report(lines: 1, unclassified: 0), Defaults(), Workbook).Summary);
    }

    /// <summary>The Pacific sample's shape: every element measured, none coded.</summary>
    [Fact]
    public void AnAllUnclassifiedRunIsNotReportedAsEmpty()
    {
        string summary = CompletionReport.For(Report(lines: 104, unclassified: 104), Defaults(), Workbook).Summary;

        Assert.Contains("0 measurement lines exported to the budget sheet", summary);
        Assert.Contains("104 elements listed as unclassified", summary);
        Assert.DoesNotContain("No measurable elements", summary);
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
        Assert.Contains("built-in criteria are in force", summary);
    }

    [Fact]
    public void WithACriteriaFileItNamesTheFile()
    {
        EffectiveCriteria fromFile = new(CriteriaSet.Default, ConfigSource.File, @"C:\Projects\Office\metrado.criteria.json");

        string summary = CompletionReport.For(Report(lines: 1, unclassified: 0), fromFile, Workbook).Summary;

        Assert.Contains(@"C:\Projects\Office\metrado.criteria.json", summary);
        Assert.Contains("over the built-in criteria", summary);
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

    /// <summary>A threshold other than the default, with its sources: nothing in the line is fixed text.</summary>
    [Fact]
    public void ACriterionLineCarriesItsOwnThresholdAndSources()
    {
        EffectiveCriteria criteria = new(
            CriteriaSet.Merge(CriteriaSet.Default, [new CategoryOverride("Walls", threshold: 0.5)]).Value,
            ConfigSource.File,
            @"C:\Projects\Office\metrado.criteria.json");

        string summary = CompletionReport.For(Report(lines: 1, unclassified: 0), criteria, Workbook).Summary;

        Assert.Contains("smaller than 0.5 m2", summary);
        Assert.Contains("HOST_AREA_COMPUTED", summary);
    }

    /// <summary>A zero exclusive threshold adds nothing back; "smaller than 0 m" says so only to a mathematician.</summary>
    [Fact]
    public void AZeroThresholdIsDescribedAsDeductingEveryOpening()
    {
        string summary = CompletionReport.For(Report(lines: 1, unclassified: 0), Defaults(), Workbook).Summary;

        Assert.Contains("Railings: measured in m from CURVE_ELEM_LENGTH; every opening is deducted.", summary);
        Assert.DoesNotContain("smaller than 0", summary);
    }

    /// <summary>A counted category reads no source and applies no openings rule; the dialog says it is counted.</summary>
    [Fact]
    public void ACountedCategoryIsDescribedAsCounted()
    {
        string summary = CompletionReport.For(Report(lines: 1, unclassified: 0), Defaults(), Workbook).Summary;

        Assert.Contains("  Doors: counted in u, one per instance.", summary);
        Assert.Contains("  Windows: counted in u, one per instance.", summary);
        Assert.DoesNotContain("from ;", summary);
    }

    [Fact]
    public void EveryWarningIsListedInTheDetails()
    {
        RunReport report = Report(lines: 2, unclassified: 0, warnings: ["first condition", "second condition"]);

        CompletionReport.Text text = CompletionReport.For(report, Defaults(), Workbook);

        Assert.Contains("2 warnings", text.Summary);
        Assert.Contains("first condition", text.Details);
        Assert.Contains("second condition", text.Details);
        Assert.Contains("element-1", text.Details);
        Assert.Contains("element-2", text.Details);
    }

    [Fact]
    public void ARunWithoutWarningsSaysSoAndHasNoList()
    {
        CompletionReport.Text text = CompletionReport.For(Report(lines: 1, unclassified: 0), Defaults(), Workbook);

        Assert.Contains("No warnings", text.Summary);
        Assert.Null(text.WarningsList);
    }

    /// <summary>
    /// A long list runs past the dialog's reach and closes with it; the list
    /// kept beside the workbook is the one the estimator works through.
    /// </summary>
    [Fact]
    public void TheSummaryNamesTheWarningsListBesideTheWorkbook()
    {
        string summary = CompletionReport.For(Report(lines: 1, unclassified: 0, warnings: ["a condition"]), Defaults(), Workbook).Summary;

        Assert.Contains(WorkbookPath.WarningsFor(Workbook), summary);
    }

    /// <summary>Opened on its own, days later, the list still says which export it belongs to and what was decided.</summary>
    [Fact]
    public void TheWarningsListCarriesTheSummaryAndEveryWarning()
    {
        CompletionReport.Text text = CompletionReport.For(
            Report(lines: 2, unclassified: 0, warnings: ["first condition", "second condition"]), Defaults(), Workbook);

        Assert.NotNull(text.WarningsList);
        Assert.StartsWith(text.Summary, text.WarningsList);
        Assert.Contains("- Walls element-1 (Generic): first condition", text.WarningsList);
        Assert.Contains("- Walls element-2 (Generic): second condition", text.WarningsList);
    }

    private static EffectiveCriteria Defaults() => new(CriteriaSet.Default, ConfigSource.BuiltInDefaults, path: null);

    private static RunReport Report(int lines, int unclassified, IReadOnlyList<string>? warnings = null) =>
        new(lines, unclassified, [],
            [.. (warnings ?? []).Select((condition, index) => new ValidationWarning($"element-{index + 1}", "Walls", "Basic Wall", "Generic", condition))]);
}

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

        Assert.Contains("3 measurement lines exported to the budget sheet; 1 element listed as unclassified.", summary);
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

    /// <summary>A layered element puts a line per material on the Unclassified sheet: lines and elements are counted apart.</summary>
    [Theory]
    [InlineData(5, 2, "5 lines of 2 elements listed as unclassified")]
    [InlineData(3, 1, "3 lines of 1 element listed as unclassified")]
    public void UnclassifiedLinesOfFewerElementsAreCountedApart(int unclassified, int elements, string wording)
    {
        string summary = CompletionReport.For(Report(lines: 10, unclassified: unclassified, elements: elements), Defaults(), Workbook).Summary;

        Assert.Contains($"{10 - unclassified} measurement lines exported to the budget sheet; {wording}.", summary);
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

    /// <summary>A category taken off by layer says so, how each function is measured, and how an element that cannot be is.</summary>
    [Fact]
    public void ALayeredCriterionSaysHowItIsTakenOff()
    {
        string summary = CompletionReport.For(Report(lines: 1, unclassified: 0), Layered("Walls", LayerFunction.Structure, LayerFunction.Substrate), Workbook).Summary;

        Assert.Contains(
            "  Walls: by material layer, one line per material, every function in m2 except Structure and Substrate in m3; "
            + "openings smaller than 1 m2 are not deducted (exclusive), each m2 line getting each one's area back once per layer it covers unless a warning says otherwise, "
            + "and each m3 line keeping Revit's deduction; an element whose layers do not account for it is measured whole, in m2 from HOST_AREA_COMPUTED.",
            summary);
    }

    /// <summary>One function in m3, as the host runs had it, is named alone; any function can be.</summary>
    [Theory]
    [InlineData(LayerFunction.Structure, "every function in m2 except Structure in m3;")]
    [InlineData(LayerFunction.Finish1, "every function in m2 except Finish1 in m3;")]
    [InlineData(LayerFunction.StructuralDeck, "every function in m2 except StructuralDeck in m3;")]
    public void OneFunctionInM3IsNamedAlone(LayerFunction function, string wording)
    {
        string summary = CompletionReport.For(Report(lines: 1, unclassified: 0), Layered("Walls", function), Workbook).Summary;

        Assert.Contains(wording, summary);
        Assert.Contains("and each m3 line keeping Revit's deduction", summary);
    }

    /// <summary>With every function in m2 no line keeps a deduction by volume; with every opening deducted there is nothing to give back.</summary>
    [Fact]
    public void ALayeredCriterionSaysOnlyWhatApplies()
    {
        EffectiveCriteria criteria = new(
            CriteriaSet.Merge(CriteriaSet.Default, [new CategoryOverride("Floors", threshold: 0, layers: LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit>()))]).Value,
            ConfigSource.File,
            @"C:\Projects\Office\metrado.criteria.json");

        string summary = CompletionReport.For(Report(lines: 1, unclassified: 0), criteria, Workbook).Summary;

        Assert.Contains("  Floors: by material layer, one line per material, every function in m2; every opening is deducted; an element whose layers", summary);
        Assert.DoesNotContain("m3 line", summary);
    }

    /// <summary>With every function in m2, openings are given back and no line keeps a deduction by volume.</summary>
    [Fact]
    public void ALayeredCriterionInSquareMetresNamesNoM3Line()
    {
        string summary = CompletionReport.For(Report(lines: 1, unclassified: 0), Layered("Roofs"), Workbook).Summary;

        Assert.Contains(
            "  Roofs: by material layer, one line per material, every function in m2; openings smaller than 1 m2 are not deducted (exclusive), "
            + "each m2 line getting each one's area back once per layer it covers unless a warning says otherwise; an element whose layers",
            summary);
        Assert.DoesNotContain("m3 line", summary);
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

    /// <summary>"The user is informed that fifteen warnings were raised" (task 3.4), and one warning is not called warnings.</summary>
    [Theory]
    [InlineData(15, "15 warnings: see the details below")]
    [InlineData(1, "1 warning: see the details below")]
    public void TheWarningsRaisedAreAnnouncedByTheirCount(int count, string wording)
    {
        string summary = CompletionReport.For(
            Report(lines: 2, unclassified: 0, warnings: [.. Enumerable.Range(1, count).Select(index => $"condition {index}")]), Defaults(), Workbook).Summary;

        Assert.Contains(wording, summary);
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

    private static EffectiveCriteria Layered(string category, params LayerFunction[] cubic) =>
        new(
            CriteriaSet.Merge(CriteriaSet.Default, [new CategoryOverride(category, layers: LayerOverride.On(cubic.ToDictionary(function => function, _ => QuantityUnit.CubicMetre)))]).Value,
            ConfigSource.File,
            @"C:\Projects\Office\metrado.criteria.json");

    private static RunReport Report(int lines, int unclassified, IReadOnlyList<string>? warnings = null, int? elements = null) =>
        new(lines, unclassified, [],
            [.. (warnings ?? []).Select((condition, index) => new ValidationWarning($"element-{index + 1}", "Walls", "Basic Wall", "Generic", condition))])
        {
            UnclassifiedElements = elements ?? unclassified,
        };
}

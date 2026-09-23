using Metrado.Configuration;
using Metrado.Domain;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins the export's composition as the command runs it: measure each element
/// under its category's criterion, codify it, group the lines, report the run.
///
/// It follows the stage order of the integration suite's test-side
/// <c>ExportPipeline</c>, and departs from it only where the shipped command
/// must: an element that cannot be measured is reported and the run goes on,
/// and extraction's own warnings reach the report.
/// </summary>
public sealed class TakeoffExportTests
{
    [Fact]
    public void AMeasuredWallBecomesALineUnderItsCode()
    {
        TakeoffExport.Outcome outcome = TakeoffExport.Run(Defaults(), [Wall("w1", "B2010", area: 18.0)], []);

        Partida partida = Assert.Single(outcome.Result.Partidas);
        Assert.Equal("B2010", partida.Key.PartidaCode);
        Assert.Equal(1, outcome.Report.ExportedLines);
        Assert.Equal(0, outcome.Report.UnclassifiedCount);
    }

    /// <summary>
    /// The integration suite's pipeline throws here, because its fixtures are
    /// all measurable. A real model is not: the wall is reported, the rest of
    /// the budget is still written.
    /// </summary>
    [Fact]
    public void AWallWithNoQuantityIsReportedAndTheRunGoesOn()
    {
        TakeoffExport.Outcome outcome = TakeoffExport.Run(
            Defaults(),
            [Wall("unreadable", "B2010", area: null), Wall("w2", "B2010", area: 10.0)],
            []);

        Assert.Equal(1, outcome.Report.ExportedLines);
        Assert.Contains(outcome.Report.Warnings, warning => warning.UniqueId == "unreadable");
    }

    [Fact]
    public void ExtractionsOwnWarningsReachTheReport()
    {
        ElementTakeoff wall = Wall("w1", "B2010", area: 18.0);
        ValidationWarning unmeasured = ValidationWarning.ForElement(wall, "Opening x cuts this wall but its area could not be measured.");

        TakeoffExport.Outcome outcome = TakeoffExport.Run(Defaults(), [wall], [unmeasured]);

        Assert.Contains(unmeasured, outcome.Report.Warnings);
    }

    [Fact]
    public void AnElementOfACategoryWithNoCriterionIsReportedNotMeasured()
    {
        ElementTakeoff floor = Wall("f1", "B1010", area: 30.0) with { CategoryName = "Floors" };

        TakeoffExport.Outcome outcome = TakeoffExport.Run(Defaults(), [floor], []);

        Assert.Equal(0, outcome.Report.ExportedLines);
        ValidationWarning warning = Assert.Single(outcome.Report.Warnings);
        Assert.Equal("f1", warning.UniqueId);
        Assert.Contains("Floors", warning.Condition);
    }

    /// <summary>
    /// An outline measured a hair short of an opening's true deduction flips
    /// it across the threshold. Openings within the band of the threshold are
    /// flagged as possibly misclassified; their numbers are left alone.
    /// </summary>
    /// <remarks>
    /// The expected metrado is the rule's own (raw 18.0, default 1.0 exclusive):
    /// an opening below the threshold is added back, one at or above it is not.
    /// </remarks>
    [Theory]
    [InlineData(0.995, true, 18.995)]
    [InlineData(1.0, true, 18.0)]
    [InlineData(1.009, true, 18.0)]
    [InlineData(0.98, false, 18.98)]
    [InlineData(1.011, false, 18.0)]
    public void AnOpeningNearTheThresholdIsFlaggedWithoutChangingItsNumber(double opening, bool flagged, double metrado)
    {
        ElementTakeoff wall = Wall("w1", "B2010", area: 18.0, openings: [("window", opening)]);

        TakeoffExport.Outcome outcome = TakeoffExport.Run(Defaults(), [wall], []);

        Assert.Equal(flagged, outcome.Report.Warnings.Any(warning => warning.Condition.Contains("window")));
        Assert.Equal(metrado, outcome.Result.Partidas[0].Total.Value, precision: 9);
    }

    /// <summary>
    /// The review's case: 0.99997 and 1.0 both used to print as "1 m2", yet
    /// the rule adds the first back and keeps the second deducted. The
    /// warning now gives the value in full and says which way the rule went.
    /// </summary>
    [Theory]
    [InlineData(BoundaryMode.Exclusive, 0.99997, "0.99997", "added it back", "threshold (exclusive)")]
    [InlineData(BoundaryMode.Exclusive, 1.0, "measures 1 m2", "kept it deducted", "threshold (exclusive)")]
    [InlineData(BoundaryMode.Inclusive, 1.00004, "1.00004", "kept it deducted", "threshold (inclusive)")]
    [InlineData(BoundaryMode.Inclusive, 1.0, "measures 1 m2", "added it back", "threshold (inclusive)")]
    public void TheBandWarningStatesTheValueAndTheRulesDecision(BoundaryMode mode, double opening, string value, string decision, string convention)
    {
        EffectiveCriteria criteria = new(
            CriteriaSet.Merge(CriteriaSet.Default, [new CategoryOverride("Walls", mode: mode)]).Value,
            ConfigSource.File,
            @"C:\Projects\Office\metrado.criteria.json");

        TakeoffExport.Outcome outcome = TakeoffExport.Run(criteria, [Wall("w1", "B2010", area: 18.0, openings: [("window", opening)])], []);

        ValidationWarning band = Assert.Single(outcome.Report.Warnings, warning => warning.Condition.Contains("window"));
        Assert.Contains(value, band.Condition);
        Assert.Contains(decision, band.Condition);
        Assert.Contains(convention, band.Condition);
    }

    /// <summary>A wall the rule never measured decided nothing about its openings.</summary>
    [Fact]
    public void AnUnmeasuredWallRaisesNoBandWarning()
    {
        TakeoffExport.Outcome outcome = TakeoffExport.Run(
            Defaults(), [Wall("no-area", "B2010", area: null, openings: [("window", 0.999)])], []);

        Assert.DoesNotContain(outcome.Report.Warnings, warning => warning.Condition.Contains("window"));
    }

    /// <summary>
    /// The criteria in force govern the metrado, not the built-in ones: under
    /// a 2.0 m2 threshold a 1.5 m2 opening is added back, where the defaults
    /// would have kept it deducted.
    /// </summary>
    [Fact]
    public void TheSuppliedCriteriaGovernTheMetrado()
    {
        TakeoffExport.Outcome outcome = TakeoffExport.Run(
            Supplied(threshold: 2.0), [Wall("w1", "B2010", area: 18.0, openings: [("window", 1.5)])], []);

        Assert.Equal(19.5, outcome.Result.Partidas[0].Total.Value, precision: 9);
    }

    /// <summary>The band surrounds the threshold in force, not the built-in one.</summary>
    [Fact]
    public void TheBandFollowsTheSuppliedThreshold()
    {
        EffectiveCriteria supplied = Supplied(threshold: 2.0);
        ElementTakeoff nearTwo = Wall("w1", "B2010", area: 18.0, openings: [("near-two", 1.995)]);
        ElementTakeoff nearOne = Wall("w2", "B2010", area: 18.0, openings: [("near-one", 0.995)]);

        TakeoffExport.Outcome outcome = TakeoffExport.Run(supplied, [nearTwo, nearOne], []);

        Assert.Contains(outcome.Report.Warnings, warning => warning.Condition.Contains("near-two"));
        Assert.DoesNotContain(outcome.Report.Warnings, warning => warning.Condition.Contains("near-one"));
    }

    private static EffectiveCriteria Defaults() => CriteriaResolver.Resolve(CriteriaFileLookup.Absent, path: null).Value;

    private static EffectiveCriteria Supplied(double threshold)
    {
        CriteriaSet merged = CriteriaSet.Merge(
            CriteriaSet.Default,
            [new CategoryOverride("Walls", threshold: threshold)]).Value;
        return new EffectiveCriteria(merged, ConfigSource.File, @"C:\Projects\Office\metrado.criteria.json");
    }

    private static ElementTakeoff Wall(
        string uniqueId, string code, double? area, IReadOnlyList<(string Id, double Area)>? openings = null) =>
        new(
            UniqueId: uniqueId,
            CategoryName: "Walls",
            FamilyName: "Basic Wall",
            TypeName: "Generic - 200mm",
            TypeKey: "type",
            Codes: new CodificationReadings(code, keynote: null, sharedParameters: new Dictionary<string, string?>()),
            Quantities: area is double value ? [new RawQuantity("HOST_AREA_COMPUTED", new Quantity(value, QuantityUnit.SquareMetre))] : [],
            Openings: [.. (openings ?? []).Select(o => new OpeningQuantity(o.Id, new Quantity(o.Area, QuantityUnit.SquareMetre)))]);
}

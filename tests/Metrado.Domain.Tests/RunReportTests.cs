namespace Metrado.Domain.Tests;

/// <summary>
/// The completion report: what the run exported, how much of it the user still
/// has to classify, and the convention that was actually applied.
/// </summary>
public sealed class RunReportTests
{
    [Fact]
    public void TheReportCountsEveryExportedLineAndHowManyOfThemAreUnclassified()
    {
        TakeoffResult result = TakeoffResult.Group(
        [
            .. TakeoffFixture.Instances("WT-A", count: 28, partidaCode: "C1010", amount: 10.0),
            .. TakeoffFixture.Instances(
                "WT-B",
                count: 12,
                partidaCode: UnclassifiedResolver.Code,
                amount: 5.0),
        ]);

        RunReport report = RunReport.For(result, []);

        Assert.Equal(40, report.ExportedLines);
        Assert.Equal(12, report.UnclassifiedCount);
    }

    [Fact]
    public void AModelInWhichEverythingResolvedReportsAnUnclassifiedCountOfZero()
    {
        TakeoffResult result = TakeoffResult.Group(
            [.. TakeoffFixture.Instances("WT-A", count: 3, partidaCode: "C1010", amount: 10.0)]);

        RunReport report = RunReport.For(result, []);

        Assert.Equal(3, report.ExportedLines);
        Assert.Equal(0, report.UnclassifiedCount);
    }

    [Fact]
    public void TheReportRepeatsTheConventionThatWasAppliedNotTheOneThatWasConfigured()
    {
        OpeningsThreshold configured = CriteriaSet.Default.ByCategory["Walls"].Threshold;

        Linea measured = new(
            MeasurementFixture.Wall("wall-1"),
            "C1010",
            TakeoffFixture.Measured(
                10.0,
                appliedMode: BoundaryMode.Inclusive,
                appliedThreshold: 2.5));

        RunReport report = RunReport.For(TakeoffResult.Group([measured]), []);

        AppliedCriterion applied = Assert.Single(report.Applied);

        Assert.Equal("Walls", applied.Capitulo);
        Assert.Equal(QuantityUnit.SquareMetre, applied.Unit);
        Assert.Equal(2.5, applied.Threshold, 9);
        Assert.Equal(BoundaryMode.Inclusive, applied.Mode);

        // The whole point of the assertions above: the built-in configuration for
        // Walls says something else, so a report sourced from the criteria table
        // rather than from the measured lines would disagree with the budget it
        // was printed next to.
        Assert.NotEqual(configured.Value, applied.Threshold);
        Assert.NotEqual(configured.Mode, applied.Mode);
    }

    [Fact]
    public void EachCapituloReportsTheConventionItsOwnLinesWereMeasuredUnder()
    {
        Linea wall = new(
            MeasurementFixture.Wall("wall-1"),
            "C1010",
            TakeoffFixture.Measured(
                10.0,
                appliedMode: BoundaryMode.Exclusive,
                appliedThreshold: 1.0));

        Linea floor = new(
            MeasurementFixture.Wall("floor-1") with { CategoryName = "Floors" },
            "C2020",
            TakeoffFixture.Measured(
                6.0,
                QuantityUnit.CubicMetre,
                BoundaryMode.Inclusive,
                appliedThreshold: 0.5));

        RunReport report = RunReport.For(TakeoffResult.Group([wall, floor]), []);

        Assert.Equal(2, report.Applied.Count);

        AppliedCriterion walls = AppliedTo(report, "Walls");

        Assert.Equal(QuantityUnit.SquareMetre, walls.Unit);
        Assert.Equal(1.0, walls.Threshold, 9);
        Assert.Equal(BoundaryMode.Exclusive, walls.Mode);

        AppliedCriterion floors = AppliedTo(report, "Floors");

        Assert.Equal(QuantityUnit.CubicMetre, floors.Unit);
        Assert.Equal(0.5, floors.Threshold, 9);
        Assert.Equal(BoundaryMode.Inclusive, floors.Mode);
    }

    [Fact]
    public void OneCapituloMeasuredUnderTwoConventionsIsRefusedRatherThanReportedAsOne()
    {
        Linea first = new(
            MeasurementFixture.Wall("wall-1"),
            "C1010",
            TakeoffFixture.Measured(10.0, appliedThreshold: 1.0));

        Linea second = new(
            MeasurementFixture.Wall("wall-2"),
            "C2020",
            TakeoffFixture.Measured(10.0, appliedThreshold: 2.5));

        TakeoffResult result = TakeoffResult.Group([first, second]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => RunReport.For(result, []));

        Assert.Contains("Walls", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReportCarriesTheWholeWarningListAndNotOnlyItsCount()
    {
        TakeoffResult result = TakeoffResult.Group([TakeoffFixture.Line("wall-1", "C1010")]);

        ValidationWarning warning = ValidationWarning.ForElement(
            MeasurementFixture.Wall("wall-1"),
            "No source yielded a value.");

        RunReport report = RunReport.For(result, [warning]);

        Assert.Equal(1, report.WarningCount);
        Assert.Same(warning, Assert.Single(report.Warnings));
    }

    [Fact]
    public void ARunThatRaisedNothingReportsAnEmptyWarningList()
    {
        TakeoffResult result = TakeoffResult.Group([TakeoffFixture.Line("wall-1", "C1010")]);

        RunReport report = RunReport.For(result, []);

        Assert.Empty(report.Warnings);
        Assert.Equal(0, report.WarningCount);
    }

    private static AppliedCriterion AppliedTo(RunReport report, string capitulo) =>
        Assert.Single(
            report.Applied,
            applied => string.Equals(applied.Capitulo, capitulo, StringComparison.Ordinal));
}

namespace Metrado.Domain.Tests;

/// <summary>
/// The empty measurement set: a model that yielded nothing to measure still
/// produces a well-formed result, and the run says so instead of reporting a
/// success that implies quantities were found.
/// </summary>
public sealed class MeasurementEmptySetTests
{
    [Fact]
    public void AModelWithNoMeasurableElementsProducesAWellFormedResultWithZeroLines()
    {
        TakeoffResult result = TakeoffResult.Group([]);

        Assert.Empty(result.Partidas);
        Assert.Equal(0, result.LineCount);
    }

    [Fact]
    public void AResultCountsEveryMeasurementLineAcrossAllItsPartidas()
    {
        TakeoffResult result = TakeoffResult.Group(
        [
            .. TakeoffFixture.Instances("WT-A", count: 3, partidaCode: "C1010", amount: 10.0),
            .. TakeoffFixture.Instances("WT-B", count: 2, partidaCode: "C2020", amount: 4.0),
        ]);

        // Two partidas, five lines: the count is of lines, not of the groups they
        // were collapsed into.
        Assert.Equal(2, result.Partidas.Count);
        Assert.Equal(5, result.LineCount);
    }

    [Fact]
    public void ARunOverAnEmptyModelReportsThatNoMeasurableElementsWereFound()
    {
        RunReport report = RunReport.For(TakeoffResult.Group([]), []);

        // The run completes rather than failing, and states the condition instead
        // of leaving the user to infer it from a zero that reads like a success.
        Assert.True(report.NoMeasurableElements);
        Assert.Equal(0, report.ExportedLines);
        Assert.Equal(0, report.UnclassifiedCount);

        // No line was measured, so there is no convention to claim was applied.
        Assert.Empty(report.Applied);
    }

    [Fact]
    public void ARunThatMeasuredSomethingNeverClaimsNoMeasurableElementsWereFound()
    {
        RunReport report = RunReport.For(
            TakeoffResult.Group([TakeoffFixture.Line("wall-1", "C1010", amount: 10.0)]),
            []);

        Assert.False(report.NoMeasurableElements);
        Assert.Equal(1, report.ExportedLines);
    }

    [Fact]
    public void ElementsNobodyCouldCodeStillCountAsMeasurableElements()
    {
        RunReport report = RunReport.For(
            TakeoffResult.Group(
                [TakeoffFixture.Line("wall-1", UnclassifiedResolver.Code, amount: 6.0)]),
            []);

        // "Nothing was measured" and "nothing could be coded" are different findings
        // with different fixes: the first means the run looked at the wrong model,
        // the second means the model needs codes. Reporting the second as the first
        // would send the user looking for the wrong problem.
        Assert.False(report.NoMeasurableElements);
        Assert.Equal(1, report.ExportedLines);
        Assert.Equal(1, report.UnclassifiedCount);
    }
}

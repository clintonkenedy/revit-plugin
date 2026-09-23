namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins what a smoke run says: a verdict a person reads at a glance, and one
/// journal line per check, because the journal is what outlives the dialog.
/// </summary>
public sealed class SmokeReportTests
{
    private static readonly SmokeCheck Passed = new("isolation", SmokeStatus.Pass, "context 'Metrado.Revit2027'");
    private static readonly SmokeCheck Skipped = new("assembly-code", SmokeStatus.Skip, "no wall type carries a code");
    private static readonly SmokeCheck Failed = new("closure", SmokeStatus.Fail, "ExcelNumberFormat could not be loaded");

    [Fact]
    public void ARunWithNoFailurePassesAndCountsWhatItSkipped()
    {
        Assert.Equal("Smoke passed: 1 passed, 1 skipped", SmokeReport.Verdict([Passed, Skipped]));
    }

    [Fact]
    public void OneFailureFailsTheRun()
    {
        Assert.Equal("Smoke FAILED: 1 failed, 1 passed, 1 skipped", SmokeReport.Verdict([Passed, Skipped, Failed]));
    }

    [Fact]
    public void ARunThatCheckedNothingDoesNotPass()
    {
        Assert.StartsWith("Smoke FAILED", SmokeReport.Verdict([Skipped]));
    }

    [Fact]
    public void EveryCheckHasItsOwnLineWithItsStatus()
    {
        Assert.Equal(
            [
                "PASS isolation: context 'Metrado.Revit2027'",
                "SKIP assembly-code: no wall type carries a code",
                "FAIL closure: ExcelNumberFormat could not be loaded",
            ],
            SmokeReport.Lines([Passed, Skipped, Failed]));
    }
}

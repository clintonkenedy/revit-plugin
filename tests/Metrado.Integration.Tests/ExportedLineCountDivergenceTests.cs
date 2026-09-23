namespace Metrado.Integration.Tests;

/// <summary>
/// The run report and the budget sheet both say how many measurement lines were
/// exported, and for the same run they say different numbers.
/// </summary>
/// <remarks>
/// Four walls are measured. <c>RunReport.ExportedLines</c> reports 4 — it documents
/// itself as "every measurement line in the result, unclassified ones included",
/// the whole run, of which the unclassified figure is a part. The budget
/// sheet states 3 — it counts the measurement rows it actually carries, so the
/// stated figure reconciles with the rows beneath it exactly as the subtotals do,
/// and the uncoded wall is counted on its own sheet.
/// <para>
/// <b>Neither is wrong and this is not a bug report.</b> They answer different
/// questions and each is correct against the block it heads. What was wrong is that
/// they shared a name while task 1.24's completion dialog shows the run report
/// beside a workbook the user then opens. The dialog settles it (PR 17): it reads
/// "3 measurement lines exported to the budget sheet; 1 element listed as
/// unclassified", the sheet's number under the sheet's phrase, and never shows
/// ExportedLines under that phrase.
/// </para>
/// <para>
/// This was recorded as an open risk when the writer's summary row was added, on
/// the reasoning that the two numbers would eventually meet. This suite is the
/// first place they are produced by one run, so it is the first place the
/// divergence is a fact rather than a prediction — pinned here so that changing
/// either number is a deliberate decision with a failing test attached, and so
/// that 1.24 inherits a stated figure instead of a surprise.
/// </para>
/// </remarks>
public sealed class ExportedLineCountDivergenceTests
{
    [Fact]
    public void TheRunReportAndTheBudgetSheetStateDifferentExportedLineCounts()
    {
        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        Assert.Equal(4, run.Report.ExportedLines);
        Assert.Equal(3.0, run.Budget.StatedExportedLineCount(), 9);
    }

    /// <summary>
    /// The difference is exactly the lines nobody could code — not an off-by-one.
    /// </summary>
    /// <remarks>
    /// Stated as a relationship rather than as two literals, so a model with a
    /// different number of uncoded walls would still describe the same rule. This is
    /// what makes the divergence explainable to the user in one sentence, which is
    /// what 1.24 needs.
    /// </remarks>
    [Fact]
    public void TheTwoCountsDifferByExactlyTheUnclassifiedLineCount()
    {
        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        Assert.Equal(1, run.Report.UnclassifiedCount);
        Assert.Equal(
            run.Report.ExportedLines - run.Report.UnclassifiedCount,
            run.Budget.StatedExportedLineCount());
    }

    /// <summary>
    /// Each count reconciles with the block it heads.
    /// </summary>
    /// <remarks>
    /// Without this the test above would merely document two numbers. Asserting each
    /// against the rows it summarises is what shows both are internally honest, and
    /// therefore that the divergence is a naming problem rather than a counting bug
    /// in either place.
    /// </remarks>
    [Fact]
    public void EachCountReconcilesWithTheLinesItSummarises()
    {
        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        Assert.Equal(run.Result.LineCount, run.Report.ExportedLines);
        Assert.Equal(run.Budget.ExportedIdentifiers().Count, run.Budget.StatedExportedLineCount());
    }
}

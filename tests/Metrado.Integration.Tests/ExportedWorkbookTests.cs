namespace Metrado.Integration.Tests;

/// <summary>
/// The last stage: what the estimator actually opens.
/// </summary>
/// <remarks>
/// <c>metrado-measurement</c>, "Metrado Is Distinguishable from the Raw Revit
/// Quantity", scenario "Correction is observable in the output": "GIVEN a model
/// whose walls have sub-threshold openings, WHEN the export runs, THEN at least
/// one exported metrado differs from the corresponding raw Revit area." The word
/// is <em>exported</em>, so the assertion belongs here, against bytes the writer
/// produced, rather than against the result it was handed.
/// <para>
/// The writer's own suite cannot make this assertion. Its fixture supplies a
/// <c>MetradoResult</c> whose metrado and raw quantity are the same value, because
/// the writer does not compute anything — which means a reverted openings
/// correction is invisible to every golden fixture. This is the only place where
/// the number that reaches the workbook is checked against the number that entered
/// the pipeline.
/// </para>
/// </remarks>
public sealed class ExportedWorkbookTests
{
    [Fact]
    public void AtLeastOneExportedMetradoDiffersFromTheRawRevitArea()
    {
        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        double raw = ModelFixture.RawAreaOf(ModelFixture.CorrectedByOneOpening);
        double exported = run.Budget.MetradoOf(ModelFixture.CorrectedByOneOpening);

        Assert.Equal(18.0, raw, 9);
        Assert.Equal(18.6, exported, 9);
        Assert.NotEqual(raw, exported);
    }

    /// <summary>
    /// And the wall whose opening exceeded the threshold is exported unchanged.
    /// </summary>
    /// <remarks>
    /// The control for the assertion above: without it, a writer that added a
    /// constant to every metrado cell would satisfy "at least one differs".
    /// </remarks>
    [Fact]
    public void AWallWhoseOnlyOpeningExceedsTheThresholdIsExportedAtItsRawArea()
    {
        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        Assert.Equal(16.0, ModelFixture.RawAreaOf(ModelFixture.UncorrectedAboveThreshold), 9);
        Assert.Equal(16.0, run.Budget.MetradoOf(ModelFixture.UncorrectedAboveThreshold), 9);
    }

    /// <summary>
    /// The subtotal an estimator prices is the sum of corrected lines.
    /// </summary>
    /// <remarks>
    /// 18.6 + 16.3 + 16.0 = 50.9 written into the sheet, against 18.0 + 15.0 + 16.0
    /// = 49.0 of raw Revit area. The 1.9 m² between them is the openings the norm
    /// does not deduct.
    /// </remarks>
    [Fact]
    public void ThePartidaSubtotalInTheWorkbookAddsUpTheCorrectedLines()
    {
        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        Assert.Equal(49.0, ModelFixture.RawAreaTotalOfCodedWalls(), 9);
        Assert.Equal(50.9, run.Budget.SubtotalOf(ModelFixture.CodedPartida), 9);
    }

    /// <summary>
    /// <c>metrado-measurement</c>, "Active boundary mode is reported".
    /// </summary>
    /// <remarks>
    /// Asserted end to end because the convention has to survive the same stages the
    /// metrado does: read off the criteria, stamped onto the measurement result,
    /// carried through grouping and written out. A mode that is correct in
    /// <c>MetradoResult</c> and lost by the composition leaves the workbook
    /// attributing the budget to nothing.
    /// </remarks>
    [Fact]
    public void TheWorkbookRecordsTheConventionThatProducedTheMetrado()
    {
        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        Assert.Equal("exclusive", run.Budget.BoundaryModeOf(ModelFixture.CorrectedByOneOpening));
    }

    /// <summary>
    /// The uncoded wall is listed, and it is listed somewhere else.
    /// </summary>
    /// <remarks>
    /// <c>excel-budget-export</c> requires the unclassified block to be separate
    /// from the coded capitulos: an uncoded element belongs to no partida, so a
    /// budget listing it among the coded ones would claim a code it does not have.
    /// Both halves are asserted — present there, absent here — because either one
    /// alone is satisfied by an element that was dropped entirely.
    /// </remarks>
    [Fact]
    public void TheUncodedWallIsListedOnTheUnclassifiedSheetAndNotInTheBudget()
    {
        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        Assert.Equal([ModelFixture.Uncoded], run.Budget.UnclassifiedIdentifiers());
        Assert.Equal(
            [
                ModelFixture.CorrectedByOneOpening,
                ModelFixture.CorrectedByThreeOpenings,
                ModelFixture.UncorrectedAboveThreshold,
            ],
            run.Budget.ExportedIdentifiers());
    }
}

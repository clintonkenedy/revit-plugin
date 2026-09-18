using Metrado.Domain;

namespace Metrado.Integration.Tests;

/// <summary>
/// The corrected metrado survives every stage between the criteria and the
/// budget: resolution, measurement, codification, grouping and the run report.
/// </summary>
/// <remarks>
/// The openings correction is already proved as a rule by
/// <c>MeasurementApplyTests</c>. What is <em>not</em> proved there is that the
/// corrected number survives the composition — five stages, any one of which could
/// substitute the raw quantity and leave every unit test green. That substitution
/// is the specific failure <c>metrado-measurement</c> names in its purpose:
/// "exporting it unchanged ships a wrong budget that looks plausible".
/// <para>
/// Each assertion states both numbers rather than only their inequality. "They
/// differ" is also satisfied by a stage that corrupts the value, so a bare
/// <c>NotEqual</c> would pass for the failure these tests are meant to catch.
/// </para>
/// </remarks>
public sealed class MeasuredModelComposesIntoABudgetTests
{
    /// <summary>
    /// <c>metrado-measurement</c>, "Sub-threshold opening is added back": 18.0 m²
    /// with a 0.6 m² opening measures 18.6 m² under the built-in 1.0 m² threshold.
    /// </summary>
    [Fact]
    public void AtLeastOneMeasuredMetradoDiffersFromTheRawRevitArea()
    {
        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        MetradoResult measured = run.LineaOf(ModelFixture.CorrectedByOneOpening).Metrado;

        Assert.Equal(18.0, ModelFixture.RawAreaOf(ModelFixture.CorrectedByOneOpening), 9);
        Assert.Equal(18.0, measured.Raw.Value, 9);
        Assert.Equal(18.6, measured.Metrado.Value, 9);
        Assert.NotEqual(measured.Raw.Value, measured.Metrado.Value);
    }

    /// <summary>
    /// The correction is not a blanket offset applied to every element.
    /// </summary>
    /// <remarks>
    /// Without this, a pipeline that added a constant to every metrado would
    /// satisfy the scenario above. <c>metrado-measurement</c>, "Above-threshold
    /// opening stays deducted": this wall's only opening is 2.1 m², larger than the
    /// 1.0 m² threshold, so Revit's deduction of it stands.
    /// </remarks>
    [Fact]
    public void AWallWhoseOnlyOpeningExceedsTheThresholdKeepsItsRawArea()
    {
        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        MetradoResult measured = run.LineaOf(ModelFixture.UncorrectedAboveThreshold).Metrado;

        Assert.Equal(16.0, ModelFixture.RawAreaOf(ModelFixture.UncorrectedAboveThreshold), 9);
        Assert.Equal(16.0, measured.Metrado.Value, 9);
        Assert.Equal(measured.Raw.Value, measured.Metrado.Value);
    }

    /// <summary>
    /// Every opening below the threshold is added back, judged one at a time.
    /// </summary>
    /// <remarks>
    /// <c>metrado-measurement</c>, "Mixed openings on one element": 15.0 m² with
    /// openings of 0.4, 0.9 and 2.5 m² measures 16.3 m². Those openings sum to 3.8,
    /// well above the 1.0 threshold, so a composition that handed the rule a
    /// pre-aggregated total would export 15.0 here — breaking "the rule MUST NOT
    /// compare the sum of openings against the threshold" in the assembled path
    /// while the unit test kept passing.
    /// </remarks>
    [Fact]
    public void OpeningsAreStillJudgedIndividuallyWhenTheirSumExceedsTheThreshold()
    {
        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        Assert.Equal(15.0, ModelFixture.RawAreaOf(ModelFixture.CorrectedByThreeOpenings), 9);
        Assert.Equal(16.3, run.LineaOf(ModelFixture.CorrectedByThreeOpenings).Metrado.Metrado.Value, 9);
    }

    /// <summary>
    /// The partida total is built from corrected lines, not raw ones.
    /// </summary>
    /// <remarks>
    /// The subtotal is the figure an estimator prices, so it is the one place where
    /// a reverted correction costs money: 18.6 + 16.3 + 16.0 = 50.9 against a raw
    /// total of 18.0 + 15.0 + 16.0 = 49.0.
    /// </remarks>
    [Fact]
    public void ThePartidaTotalAddsUpTheCorrectedLinesAndNotTheRawAreas()
    {
        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        Assert.Equal(49.0, ModelFixture.RawAreaTotalOfCodedWalls(), 9);
        Assert.Equal(50.9, run.PartidaOf(ModelFixture.CodedPartida).Total.Value, 9);
    }

    /// <summary>
    /// Two walls sharing a code collapse into one partida; the uncoded one does not
    /// join them.
    /// </summary>
    /// <remarks>
    /// <c>partida-codification</c> requires codification to be total, so the wall
    /// with an empty Assembly Code is measured and reaches the result under the
    /// terminal link's code rather than being dropped. Asserted here because
    /// grouping and codification are separate units that only meet in a run.
    /// </remarks>
    [Fact]
    public void TheUncodedWallIsMeasuredAndReachesTheResultSeparatelyFromTheCodedOnes()
    {
        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        Assert.Equal(3, run.PartidaOf(ModelFixture.CodedPartida).Lineas.Count);
        Assert.Equal(20.0, run.LineaOf(ModelFixture.Uncoded).Metrado.Metrado.Value, 9);
        Assert.True(run.PartidaOf(UnclassifiedResolver.Code).IsUnclassified);
    }

    /// <summary>
    /// The run report counts the whole run, unclassified lines included.
    /// </summary>
    /// <remarks>
    /// <c>RunReport</c> documents <c>ExportedLines</c> as "every measurement line in
    /// the result, unclassified ones included", so four measured walls report four
    /// exported lines and one unclassified. This is the report the completion dialog
    /// shows without opening the workbook.
    /// </remarks>
    [Fact]
    public void TheRunReportCountsEveryMeasuredLineAndSaysHowManyAreUnclassified()
    {
        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        Assert.Equal(4, run.Report.ExportedLines);
        Assert.Equal(1, run.Report.UnclassifiedCount);
        Assert.False(run.Report.NoMeasurableElements);
    }

    /// <summary>
    /// The report names the convention the lines were actually measured under.
    /// </summary>
    /// <remarks>
    /// <c>AppliedCriterion</c> is read back off the measured lines rather than
    /// copied from the criteria table, so this is the assertion that the criteria in
    /// force and the budget produced agree. One capitulo produced every line, so
    /// there is exactly one applied convention to report.
    /// </remarks>
    [Fact]
    public void TheRunReportNamesTheConventionTheLinesWereMeasuredUnder()
    {
        using ExportRun run = ExportPipeline.RunWithBuiltInDefaults(ModelFixture.Walls);

        AppliedCriterion applied = Assert.Single(run.Report.Applied);

        Assert.Equal(ModelFixture.Capitulo, applied.Capitulo);
        Assert.Equal(QuantityUnit.SquareMetre, applied.Unit);
        Assert.Equal(Defaults.AreaThresholdSquareMetres, applied.Threshold, 9);
        Assert.Equal(BoundaryMode.Exclusive, applied.Mode);
    }
}

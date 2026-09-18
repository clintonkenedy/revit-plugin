using Metrado.Domain;

namespace Metrado.Integration.Tests;

/// <summary>
/// The criteria handed to a run are the criteria that produced the budget.
/// </summary>
/// <remarks>
/// Every other test in this suite runs under the built-in defaults, which means
/// none of them can tell a pipeline that honoured its criteria from one that
/// reached for <see cref="CriteriaSet.Default"/> on its own — the two produce
/// identical numbers. Measured, not assumed: replacing the supplied criteria with
/// the defaults inside the pipeline leaves every one of those tests green.
/// <para>
/// So this class supplies criteria that are <em>not</em> the defaults and asserts
/// the difference reaches the workbook. It is what makes the first stage of task
/// 1.18 load-bearing rather than decorative.
/// </para>
/// </remarks>
public sealed class SuppliedCriteriaGovernTheBudgetTests
{
    /// <summary>
    /// A threshold at which the 2.5 m² opening sits exactly on the boundary.
    /// </summary>
    /// <remarks>
    /// Chosen so the threshold and the mode are separately detectable. Under
    /// <c>inclusive</c> that opening is added back; under <c>exclusive</c> at the
    /// same threshold it stays deducted — so a pipeline that carried the threshold
    /// across and lost the mode produces a different number than one that carried
    /// both.
    /// </remarks>
    private const double BoundaryThreshold = 2.5;

    private const string CriteriaPath = "criteria.json";

    [Fact]
    public void TheSuppliedThresholdAndNotTheBuiltInOneDecidesTheExportedMetrado()
    {
        using ExportRun run = ExportPipeline.Run(
            WallsMeasuredUnder(BoundaryThreshold, BoundaryMode.Inclusive),
            ModelFixture.Walls);

        // 16.0 + 2.1 under the supplied criteria; 16.0 under the built-in 1.0 m².
        Assert.Equal(18.1, run.Budget.MetradoOf(ModelFixture.UncorrectedAboveThreshold), 9);
        Assert.Equal(16.0, ModelFixture.RawAreaOf(ModelFixture.UncorrectedAboveThreshold), 9);
    }

    /// <summary>
    /// The supplied <em>mode</em> is honoured too, not just the threshold.
    /// </summary>
    /// <remarks>
    /// <c>metrado-measurement</c>: "The mode affects ONLY openings whose quantity is
    /// exactly equal to the threshold." This wall's 2.5 m² opening is exactly the
    /// supplied threshold, so it is added back under <c>inclusive</c> and stays
    /// deducted under <c>exclusive</c>. 15.0 + 0.4 + 0.9 + 2.5 = 18.8 here; the same
    /// wall measures 16.3 when the boundary opening is excluded.
    /// </remarks>
    [Fact]
    public void AnOpeningExactlyAtTheSuppliedThresholdIsAddedBackUnderInclusiveMode()
    {
        using ExportRun inclusive = ExportPipeline.Run(
            WallsMeasuredUnder(BoundaryThreshold, BoundaryMode.Inclusive),
            ModelFixture.Walls);

        using ExportRun exclusive = ExportPipeline.Run(
            WallsMeasuredUnder(BoundaryThreshold, BoundaryMode.Exclusive),
            ModelFixture.Walls);

        Assert.Equal(18.8, inclusive.Budget.MetradoOf(ModelFixture.CorrectedByThreeOpenings), 9);
        Assert.Equal(16.3, exclusive.Budget.MetradoOf(ModelFixture.CorrectedByThreeOpenings), 9);
    }

    /// <summary>
    /// The workbook names the convention it was actually given.
    /// </summary>
    [Fact]
    public void TheWorkbookRecordsTheSuppliedConventionAndNotTheProductDefault()
    {
        using ExportRun run = ExportPipeline.Run(
            WallsMeasuredUnder(BoundaryThreshold, BoundaryMode.Inclusive),
            ModelFixture.Walls);

        Assert.Equal(BoundaryMode.Exclusive, Defaults.Mode);
        Assert.Equal("inclusive", run.Budget.BoundaryModeOf(ModelFixture.CorrectedByOneOpening));
    }

    /// <summary>
    /// The subtotal moves with the criteria, so a criteria change is a budget change.
    /// </summary>
    /// <remarks>
    /// 18.6 + 18.8 + 18.1 = 55.5 under the supplied criteria, against 50.9 under the
    /// built-in defaults and 49.0 of raw Revit area. Three different totals from one
    /// model — which is exactly why <c>takeoff-configuration</c> requires the run to
    /// report which criteria were in force.
    /// </remarks>
    [Fact]
    public void TheSubtotalMovesWhenTheCriteriaMove()
    {
        using ExportRun run = ExportPipeline.Run(
            WallsMeasuredUnder(BoundaryThreshold, BoundaryMode.Inclusive),
            ModelFixture.Walls);

        Assert.Equal(55.5, run.Budget.SubtotalOf(ModelFixture.CodedPartida), 9);
        Assert.Equal(ConfigSource.File, run.Criteria.Source);
        Assert.Equal(CriteriaPath, run.Criteria.Path);
    }

    /// <summary>
    /// The criteria in force reach the run report as the convention applied.
    /// </summary>
    [Fact]
    public void TheRunReportNamesTheSuppliedConventionAsTheOneApplied()
    {
        using ExportRun run = ExportPipeline.Run(
            WallsMeasuredUnder(BoundaryThreshold, BoundaryMode.Inclusive),
            ModelFixture.Walls);

        AppliedCriterion applied = Assert.Single(run.Report.Applied);

        Assert.Equal(BoundaryMode.Inclusive, applied.Mode);
        Assert.Equal(BoundaryThreshold, applied.Threshold, 9);
    }

    /// <summary>
    /// Criteria the merge rejects stop the run instead of half-applying.
    /// </summary>
    /// <remarks>
    /// <c>takeoff-configuration</c>, "Invalid Configuration Fails Loudly". Included
    /// here because it is the branch that proves the supplied criteria are actually
    /// built rather than waved through: a negative threshold is refused by name,
    /// with the offending category and value carried on the error.
    /// </remarks>
    [Fact]
    public void ANegativeThresholdIsRefusedBeforeAnyBudgetIsProduced()
    {
        Result<CriteriaSet, ConfigError> merged = CriteriaSet.Merge(
            CriteriaSet.Default,
            [new CategoryOverride(ModelFixture.Capitulo, threshold: -1.0)]);

        Assert.False(merged.IsOk);
        Assert.Equal(ModelFixture.Capitulo, merged.Error.Category);
        Assert.Equal("-1", merged.Error.InvalidValue);
    }

    /// <summary>
    /// Builds the criteria an I2 configuration file will produce for Walls.
    /// </summary>
    /// <remarks>
    /// <see cref="ConfigSource.File"/> is stated because that is what these criteria
    /// are: values a file supplied, overriding the defaults per field. I1's
    /// <c>CriteriaResolver</c> cannot produce this yet — it refuses a supplied file
    /// outright, since the JSON reader is task 2.1 — so this value stands in for the
    /// reader exactly as <see cref="ModelFixture"/> stands in for extraction.
    /// <para>
    /// Going through <see cref="CriteriaSet.Merge"/> rather than constructing a
    /// <see cref="CriteriaSet"/> by hand keeps the per-field coalesce in the path:
    /// the unit and the quantity sources are inherited here, and only the threshold
    /// and the mode are stated.
    /// </para>
    /// </remarks>
    private static EffectiveCriteria WallsMeasuredUnder(double threshold, BoundaryMode mode)
    {
        Result<CriteriaSet, ConfigError> merged = CriteriaSet.Merge(
            CriteriaSet.Default,
            [new CategoryOverride(ModelFixture.Capitulo, threshold: threshold, mode: mode)]);

        Assert.True(
            merged.IsOk,
            $"The supplied criteria must merge cleanly over the defaults: "
                + $"{(merged.IsOk ? string.Empty : merged.Error.Message)}");

        return new EffectiveCriteria(merged.Value, ConfigSource.File, CriteriaPath);
    }
}

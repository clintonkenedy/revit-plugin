using System.Reflection;
using static Metrado.Domain.Tests.MeasurementFixture;

namespace Metrado.Domain.Tests;

/// <summary>
/// <c>metrado-measurement</c>, requirement "Per-Category Measurement Criterion",
/// scenario "No source yields a value": such an element "is reported with no
/// metrado and a validation warning AND it MUST NOT be silently assigned zero as
/// if it were measured".
/// </summary>
public sealed class MeasurementNoSourceTests
{
    /// <summary>
    /// <c>metrado-measurement</c>, scenario "No source yields a value".
    /// </summary>
    [Fact]
    public void AnElementWhoseSourcesAllCameUpEmptyIsReportedWithNoMetrado()
    {
        MetradoOutcome outcome = Measurement.Measure(
            WallWith(Source("Some_Other_Parameter", 4.0)),
            Criterion("HOST_AREA_COMPUTED", "Metrado_Area"));

        Assert.Equal(MetradoStatus.NoSource, outcome.Status);
        Assert.Null(outcome.Result);
        Assert.NotNull(outcome.Warning);
    }

    /// <summary>
    /// The other half of the requirement's "MUST NOT": an element whose source
    /// genuinely reads 0.0 <em>was</em> measured, and is reported as measured.
    /// </summary>
    /// <remarks>
    /// The contrast with the test above is the whole point. Same category, same
    /// criterion, and the two elements differ only in whether the source was there
    /// — yet one carries a result and the other carries none. A rule that reported
    /// both as a metrado of 0.0 would pass any assertion about the number and still
    /// be wrong about the fact.
    /// </remarks>
    [Fact]
    public void AnElementWhoseSourceReadsZeroIsMeasuredRatherThanUnreported()
    {
        MetradoOutcome outcome = Measurement.Measure(
            WallWith(Source("HOST_AREA_COMPUTED", 0.0)),
            Criterion("HOST_AREA_COMPUTED", "Metrado_Area"));

        Assert.Equal(MetradoStatus.Measured, outcome.Status);
        Assert.NotNull(outcome.Result);
        Assert.Equal(0.0, outcome.Result.Metrado.Value, 9);

        // Nothing to report: the element was measured, and it measures nothing.
        Assert.Null(outcome.Warning);
    }

    /// <summary>
    /// A selected source is carried into the openings correction, openings and all.
    /// </summary>
    /// <remarks>
    /// <c>metrado-measurement</c>, scenario "Sub-threshold opening is added back",
    /// reached through <c>Measure</c> rather than by calling the rule directly. It
    /// pins the composition: a version that selected the source correctly and then
    /// applied the correction to an empty openings list would report 18.0 — the raw
    /// Revit area, which is exactly the wrong number this product exists to avoid.
    /// </remarks>
    [Fact]
    public void ASelectedSourceIsMeasuredWithItsOpeningsCorrection()
    {
        ElementTakeoff wall = WallWith(Source("HOST_AREA_COMPUTED", 18.0)) with
        {
            Openings = [new OpeningQuantity("window-1", SquareMetres(0.6))],
        };

        MetradoOutcome outcome = Measurement.Measure(wall, Criterion("HOST_AREA_COMPUTED"));

        Assert.Equal(MetradoStatus.Measured, outcome.Status);
        Assert.Equal(18.6, outcome.Result!.Metrado.Value, 9);
        Assert.Equal(18.0, outcome.Result.Raw.Value, 9);
    }

    /// <summary>
    /// The warning names the element to fix and the sources that were tried.
    /// </summary>
    /// <remarks>
    /// <c>UniqueId</c> is the only handle that survives the Revit seam, and the
    /// source list is the user's actual fix — populate one of them. A warning
    /// without both is one the user can acknowledge but not act on.
    /// </remarks>
    [Fact]
    public void TheWarningNamesTheElementAndTheSourcesThatWereTried()
    {
        MetradoOutcome outcome = Measurement.Measure(
            WallWith(Source("Some_Other_Parameter", 4.0)) with { UniqueId = "wall-unmeasured-3" },
            Criterion("HOST_AREA_COMPUTED", "Metrado_Area"));

        ValidationWarning warning = outcome.Warning!;

        Assert.Equal("wall-unmeasured-3", warning.UniqueId);
        Assert.Equal("Walls", warning.CategoryName);
        Assert.Contains("HOST_AREA_COMPUTED", warning.Condition);
        Assert.Contains("Metrado_Area", warning.Condition);
        Assert.Contains("not measured as zero", warning.Condition);
    }

    /// <summary>
    /// A criterion listing no sources at all measures nothing, even when the
    /// element carries quantities.
    /// </summary>
    /// <remarks>
    /// Correct for I1, where every criterion names its sources. Task 2.3 branches
    /// on the empty list <em>before</em> selection so a counted category reports
    /// <c>Counted</c> instead; this test pins what today's behaviour actually is,
    /// so that change is a visible decision rather than a silent drift.
    /// </remarks>
    [Fact]
    public void ACriterionListingNoSourcesReportsNoMetradoRatherThanZero()
    {
        MetradoOutcome outcome = Measurement.Measure(
            WallWith(Source("HOST_AREA_COMPUTED", 12.0)),
            Criterion());

        Assert.Equal(MetradoStatus.NoSource, outcome.Status);
        Assert.Null(outcome.Result);
    }

    /// <summary>
    /// Unreachability, part two: the correction accepts only a plain
    /// <see cref="Quantity"/>.
    /// </summary>
    /// <remarks>
    /// Together with <c>SourceSelectionTests</c> — where no public member yields a
    /// <see cref="Quantity"/> outside <c>Match</c>, and <c>Match</c> never invokes
    /// the selected branch for an absent selection — this closes the argument: an
    /// absent selection produces no value of type <see cref="Quantity"/>, and
    /// <c>Apply</c> accepts nothing else. There is no call to the correction that
    /// an unmeasured element can be written into.
    /// <para>
    /// A <c>Quantity?</c> parameter would reopen it, since <c>null</c> would then be
    /// a thing the correction had to decide about at runtime.
    /// </para>
    /// </remarks>
    [Fact]
    public void ApplyAcceptsOnlyAPlainQuantitySoAnAbsentSelectionCannotReachIt()
    {
        ParameterInfo raw = typeof(Measurement)
            .GetMethod(nameof(Measurement.Apply))!
            .GetParameters()
            .Single(parameter => parameter.Name == "raw");

        Assert.Equal(typeof(Quantity), raw.ParameterType);
        Assert.Null(Nullable.GetUnderlyingType(raw.ParameterType));
    }
}

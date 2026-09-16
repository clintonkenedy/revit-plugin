using static Metrado.Domain.Tests.MeasurementFixture;

namespace Metrado.Domain.Tests;

/// <summary>
/// <c>metrado-measurement</c>, requirement "Per-Category Measurement Criterion":
/// each category's criterion lists the quantity sources to read in order, and the
/// first source with a value is the one measured.
/// </summary>
public sealed class MeasurementSelectSourceTests
{
    /// <summary>
    /// <c>metrado-measurement</c>, scenario "First available source wins".
    /// </summary>
    [Fact]
    public void TheSecondSourceIsUsedWhenTheFirstHasNoValue()
    {
        SourceSelection selection = Measurement.SelectSource(
            WallWith(Source("HOST_VOLUME_COMPUTED", 7.5)),
            Criterion("HOST_AREA_COMPUTED", "HOST_VOLUME_COMPUTED"));

        Assert.Equal(7.5, Selected(selection).Value, 9);
    }

    /// <summary>
    /// Priority belongs to the criterion, not to whatever order Revit happened to
    /// hand the parameters over in.
    /// </summary>
    /// <remarks>
    /// Without this, a rule that simply takes the element's first quantity passes
    /// the first-available-source scenario outright, because that scenario's
    /// element carries exactly one quantity. Here the element carries both, listed
    /// in the opposite order to the criterion, so the two rules disagree.
    /// </remarks>
    [Fact]
    public void TheCriterionsOrderDecidesTheWinnerNotTheOrderTheQuantitiesArrivedIn()
    {
        SourceSelection selection = Measurement.SelectSource(
            WallWith(Source("HOST_AREA_COMPUTED", 5.0), Source("Metrado_Area", 7.0)),
            Criterion("Metrado_Area", "HOST_AREA_COMPUTED"));

        Assert.Equal(7.0, Selected(selection).Value, 9);
    }

    /// <summary>
    /// A source that is present and reads 0.0 <em>has</em> a value. It wins, and
    /// the later source is never consulted.
    /// </summary>
    /// <remarks>
    /// This is the distinction the whole requirement turns on. Treating 0.0 as
    /// "no value" and falling through to the next source would silently replace a
    /// genuine zero with a different parameter's number — a measured wall reported
    /// as something it is not.
    /// </remarks>
    [Fact]
    public void ASourceReadingZeroHasAValueAndWinsOverALaterSourceThatAlsoHasOne()
    {
        SourceSelection selection = Measurement.SelectSource(
            WallWith(Source("Metrado_Area", 0.0), Source("HOST_AREA_COMPUTED", 9.0)),
            Criterion("Metrado_Area", "HOST_AREA_COMPUTED"));

        Assert.Equal(0.0, Selected(selection).Value, 9);
    }

    /// <summary>
    /// A quantity the criterion never asked for is not a fallback.
    /// </summary>
    /// <remarks>
    /// The criterion is the whole definition of what this category is measured by.
    /// Reaching past it for any other parameter Revit happened to expose would
    /// measure the element by a rule nobody configured and nothing reports.
    /// </remarks>
    [Fact]
    public void AQuantityWhoseSourceTheCriterionDoesNotListIsNeverUsed()
    {
        SourceSelection selection = Measurement.SelectSource(
            WallWith(Source("Some_Other_Parameter", 4.0)),
            Criterion("Metrado_Area", "HOST_AREA_COMPUTED"));

        Assert.False(selection.HasQuantity);
    }

    /// <summary>
    /// A material layer's quantity is not the element's quantity, even when it was
    /// read from the source the criterion names.
    /// </summary>
    /// <remarks>
    /// <see cref="MaterialRef"/> states the rule this relies on: a null material
    /// means the quantity describes the whole element, a populated one means a
    /// single layer. Measuring a wall by its insulation layer is not a smaller
    /// number, it is a different fact — and it would arrive looking exactly like a
    /// correct one.
    /// <para>
    /// I1 populates no material, so this cannot happen yet. Task 3.1 emits layer
    /// quantities <em>alongside</em> the whole-element quantity, at which point a
    /// selector without this rule starts picking whichever the extraction listed
    /// first.
    /// </para>
    /// </remarks>
    [Fact]
    public void AMaterialLayerQuantityIsNotEligibleAsTheElementsOwnQuantity()
    {
        SourceSelection selection = Measurement.SelectSource(
            WallWith(LayerSource("HOST_AREA_COMPUTED", 3.0, "Insulation")),
            Criterion("HOST_AREA_COMPUTED"));

        Assert.False(selection.HasQuantity);
    }

    /// <summary>
    /// When a layer and the element itself both report the named source, the
    /// element's own quantity is the one measured.
    /// </summary>
    /// <remarks>
    /// The companion to the test above, and the case task 3.1 actually produces.
    /// Without it, a selector could satisfy "layers are not eligible" by returning
    /// nothing whenever any layer is present — refusing to measure a compound wall
    /// at all.
    /// </remarks>
    [Fact]
    public void TheWholeElementQuantityWinsOverALayerSharingItsSourceKey()
    {
        SourceSelection selection = Measurement.SelectSource(
            WallWith(
                LayerSource("HOST_AREA_COMPUTED", 3.0, "Insulation"),
                Source("HOST_AREA_COMPUTED", 12.0)),
            Criterion("HOST_AREA_COMPUTED"));

        Assert.Equal(12.0, Selected(selection).Value, 9);
    }
}

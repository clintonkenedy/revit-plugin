using static Metrado.Domain.Tests.MeasurementFixture;

namespace Metrado.Domain.Tests;

/// <summary>
/// <c>metrado-measurement</c>, requirement "Corrected Metrado Is Bounded by the
/// Gross Quantity": the correction adds material back, so it needs a ceiling. The
/// ceiling is the element's gross quantity — what it would measure with no opening
/// deducted at all.
/// </summary>
public sealed class MeasurementGrossBoundTests
{

    /// <summary>
    /// <c>metrado-measurement</c>, scenario "Openings consume the whole element".
    /// </summary>
    /// <remarks>
    /// A raw area of 0.0 is not an error here: Revit subtracted every opening, and
    /// they happened to cover the full face. Adding them all back is exactly the
    /// gross area, so this is the case where the metrado legitimately sits on its
    /// own ceiling — the bound must permit equality, not just strict inequality.
    /// </remarks>
    [Fact]
    public void OpeningsConsumingTheWholeFaceMeasureTheGrossAreaAndNeverMore()
    {
        MetradoOutcome outcome = Measurement.Apply(
            Wall(),
            SquareMetres(0.0),
            [SquareMetres(3.0), SquareMetres(2.5), SquareMetres(1.5)],
            Threshold(5.0));

        MetradoResult result = outcome.Result!;

        Assert.Equal(7.0, result.Gross.Value, 9);
        Assert.Equal(7.0, result.Metrado.Value, 9);
        Assert.False(result.Metrado.Value > result.Gross.Value);

        // Sitting exactly on the ceiling is the correct answer here, not a fault.
        // Warning about it would train the user to ignore the warning list.
        Assert.False(result.ClampedToGross);
        Assert.Null(outcome.Warning);
    }

    /// <summary>
    /// The gross quantity sums <em>every</em> opening, including the ones the
    /// threshold leaves deducted. Only then is it a ceiling the metrado can sit
    /// strictly below — a gross built from the added-back openings alone would
    /// equal the metrado on every input and bound nothing at all.
    /// </summary>
    /// <remarks>
    /// The 2.5 opening stays deducted, so the metrado is 16.3 while the gross is
    /// 18.8. The 2.5 gap between them is the material genuinely absent from the
    /// wall, and it is the only reason the two numbers are distinguishable.
    /// </remarks>
    [Fact]
    public void TheGrossQuantityCountsDeductedOpeningsTooSoItSitsAboveTheMetrado()
    {
        MetradoOutcome outcome = Measurement.Apply(
            Wall(),
            SquareMetres(15.0),
            [SquareMetres(0.4), SquareMetres(0.9), SquareMetres(2.5)],
            Threshold(1.0));

        MetradoResult result = outcome.Result!;

        Assert.Equal(18.8, result.Gross.Value, 9);
        Assert.Equal(16.3, result.Metrado.Value, 9);
        Assert.True(result.Metrado.Value < result.Gross.Value);

        Assert.False(result.ClampedToGross);
        Assert.Null(outcome.Warning);
    }

    /// <summary>
    /// <c>metrado-measurement</c>, scenario "Inconsistent opening data is
    /// reported, not exported".
    /// </summary>
    /// <remarks>
    /// The specification defines the gross quantity as <c>raw + Σ q(o)</c>, so
    /// "opening quantities exceed the gross quantity" is reachable on exactly one
    /// input: a negative raw quantity. Revit cannot compute a negative area, so
    /// this is the inconsistent extraction the requirement is guarding against —
    /// openings claiming to restore more material than the element ever had.
    /// <para>
    /// The user cannot act on a number alone, so the warning has to name the
    /// element. <c>UniqueId</c> is the handle that survives the Revit seam and the
    /// only one that selects the element back in the model.
    /// </para>
    /// </remarks>
    [Fact]
    public void InconsistentOpeningDataIsClampedToGrossAndWarnsNamingTheUniqueId()
    {
        MetradoOutcome outcome = Measurement.Apply(
            Wall("wall-inconsistent-7"),
            SquareMetres(-1.0),
            [SquareMetres(4.0)],
            Threshold(10.0));

        MetradoResult result = outcome.Result!;

        Assert.Equal(3.0, result.Gross.Value, 9);
        Assert.Equal(3.0, result.Metrado.Value, 9);
        Assert.True(result.ClampedToGross);

        Assert.NotNull(outcome.Warning);
        Assert.Equal("wall-inconsistent-7", outcome.Warning.UniqueId);
    }
}

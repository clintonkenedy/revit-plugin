namespace Metrado.Domain.Tests;

/// <summary>
/// Pins <see cref="Measurement.IsAddedBack"/>, the rule's own decision for one
/// opening, exposed so a report can say which way the rule went without
/// restating the rule.
/// </summary>
public sealed class MeasurementIsAddedBackTests
{
    [Theory]
    [InlineData(BoundaryMode.Exclusive, 0.99997, true)]
    [InlineData(BoundaryMode.Exclusive, 1.0, false)]
    [InlineData(BoundaryMode.Inclusive, 1.0, true)]
    [InlineData(BoundaryMode.Inclusive, 1.00004, false)]
    public void ItDecidesAsTheRuleDoes(BoundaryMode mode, double opening, bool addedBack)
    {
        Assert.Equal(addedBack, Measurement.IsAddedBack(SquareMetres(opening), Threshold(1.0, mode)));
    }

    /// <summary>
    /// Agreement with <see cref="Measurement.Apply"/> itself, across both modes
    /// and both sides of the threshold: an opening is added back exactly when
    /// Apply's metrado grows by it.
    /// </summary>
    [Theory]
    [InlineData(BoundaryMode.Exclusive, 0.5)]
    [InlineData(BoundaryMode.Exclusive, 1.0)]
    [InlineData(BoundaryMode.Exclusive, 1.5)]
    [InlineData(BoundaryMode.Inclusive, 0.5)]
    [InlineData(BoundaryMode.Inclusive, 1.0)]
    [InlineData(BoundaryMode.Inclusive, 1.5)]
    public void ItAgreesWithApply(BoundaryMode mode, double opening)
    {
        OpeningsThreshold threshold = Threshold(1.0, mode);
        MetradoOutcome outcome = Measurement.Apply(MeasurementFixture.Wall(), SquareMetres(18.0), [SquareMetres(opening)], threshold);

        bool grew = outcome.Result!.Metrado.Value > 18.0;

        Assert.Equal(grew, Measurement.IsAddedBack(SquareMetres(opening), threshold));
    }

    [Fact]
    public void ItRefusesToCompareAcrossUnits()
    {
        Assert.Throws<ArgumentException>(() =>
            Measurement.IsAddedBack(new Quantity(0.5, QuantityUnit.CubicMetre), Threshold(1.0, BoundaryMode.Exclusive)));
    }

    private static Quantity SquareMetres(double value) => new(value, QuantityUnit.SquareMetre);

    private static OpeningsThreshold Threshold(double value, BoundaryMode mode) =>
        OpeningsThreshold.TryCreate(value, QuantityUnit.SquareMetre, mode).Value;
}

namespace Metrado.Domain.Tests;

/// <summary>
/// The openings correction — the single most important behaviour in the product.
/// Revit's computed area and volume parameters already subtract every opening, so
/// the raw value is geometry, not metrado. Metrado norms do not deduct small
/// openings, so the correction adds back every opening below the threshold.
/// </summary>
public sealed class MeasurementApplyTests
{
    private static OpeningsThreshold Threshold(
        double value,
        BoundaryMode mode = BoundaryMode.Exclusive,
        QuantityUnit unit = QuantityUnit.SquareMetre)
    {
        Result<OpeningsThreshold, ConfigError> result =
            OpeningsThreshold.TryCreate(value, unit, mode, "Walls");

        Assert.True(result.IsOk, $"Test setup built an invalid threshold: {result}");
        return result.Value;
    }

    private static Quantity SquareMetres(double value) => new(value, QuantityUnit.SquareMetre);

    /// <summary>
    /// <c>metrado-measurement</c>, scenario "Sub-threshold opening is added back".
    /// </summary>
    /// <remarks>
    /// Compared to nine decimals rather than exactly: 0.6 has no exact binary
    /// representation, so <c>18.0 + 0.6</c> lands a few ulps away from 18.6. The
    /// tolerance is far tighter than any quantity a budget distinguishes and far
    /// looser than the representation error.
    /// </remarks>
    [Fact]
    public void ASubThresholdOpeningIsAddedBackIntoTheMetrado()
    {
        MetradoResult result = Measurement.Apply(
            SquareMetres(18.0),
            [SquareMetres(0.6)],
            Threshold(1.0));

        Assert.Equal(18.6, result.Metrado.Value, 9);
    }

    /// <summary>
    /// <c>metrado-measurement</c>, scenario "Opening exactly at the threshold,
    /// exclusive mode". Exclusive matches the norm wording "vanos de área
    /// <em>menor a</em> X", so an opening of exactly the threshold stays deducted.
    /// </summary>
    [Fact]
    public void AnOpeningExactlyAtTheThresholdStaysDeductedUnderExclusiveMode()
    {
        MetradoResult result = Measurement.Apply(
            SquareMetres(18.0),
            [SquareMetres(1.0)],
            Threshold(1.0, BoundaryMode.Exclusive));

        Assert.Equal(18.0, result.Metrado.Value, 9);
    }

    /// <summary>
    /// <c>metrado-measurement</c>, scenario "Opening exactly at the threshold,
    /// inclusive mode". Inclusive matches the norm wording "vanos <em>hasta</em>
    /// X", so the same opening is added back instead.
    /// </summary>
    [Fact]
    public void AnOpeningExactlyAtTheThresholdIsAddedBackUnderInclusiveMode()
    {
        MetradoResult result = Measurement.Apply(
            SquareMetres(18.0),
            [SquareMetres(1.0)],
            Threshold(1.0, BoundaryMode.Inclusive));

        Assert.Equal(19.0, result.Metrado.Value, 9);
    }

    /// <summary>
    /// <c>metrado-measurement</c>, scenario "Boundary mode is the only difference
    /// between the two modes". This is the whole justification for the mode
    /// existing: it must move the boundary opening and nothing else.
    /// </summary>
    [Fact]
    public void TheTwoModesDifferByExactlyTheBoundaryOpeningAndNothingElse()
    {
        Quantity raw = SquareMetres(15.0);
        Quantity[] openings = [SquareMetres(0.4), SquareMetres(1.0), SquareMetres(2.5)];

        double exclusive = Measurement.Apply(
            raw, openings, Threshold(1.0, BoundaryMode.Exclusive)).Metrado.Value;
        double inclusive = Measurement.Apply(
            raw, openings, Threshold(1.0, BoundaryMode.Inclusive)).Metrado.Value;

        // Exclusive adds back only the 0.4; inclusive also adds back the 1.0.
        Assert.Equal(15.4, exclusive, 9);
        Assert.Equal(16.4, inclusive, 9);

        // The difference is exactly the boundary opening — not a rounding drift
        // and not the 2.5, which stays deducted under both modes.
        Assert.Equal(1.0, inclusive - exclusive, 9);
    }

    /// <summary>
    /// <c>metrado-measurement</c>, scenario "Above-threshold opening stays
    /// deducted". A door or window large enough to matter is genuinely absent
    /// material, so Revit's deduction is what the norm wants.
    /// </summary>
    [Fact]
    public void AnAboveThresholdOpeningStaysDeducted()
    {
        MetradoResult result = Measurement.Apply(
            SquareMetres(16.0),
            [SquareMetres(2.1)],
            Threshold(1.0));

        Assert.Equal(16.0, result.Metrado.Value, 9);
    }

    /// <summary>
    /// <c>metrado-measurement</c>, scenario "Mixed openings on one element":
    /// 15.0 + 0.4 + 0.9 = 16.3, with the 2.5 left deducted.
    /// </summary>
    [Fact]
    public void MixedOpeningsAddBackOnlyThoseBelowTheThreshold()
    {
        MetradoResult result = Measurement.Apply(
            SquareMetres(15.0),
            [SquareMetres(0.4), SquareMetres(0.9), SquareMetres(2.5)],
            Threshold(1.0));

        Assert.Equal(16.3, result.Metrado.Value, 9);
    }

    /// <summary>
    /// <c>metrado-measurement</c>, scenario "Element with no openings". A solid
    /// wall has nothing to add back, so the correction is the identity.
    /// </summary>
    [Fact]
    public void AnElementWithNoOpeningsMeasuresItsRawQuantity()
    {
        MetradoResult result = Measurement.Apply(
            SquareMetres(20.0),
            [],
            Threshold(1.0));

        Assert.Equal(20.0, result.Metrado.Value, 9);
    }

    /// <summary>
    /// <c>metrado-measurement</c>, scenario "Zero threshold disables the
    /// correction". Asserted under both modes, because a zero threshold must
    /// disable the correction regardless of the equality convention — under
    /// inclusive, an opening of exactly 0.0 would otherwise be "added back".
    /// </summary>
    [Theory]
    [InlineData(BoundaryMode.Exclusive)]
    [InlineData(BoundaryMode.Inclusive)]
    public void AZeroThresholdDisablesTheCorrectionEntirely(BoundaryMode mode)
    {
        MetradoResult result = Measurement.Apply(
            SquareMetres(12.0),
            [SquareMetres(0.0), SquareMetres(0.5), SquareMetres(3.0)],
            Threshold(0.0, mode));

        Assert.Equal(12.0, result.Metrado.Value, 9);
    }

    /// <summary>
    /// <c>metrado-measurement</c> MUST: "Openings MUST be evaluated individually;
    /// the rule MUST NOT compare the sum of openings against the threshold."
    /// </summary>
    /// <remarks>
    /// The three openings are each below the 1.0 threshold but sum to 2.1, well
    /// above it. An implementation that pre-aggregated would add back nothing and
    /// report 10.0 — a defensible-looking number that is wrong. Summing the
    /// openings is a different rule, not an optimisation of this one.
    /// </remarks>
    [Fact]
    public void OpeningsAreEvaluatedIndividuallyAndNeverSummedAgainstTheThreshold()
    {
        MetradoResult result = Measurement.Apply(
            SquareMetres(10.0),
            [SquareMetres(0.6), SquareMetres(0.7), SquareMetres(0.8)],
            Threshold(1.0));

        Assert.Equal(12.1, result.Metrado.Value, 9);
        Assert.NotEqual(10.0, result.Metrado.Value, 9);
    }

    /// <summary>
    /// The converse of the individual-evaluation guarantee: openings that are each
    /// above the threshold must all stay deducted even though no single one
    /// dominates. A rule keyed on the smallest or the first opening would pass the
    /// test above and fail this one.
    /// </summary>
    [Fact]
    public void EveryOpeningIsTestedNotJustTheFirstOrTheSmallest()
    {
        MetradoResult result = Measurement.Apply(
            SquareMetres(30.0),
            [SquareMetres(0.5), SquareMetres(4.0), SquareMetres(0.25)],
            Threshold(1.0));

        // Only the 0.5 and the 0.25 come back; the 4.0 stays deducted.
        Assert.Equal(30.75, result.Metrado.Value, 9);
    }

    /// <summary>
    /// <c>metrado-measurement</c> MUST: "The active mode MUST be recorded on the
    /// measurement result". A configurable convention that is not reported
    /// produces two different defensible budgets from one model with no way to
    /// tell them apart, so carrying it is part of computing it.
    /// </summary>
    [Theory]
    [InlineData(BoundaryMode.Exclusive)]
    [InlineData(BoundaryMode.Inclusive)]
    public void TheResultRecordsTheBoundaryModeThatProducedIt(BoundaryMode mode)
    {
        MetradoResult result = Measurement.Apply(
            SquareMetres(18.0),
            [SquareMetres(1.0)],
            Threshold(1.0, mode));

        Assert.Equal(mode, result.AppliedMode);
    }

    /// <summary>
    /// The threshold is the other half of the convention: the same mode with a
    /// different threshold is a different budget.
    /// </summary>
    [Fact]
    public void TheResultRecordsTheThresholdThatProducedIt()
    {
        MetradoResult result = Measurement.Apply(
            SquareMetres(18.0),
            [SquareMetres(0.6)],
            Threshold(1.25));

        Assert.Equal(1.25, result.AppliedThreshold, 9);
    }

    /// <summary>
    /// <c>metrado-measurement</c>, requirement "Metrado Is Distinguishable from
    /// the Raw Revit Quantity": the raw value is carried alongside the metrado so
    /// no downstream consumer can substitute one for the other.
    /// </summary>
    [Fact]
    public void TheResultCarriesTheRawQuantitySeparatelyFromTheMetrado()
    {
        MetradoResult result = Measurement.Apply(
            SquareMetres(18.0),
            [SquareMetres(0.6)],
            Threshold(1.0));

        Assert.Equal(18.0, result.Raw.Value, 9);
        Assert.Equal(18.6, result.Metrado.Value, 9);
        Assert.NotEqual(result.Raw.Value, result.Metrado.Value, 9);
    }

    /// <summary>
    /// Domain never converts, so the metrado is expressed in the unit the raw
    /// quantity arrived in. Asserted on a volume to prove the unit is carried
    /// through rather than defaulted to the I1 wall unit.
    /// </summary>
    [Fact]
    public void TheMetradoIsExpressedInTheRawQuantitysUnit()
    {
        MetradoResult result = Measurement.Apply(
            new Quantity(8.0, QuantityUnit.CubicMetre),
            [new Quantity(0.3, QuantityUnit.CubicMetre)],
            Threshold(1.0, unit: QuantityUnit.CubicMetre));

        Assert.Equal(QuantityUnit.CubicMetre, result.Metrado.Unit);
        Assert.Equal(8.3, result.Metrado.Value, 9);
    }

    /// <summary>
    /// <c>TryCreate</c> rejects an undeclared mode, but C# gives every struct a
    /// reachable zero value, so <c>default(OpeningsThreshold)</c> arrives with a
    /// mode that was never configured.
    /// </summary>
    /// <remarks>
    /// Its value is 0, so the correction is disabled and the arithmetic would be
    /// safe — the metrado would equal the raw quantity either way. The refusal is
    /// not about the number: it is about the report. Returning a result stamped
    /// with mode <c>0</c> would claim a convention the product cannot name, and
    /// that claim reaches the workbook. Refusing matches how the rest of Domain
    /// already treats an undeclared enum reaching a consumer — see
    /// <c>QuantityUnit.Symbol()</c>, which throws the same exception type.
    /// </remarks>
    [Fact]
    public void AnUndeclaredBoundaryModeIsRefusedRatherThanReportedAsAConvention()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => Measurement.Apply(
                SquareMetres(18.0),
                [SquareMetres(0.6)],
                default));

        Assert.Contains("boundary mode", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The same refusal must hold for an element with no openings. This is the
    /// case that decides where the check lives: validating the mode inside the
    /// per-opening loop would let a solid wall slip through and return a result
    /// stamped with an unnamed convention, purely because the loop never ran.
    /// </summary>
    [Fact]
    public void AnUndeclaredBoundaryModeIsRefusedEvenWhenThereAreNoOpeningsToTest()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Measurement.Apply(SquareMetres(18.0), [], default));
    }

    /// <summary>
    /// The seam guarantees an openings list that is empty rather than null, so a
    /// null here is a broken extractor. Refusing names it instead of letting a
    /// <c>NullReferenceException</c> surface from inside the rule.
    /// </summary>
    [Fact]
    public void ANullOpeningsListIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => Measurement.Apply(SquareMetres(18.0), null!, Threshold(1.0)));
    }
}

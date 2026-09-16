namespace Metrado.Domain.Tests;

/// <summary>
/// The threshold is valid by construction (decision 6), so the measurement rule
/// needs no validation branch of its own. Everything that could make a threshold
/// nonsensical has to be rejected here, at the only door in.
/// </summary>
public sealed class OpeningsThresholdTests
{
    private static OpeningsThreshold Create(
        double value,
        QuantityUnit unit = QuantityUnit.SquareMetre,
        BoundaryMode mode = BoundaryMode.Exclusive)
    {
        Result<OpeningsThreshold, ConfigError> result = OpeningsThreshold.TryCreate(value, unit, mode);

        Assert.True(result.IsOk, $"Expected a valid threshold but got: {result}");
        return result.Value;
    }

    [Fact]
    public void AValidThresholdCarriesItsValueUnitAndMode()
    {
        OpeningsThreshold threshold = Create(1.0, QuantityUnit.SquareMetre, BoundaryMode.Inclusive);

        Assert.Equal(1.0, threshold.Value);
        Assert.Equal(QuantityUnit.SquareMetre, threshold.Unit);
        Assert.Equal(BoundaryMode.Inclusive, threshold.Mode);
    }

    /// <summary>
    /// "Zero threshold disables the correction" is a specified scenario, so zero
    /// is a legitimate threshold and must not be swept up with the negatives.
    /// </summary>
    [Fact]
    public void ZeroIsAValidThresholdBecauseItDisablesTheCorrection()
    {
        Assert.Equal(0.0, Create(0.0).Value);
    }

    /// <summary>
    /// The <c>takeoff-configuration</c> MUST: "loading fails with an error naming
    /// the category and the invalid value".
    /// </summary>
    [Fact]
    public void ANegativeThresholdIsRejectedNamingTheCategoryAndTheValue()
    {
        Result<OpeningsThreshold, ConfigError> result =
            OpeningsThreshold.TryCreate(-1.5, QuantityUnit.SquareMetre, BoundaryMode.Exclusive, "Walls");

        Assert.False(result.IsOk);
        Assert.Equal("Walls", result.Error.Category);
        Assert.Equal("-1.5", result.Error.InvalidValue);
        Assert.Contains("negative", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The value is formatted with the invariant culture, so the reported number
    /// does not change shape on a machine whose locale uses a decimal comma.
    /// </summary>
    [Fact]
    public void TheReportedInvalidValueIsCultureIndependent()
    {
        Result<OpeningsThreshold, ConfigError> result =
            OpeningsThreshold.TryCreate(-0.25, QuantityUnit.SquareMetre, BoundaryMode.Exclusive, "Floors");

        Assert.Equal("-0.25", result.Error.InvalidValue);
    }

    [Fact]
    public void ANegativeThresholdWithNoKnownCategoryStillReportsTheValue()
    {
        Result<OpeningsThreshold, ConfigError> result =
            OpeningsThreshold.TryCreate(-1.0, QuantityUnit.SquareMetre, BoundaryMode.Exclusive);

        Assert.False(result.IsOk);
        Assert.Null(result.Error.Category);
        Assert.Equal("-1", result.Error.InvalidValue);
    }

    /// <summary>
    /// NaN slips past a <c>value &lt; 0</c> check because every comparison against
    /// NaN is false. A NaN threshold would then silently deduct every opening —
    /// a wrong budget that raises nothing.
    /// </summary>
    [Fact]
    public void ANotANumberThresholdIsRejected()
    {
        Result<OpeningsThreshold, ConfigError> result =
            OpeningsThreshold.TryCreate(double.NaN, QuantityUnit.SquareMetre, BoundaryMode.Exclusive, "Walls");

        Assert.False(result.IsOk);
        Assert.Equal("Walls", result.Error.Category);
        Assert.Contains("number", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The <c>takeoff-configuration</c> MUST: any mode outside the closed domain
    /// is rejected, and the error lists the two accepted values.
    /// </summary>
    [Fact]
    public void AnUndeclaredBoundaryModeIsRejectedListingBothAcceptedValues()
    {
        Result<OpeningsThreshold, ConfigError> result =
            OpeningsThreshold.TryCreate(1.0, QuantityUnit.SquareMetre, (BoundaryMode)99, "Walls");

        Assert.False(result.IsOk);
        Assert.Contains("Exclusive", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Inclusive", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal("99", result.Error.InvalidValue);
    }

    [Fact]
    public void AnUnsetBoundaryModeIsRejected()
    {
        Result<OpeningsThreshold, ConfigError> result =
            OpeningsThreshold.TryCreate(1.0, QuantityUnit.SquareMetre, default);

        Assert.False(result.IsOk);
    }

    /// <summary>
    /// A threshold built with the product default must carry the exclusive
    /// convention, which is what the workbook later reports.
    /// </summary>
    [Fact]
    public void AThresholdBuiltWithTheProductDefaultIsExclusive()
    {
        Assert.Equal(BoundaryMode.Exclusive, Create(1.0, mode: Defaults.Mode).Mode);
    }

    [Fact]
    public void ThresholdsWithTheSameValueUnitAndModeAreEqual()
    {
        Assert.Equal(Create(1.0), Create(1.0));
        Assert.NotEqual(Create(1.0), Create(0.5));
        Assert.NotEqual(Create(1.0, mode: BoundaryMode.Exclusive), Create(1.0, mode: BoundaryMode.Inclusive));
        Assert.NotEqual(Create(1.0, QuantityUnit.SquareMetre), Create(1.0, QuantityUnit.CubicMetre));
    }
}

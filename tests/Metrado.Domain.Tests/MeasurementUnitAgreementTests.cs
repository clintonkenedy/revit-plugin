using static Metrado.Domain.Tests.MeasurementFixture;

namespace Metrado.Domain.Tests;

/// <summary>
/// <c>metrado-measurement</c>, requirement "Unit Conversion from Revit Internal
/// Units": the threshold comparison and the metrado arithmetic MUST be performed
/// in one consistent unit system.
/// </summary>
/// <remarks>
/// Domain never converts — conversion belongs to the Revit-facing layer, which is
/// the only place that has <c>UnitUtils</c> and the project's display units. So
/// the rule's only correct answer to units that disagree is to refuse. Converting
/// here would be guessing, and quietly comparing across unit systems would ship a
/// budget whose threshold was applied in the wrong scale.
/// </remarks>
public sealed class MeasurementUnitAgreementTests
{
    /// <summary>
    /// A raw quantity that is not in the threshold's unit cannot be compared
    /// against it. The comparison is refused rather than performed, and no result
    /// is produced at all.
    /// </summary>
    /// <remarks>
    /// Deliberately given <em>no</em> openings. With an opening present the
    /// per-opening check would refuse the element anyway, and this test would
    /// still pass with the raw quantity never inspected at all — leaving a solid
    /// wall measured in m³ free to be compared against an m² threshold. An
    /// empty list is what isolates the raw check from the opening check.
    /// </remarks>
    [Fact]
    public void ARawQuantityInAnotherUnitRefusesTheComparisonInsteadOfConverting()
    {
        MetradoOutcome outcome = Measurement.Apply(
            Wall("wall-volume-3"),
            CubicMetres(18.0),
            [],
            Threshold(1.0, unit: QuantityUnit.SquareMetre));

        Assert.Equal(MetradoStatus.UnitMismatch, outcome.Status);

        // Not a zero and not a passthrough of the raw value: there is no number
        // this rule could honestly return, so it returns none.
        Assert.Null(outcome.Result);

        Assert.NotNull(outcome.Warning);
        Assert.Equal("wall-volume-3", outcome.Warning.UniqueId);
    }

    /// <summary>
    /// Openings are compared against the threshold one by one, so each one has to
    /// agree with it. Checking only the raw quantity would let a single opening
    /// read in the wrong unit through, and that opening is added straight into the
    /// metrado.
    /// </summary>
    /// <remarks>
    /// The foreign opening is the second of three, so a rule that inspected only
    /// the first opening — or only the raw quantity — would measure this element
    /// and report a total that silently mixes areas with volumes.
    /// </remarks>
    [Fact]
    public void ASingleOpeningInAnotherUnitIsEnoughToRefuseTheWholeElement()
    {
        MetradoOutcome outcome = Measurement.Apply(
            Wall("wall-mixed-openings-9"),
            SquareMetres(18.0),
            [SquareMetres(0.4), CubicMetres(0.5), SquareMetres(0.3)],
            Threshold(1.0));

        Assert.Equal(MetradoStatus.UnitMismatch, outcome.Status);
        Assert.Null(outcome.Result);
        Assert.NotNull(outcome.Warning);
        Assert.Equal("wall-mixed-openings-9", outcome.Warning.UniqueId);
    }

    /// <summary>
    /// Units that agree are measured normally. Without this the refusal above is
    /// indistinguishable from a rule that refuses everything.
    /// </summary>
    [Fact]
    public void QuantitiesSharingTheThresholdsUnitAreMeasuredNormally()
    {
        MetradoOutcome outcome = Measurement.Apply(
            Wall(),
            CubicMetres(8.0),
            [CubicMetres(0.3)],
            Threshold(1.0, unit: QuantityUnit.CubicMetre));

        Assert.Equal(MetradoStatus.Measured, outcome.Status);
        Assert.NotNull(outcome.Result);
        Assert.Equal(8.3, outcome.Result.Metrado.Value, 9);
        Assert.Equal(QuantityUnit.CubicMetre, outcome.Result.Metrado.Unit);
        Assert.Null(outcome.Warning);
    }

    /// <summary>
    /// The refusal happens before the arithmetic, not after it.
    /// </summary>
    /// <remarks>
    /// This input would also break the gross bound, so a rule that measured first
    /// and checked units afterwards would return a clamped result carrying a
    /// number derived from a comparison it had no right to make. Refusing first
    /// means the only thing reported is the fault that made measurement
    /// impossible.
    /// </remarks>
    [Fact]
    public void TheUnitRefusalTakesPrecedenceOverTheGrossBound()
    {
        MetradoOutcome outcome = Measurement.Apply(
            Wall(),
            new Quantity(-1.0, QuantityUnit.CubicMetre),
            [CubicMetres(4.0)],
            Threshold(10.0, unit: QuantityUnit.SquareMetre));

        Assert.Equal(MetradoStatus.UnitMismatch, outcome.Status);
        Assert.Null(outcome.Result);

        // The fault reported is the refusal, not the bound. Reporting the clamp
        // would send the user to fix the model when the actual fault is in the
        // extraction, and the clamped figure itself came from a comparison the
        // rule had already decided it could not make.
        Assert.NotNull(outcome.Warning);
        Assert.Contains("refused", outcome.Warning.Condition, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "clamped", outcome.Warning.Condition, StringComparison.OrdinalIgnoreCase);
    }
}

namespace Metrado.Domain;

/// <summary>
/// The measurement convention one capitulo was actually measured under.
/// </summary>
/// <remarks>
/// Every field is read back off the measured lines rather than copied from the
/// criteria table, because <c>takeoff-configuration</c> requires the run to report
/// what was applied. A criterion that was configured but never reached an element
/// applied to nothing, and a convention that reached an element is reportable even
/// if no table currently says so.
/// <para>
/// Ordered sources are deliberately absent. They are an input to selecting a
/// quantity, not an observable property of the metrado that came out, so there is
/// nothing on a finished run to check a claim about them against.
/// </para>
/// </remarks>
/// <param name="Threshold">
/// The threshold value applied, expressed in <paramref name="Unit"/>. Reported
/// beside the mode because the same mode at a different threshold is a different
/// budget.
/// </param>
public sealed record AppliedCriterion(
    string Capitulo,
    QuantityUnit Unit,
    double Threshold,
    BoundaryMode Mode)
{
    /// <summary>The unit <see cref="Threshold"/> is in, which for a capitulo of layer lines in m3 is still m2.</summary>
    public QuantityUnit ThresholdUnit { get; init; } = Unit;
}

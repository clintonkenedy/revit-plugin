namespace Metrado.Domain;

/// <summary>
/// What the measurement rule was able to conclude about one element.
/// </summary>
/// <remarks>
/// A missing metrado is a status, never a number. Reporting "could not measure"
/// as <c>0.0</c> would make an unmeasurable element indistinguishable from one
/// genuinely measured as zero, and the difference is a whole line of the budget.
/// <para>
/// Members carry explicit values so zero stays undeclared, matching
/// <see cref="QuantityUnit"/> and <see cref="BoundaryMode"/>: an uninitialised
/// status must not silently claim the element was measured.
/// </para>
/// </remarks>
public enum MetradoStatus
{
    /// <summary>The rule produced a metrado. The outcome carries a result.</summary>
    Measured = 1,

    /// <summary>
    /// A quantity did not share the threshold's unit, so the comparison was
    /// refused and no metrado exists. Domain does not convert — the Revit-facing
    /// layer owns conversion, so reconciling the units here would be a guess
    /// dressed up as a measurement.
    /// </summary>
    UnitMismatch = 2,

    /// <summary>
    /// No source listed by the category's criterion had a value for this element,
    /// so nothing was measured. The outcome carries no result.
    /// </summary>
    /// <remarks>
    /// Distinct from a metrado of <c>0.0</c>, which is a measurement that happens
    /// to be empty. The specification is explicit that such an element "MUST NOT be
    /// silently assigned zero as if it were measured": the zero would total into
    /// its partida and read as a wall confirmed to have no area, rather than as a
    /// wall the export could not measure.
    /// </remarks>
    NoSource = 3,

    /// <summary>
    /// A counted category (N1): its criterion lists no quantity source, so the
    /// element counts as one instance, in <c>u</c>. Never a category whose
    /// listed sources all came up empty; that is <see cref="NoSource"/>.
    /// </summary>
    Counted = 4,
}

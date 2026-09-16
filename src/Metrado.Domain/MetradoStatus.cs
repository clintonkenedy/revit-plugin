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
}

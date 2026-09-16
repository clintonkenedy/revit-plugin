namespace Metrado.Domain;

/// <summary>
/// The outcome of the openings correction for one element: the metrado, the raw
/// Revit quantity it was derived from, and the convention that produced it.
/// </summary>
/// <param name="Metrado">
/// The measured quantity for budgeting, expressed in <paramref name="Raw"/>'s unit.
/// </param>
/// <param name="Raw">
/// The Revit-computed quantity, with every opening already subtracted. Carried
/// separately because <c>metrado-measurement</c> requires that the raw value is
/// never substituted for the metrado downstream — they are different numbers with
/// different meanings, and conflating them is how a wrong budget ships.
/// </param>
/// <param name="Gross">
/// What the element would measure with no opening deducted at all, defined by
/// <c>metrado-measurement</c> as <c>rawQuantity + Σ q(o)</c> over <em>every</em>
/// opening. It is the ceiling on the correction: adding openings back can reach
/// it but never pass it, because there is no more material to restore than Revit
/// removed.
/// </param>
/// <param name="AppliedMode">
/// The boundary convention actually applied. Recorded rather than assumed: the
/// mode is configurable, so a result that does not name its own convention leaves
/// two defensible budgets indistinguishable.
/// </param>
/// <param name="AppliedThreshold">
/// The threshold value actually applied, in <paramref name="Raw"/>'s unit. The
/// other half of the convention — the same mode at a different threshold is a
/// different budget.
/// </param>
/// <param name="ClampedToGross">
/// Whether the gross bound had to be enforced on this element. True means the
/// extraction input was inconsistent with its own gross quantity, so
/// <paramref name="Metrado"/> is a bound rather than the number the correction
/// produced. It always travels with a validation warning naming the element:
/// carrying the flag alone would let a reviewer read a bounded figure as a
/// measured one.
/// </param>
public sealed record MetradoResult(
    Quantity Metrado,
    Quantity Raw,
    Quantity Gross,
    BoundaryMode AppliedMode,
    double AppliedThreshold,
    bool ClampedToGross);

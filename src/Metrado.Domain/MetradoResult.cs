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
/// <remarks>
/// Task 1.8 adds the gross quantity and the <c>ClampedToGross</c> flag to this
/// record, and wraps it in a <c>MetradoOutcome</c> carrying the measurement
/// status. Neither is modelled here because neither has a behaviour yet.
/// </remarks>
public sealed record MetradoResult(
    Quantity Metrado,
    Quantity Raw,
    BoundaryMode AppliedMode,
    double AppliedThreshold);

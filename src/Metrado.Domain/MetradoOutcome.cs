namespace Metrado.Domain;

/// <summary>
/// Everything the measurement rule concluded about one element: whether it could
/// be measured, the numbers when it could, and anything the user needs to fix.
/// </summary>
/// <param name="Status">What the rule concluded.</param>
/// <param name="Result">
/// The measured quantities, or <c>null</c> when <paramref name="Status"/> is not
/// <see cref="MetradoStatus.Measured"/>. Nullable rather than a zeroed result,
/// because a caller that forgets to check the status gets a null reference at the
/// point of the mistake instead of a plausible zero in the workbook.
/// </param>
/// <param name="Warning">
/// The finding the user must act on, or <c>null</c> when there is nothing to
/// report. Carried with the outcome rather than collected separately so a refused
/// or clamped element cannot reach the workbook without its explanation.
/// </param>
public sealed record MetradoOutcome(
    MetradoStatus Status,
    MetradoResult? Result,
    ValidationWarning? Warning);

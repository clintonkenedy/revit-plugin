namespace Metrado.Domain;

/// <summary>
/// One individual opening subtracted from a host element by Revit — a door, a
/// window, a shaft.
/// </summary>
/// <remarks>
/// Openings are carried one by one and are never pre-aggregated (decision D4).
/// The measurement rule compares each opening against the threshold on its own,
/// so a summed field would make "the rule MUST NOT compare the sum of openings
/// against the threshold" impossible to enforce. Each opening keeps its own
/// identifier so a clamp warning can name it.
/// </remarks>
public sealed record OpeningQuantity
{
    public OpeningQuantity(string uniqueId, Quantity amount)
    {
        UniqueId = Guard.RequiredText(uniqueId, nameof(uniqueId));
        Amount = amount;
    }

    public string UniqueId { get; }

    public Quantity Amount { get; }
}

namespace Metrado.Domain;

/// <summary>
/// A quantity as Revit computed it, before the openings correction. Revit has
/// already subtracted every opening, which is exactly why this is geometry rather
/// than metrado.
/// </summary>
/// <remarks>
/// <c>SourceKey</c> stays a plain string so the user-nominated quantity sources
/// arriving in I2 need no Domain change. <c>Material</c> is null for a
/// whole-element quantity and populated for a single material layer (I3).
/// </remarks>
public sealed record RawQuantity
{
    public RawQuantity(string sourceKey, Quantity amount, MaterialRef? material = null)
    {
        SourceKey = Guard.RequiredText(sourceKey, nameof(sourceKey));
        Amount = amount;
        Material = material;
    }

    public string SourceKey { get; }

    public Quantity Amount { get; }

    public MaterialRef? Material { get; }
}

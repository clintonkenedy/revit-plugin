namespace Metrado.Domain;

/// <summary>
/// One measurable element as it crosses the Revit boundary. This DTO is the
/// reason Domain and Excel compile and test with no Revit installed (decision D4).
/// </summary>
/// <param name="UniqueId">Stable Revit identifier. Every validation warning names it.</param>
/// <param name="TypeKey">
/// Type identity only. It attributes type-level parameter reads and lets the
/// writer name contributing types; it is deliberately <em>not</em> part of the
/// partida grouping key.
/// </param>
/// <param name="Quantities">Revit-computed amounts, openings already subtracted.</param>
/// <param name="Openings">Individual openings, never pre-aggregated.</param>
/// <remarks>
/// <c>UniqueId</c> and the two lists are validated on construction and on the
/// <c>with</c> copy path, because <c>with</c> copies backing state directly and a
/// constructor guard alone would leave that path unchecked.
/// </remarks>
public sealed record ElementTakeoff(
    string UniqueId,
    string CategoryName,
    string FamilyName,
    string TypeName,
    string TypeKey,
    CodificationReadings Codes,
    IReadOnlyList<RawQuantity> Quantities,
    IReadOnlyList<OpeningQuantity> Openings)
{
    private readonly string _uniqueId = Guard.RequiredText(UniqueId, nameof(UniqueId));

    private readonly IReadOnlyList<RawQuantity> _quantities =
        Guard.RequiredValue(Quantities, nameof(Quantities));

    private readonly IReadOnlyList<OpeningQuantity> _openings =
        Guard.RequiredValue(Openings, nameof(Openings));

    public string UniqueId
    {
        get => _uniqueId;
        init => _uniqueId = Guard.RequiredText(value, nameof(UniqueId));
    }

    public IReadOnlyList<RawQuantity> Quantities
    {
        get => _quantities;
        init => _quantities = Guard.RequiredValue(value, nameof(Quantities));
    }

    /// <summary>
    /// Empty when the element has no doors or windows — never null. The rule
    /// iterates this list unconditionally, so null would be a crash rather than
    /// the "element with no openings" scenario the specification describes.
    /// </summary>
    public IReadOnlyList<OpeningQuantity> Openings
    {
        get => _openings;
        init => _openings = Guard.RequiredValue(value, nameof(Openings));
    }
}

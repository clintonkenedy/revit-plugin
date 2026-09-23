using Metrado.Domain;

namespace Metrado.Revit2027;

/// <summary>
/// One wall as read from the model, in Revit's internal units (square feet),
/// before any conversion. Holds no Revit type, so the step from reading to
/// <see cref="ElementTakeoff"/> can be proved without Revit running.
/// </summary>
public sealed record WallReading(
    string UniqueId,
    string FamilyName,
    string TypeName,
    string TypeUniqueId,
    string? AssemblyCode,
    double ComputedAreaSquareFeet,
    IReadOnlyList<OpeningReading> Openings);

/// <summary>One opening Revit subtracted from a wall, read on its own and never summed.</summary>
public sealed record OpeningReading(string UniqueId, double AreaSquareFeet);

/// <summary>
/// Turns a <see cref="WallReading"/> into the domain's <see cref="ElementTakeoff"/>.
/// </summary>
public static class WallTakeoff
{
    /// <summary>
    /// The criteria key for walls — never <c>Category.Name</c>, which Revit
    /// localizes ("Muros" under a Spanish UI) and which would then match no
    /// criterion, leaving every wall unmeasured.
    /// </summary>
    public const string CategoryKey = "Walls";

    /// <summary>The source the built-in Walls criterion reads.</summary>
    public const string ComputedAreaSource = "HOST_AREA_COMPUTED";

    /// <param name="reading">The wall as read, in square feet.</param>
    /// <param name="squareFeetToSquareMetres">
    /// Inside Revit, <c>UnitUtils.ConvertFromInternalUnits</c> to square metres.
    /// Injected rather than written here as a factor, so the conversion stays
    /// Revit's own and this mapping stays runnable outside it.
    /// </param>
    public static ElementTakeoff From(WallReading reading, Func<double, double> squareFeetToSquareMetres)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(squareFeetToSquareMetres);

        Quantity SquareMetres(double squareFeet) =>
            new(squareFeetToSquareMetres(squareFeet), QuantityUnit.SquareMetre);

        return new ElementTakeoff(
            UniqueId: reading.UniqueId,
            CategoryName: CategoryKey,
            FamilyName: reading.FamilyName,
            TypeName: reading.TypeName,
            TypeKey: reading.TypeUniqueId,
            Codes: new CodificationReadings(
                reading.AssemblyCode,
                keynote: null,
                sharedParameters: new Dictionary<string, string?>()),
            Quantities: [new RawQuantity(ComputedAreaSource, SquareMetres(reading.ComputedAreaSquareFeet))],
            Openings: [.. reading.Openings.Select(opening =>
                new OpeningQuantity(opening.UniqueId, SquareMetres(opening.AreaSquareFeet)))]);
    }
}

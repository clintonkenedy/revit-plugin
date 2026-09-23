using Metrado.Domain;

namespace Metrado.Revit2027;

/// <summary>What a quantity measures, which decides its unit once converted.</summary>
public enum QuantityKind
{
    /// <summary>A length, read in feet and converted to metres.</summary>
    Length = 1,
}

/// <param name="SourceKey">The built-in parameter's name, as criteria list their sources.</param>
/// <param name="InternalValue">The value in Revit's internal units (feet).</param>
public sealed record QuantityReading(string SourceKey, QuantityKind Kind, double InternalValue);

/// <summary>
/// The codes a type holds, by built-in identifier, and the nominated shared
/// parameter's value keyed by its GUID; none of it judged yet.
/// </summary>
public sealed record CodesReading(string? AssemblyCode, string? Keynote, IReadOnlyDictionary<string, string?> SharedParameters);

/// <summary>
/// One element other than a wall, as read from the model, before conversion:
/// a railing with its length, a door or a window with no quantity at all
/// (they are counted). Holds no Revit type, so the step to
/// <see cref="ElementTakeoff"/> can be proved without Revit running.
/// </summary>
/// <param name="CategoryKey">The key the criteria know the category by, never Revit's localized name.</param>
public sealed record ElementReading(
    string UniqueId,
    string CategoryKey,
    string FamilyName,
    string TypeName,
    string TypeUniqueId,
    CodesReading Codes,
    IReadOnlyList<QuantityReading> Quantities);

/// <summary>Turns an <see cref="ElementReading"/> into the domain's <see cref="ElementTakeoff"/>.</summary>
public static class ElementTakeoffs
{
    /// <param name="feetToMetres">Revit's own conversion, never a hand-written factor.</param>
    public static ElementTakeoff From(ElementReading reading, Func<double, double> feetToMetres)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(feetToMetres);

        return new ElementTakeoff(
            UniqueId: reading.UniqueId,
            CategoryName: reading.CategoryKey,
            FamilyName: reading.FamilyName,
            TypeName: reading.TypeName,
            TypeKey: reading.TypeUniqueId,
            Codes: new CodificationReadings(reading.Codes.AssemblyCode, reading.Codes.Keynote, reading.Codes.SharedParameters),
            // A value Revit could not give is left out rather than read as
            // zero: the domain then reports no source, instead of pricing nothing.
            Quantities: [.. reading.Quantities
                .Where(quantity => double.IsFinite(quantity.InternalValue))
                .Select(quantity => new RawQuantity(quantity.SourceKey, new Quantity(Math.Round(feetToMetres(quantity.InternalValue), 9), QuantityUnit.Metre)))],
            Openings: []);
    }
}

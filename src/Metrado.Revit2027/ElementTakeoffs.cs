using Metrado.Domain;

namespace Metrado.Revit2027;

/// <summary>What a quantity measures. Only lengths are read so far, converted from feet to metres.</summary>
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
/// <param name="Conditions">What the estimator must know about the quantity read, one warning each.</param>
public sealed record ElementReading(
    string UniqueId,
    string CategoryKey,
    string FamilyName,
    string TypeName,
    string TypeUniqueId,
    CodesReading Codes,
    IReadOnlyList<QuantityReading> Quantities,
    IReadOnlyList<string>? Conditions = null);

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

    /// <summary>
    /// A railing's condition when a multistory stair repeats it: the copies
    /// are subelements no collector returns, and Revit gives the length of
    /// one storey. Null on a single storey.
    /// </summary>
    public static string? RepeatedOn(int storeys) =>
        storeys > 1
            ? $"A multistory stair repeats this railing on {storeys} storeys, and Revit gives the length of one, "
                + $"which is what the budget carries: add the other {storeys - 1} by hand."
            : null;

    /// <summary>One warning per condition the reading carries, naming the element.</summary>
    public static IReadOnlyList<ValidationWarning> Warnings(ElementTakeoff takeoff, ElementReading reading)
    {
        ArgumentNullException.ThrowIfNull(takeoff);
        ArgumentNullException.ThrowIfNull(reading);

        return [.. (reading.Conditions ?? []).Select(condition => ValidationWarning.ForElement(takeoff, condition))];
    }
}

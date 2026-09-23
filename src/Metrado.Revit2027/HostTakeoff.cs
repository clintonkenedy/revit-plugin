using Metrado.Domain;

namespace Metrado.Revit2027;

/// <summary>
/// One wall as read from the model, in Revit's internal units (square feet),
/// before any conversion. Holds no Revit type, so the step from reading to
/// <see cref="ElementTakeoff"/> can be proved without Revit running.
/// </summary>
/// <param name="ComputedAreaSquareFeet">Null when the wall carries no readable, finite computed area.</param>
/// <param name="Openings">Openings measured individually.</param>
/// <param name="Unmeasured">Openings Revit subtracted whose own area could not be measured.</param>
public sealed record HostReading(
    string UniqueId,
    string FamilyName,
    string TypeName,
    string TypeUniqueId,
    string? AssemblyCode,
    double? ComputedAreaSquareFeet,
    IReadOnlyList<OpeningReading> Openings,
    IReadOnlyList<UnmeasuredOpening> Unmeasured,
    string? Keynote = null,
    IReadOnlyDictionary<string, string?>? SharedParameters = null);

/// <summary>One opening Revit subtracted from a wall, read on its own and never summed.</summary>
public sealed record OpeningReading(string UniqueId, double AreaSquareFeet);

/// <summary>An opening that cuts the wall but whose area could not be measured, and why.</summary>
public sealed record UnmeasuredOpening(string UniqueId, string Reason);

/// <summary>
/// Turns a <see cref="HostReading"/> into the domain's <see cref="ElementTakeoff"/>.
/// </summary>
public static class HostTakeoff
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
    public static ElementTakeoff From(HostReading reading, Func<double, double> squareFeetToSquareMetres)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(squareFeetToSquareMetres);

        // Rounded to the nano-square-metre: far below any real quantity, and
        // enough to keep the feet round trip's noise from deciding whether an
        // opening modelled at exactly the threshold is added back.
        Quantity SquareMetres(double squareFeet) =>
            new(Math.Round(squareFeetToSquareMetres(squareFeet), 9), QuantityUnit.SquareMetre);

        return new ElementTakeoff(
            UniqueId: reading.UniqueId,
            CategoryName: CategoryKey,
            FamilyName: reading.FamilyName,
            TypeName: reading.TypeName,
            TypeKey: reading.TypeUniqueId,
            Codes: new CodificationReadings(
                reading.AssemblyCode,
                reading.Keynote,
                sharedParameters: reading.SharedParameters ?? new Dictionary<string, string?>()),
            // No quantity rather than zero: the domain then reports that no
            // source had a value, instead of pricing the wall at nothing.
            Quantities: reading.ComputedAreaSquareFeet is double area
                ? [new RawQuantity(ComputedAreaSource, SquareMetres(area))]
                : [],
            Openings: [.. reading.Openings.Select(opening =>
                new OpeningQuantity(opening.UniqueId, SquareMetres(opening.AreaSquareFeet)))]);
    }

    /// <summary>
    /// One warning per opening that cuts the wall but could not be measured.
    /// Such an opening is left out of <see cref="ElementTakeoff.Openings"/>, so
    /// Revit's deduction of it stands and it is never added back; the warning
    /// is what keeps that from being silent.
    /// </summary>
    public static IReadOnlyList<ValidationWarning> Warnings(ElementTakeoff takeoff, HostReading reading)
    {
        ArgumentNullException.ThrowIfNull(takeoff);
        ArgumentNullException.ThrowIfNull(reading);

        return [.. reading.Unmeasured.Select(opening => ValidationWarning.ForElement(
            takeoff,
            $"Opening {opening.UniqueId} cuts this wall but its area could not be measured ({opening.Reason}). "
            + "Revit's deduction of it is kept, so it is never added back, even below the threshold."))];
    }
}

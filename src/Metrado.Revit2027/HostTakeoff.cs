using Metrado.Domain;

namespace Metrado.Revit2027;

/// <summary>
/// One host — a wall, a floor or a roof — as read from the model, in Revit's
/// internal units (square feet), before any conversion. Holds no Revit type, so the step from reading to
/// <see cref="ElementTakeoff"/> can be proved without Revit running.
/// </summary>
/// <param name="CategoryKey">The key the criteria know the category by, never Revit's localized name.</param>
/// <param name="ComputedAreaSquareFeet">Null when the host carries no readable, finite computed area.</param>
/// <param name="Openings">Openings measured individually.</param>
/// <param name="Unmeasured">Openings Revit subtracted whose own area could not be measured.</param>
/// <param name="Layers">The host's layers, read only when its category is taken off by material layer.</param>
public sealed record HostReading(
    string UniqueId,
    string CategoryKey,
    string FamilyName,
    string TypeName,
    string TypeUniqueId,
    string? AssemblyCode,
    double? ComputedAreaSquareFeet,
    IReadOnlyList<OpeningReading> Openings,
    IReadOnlyList<UnmeasuredOpening> Unmeasured,
    string? Keynote = null,
    IReadOnlyDictionary<string, string?>? SharedParameters = null,
    LayerReading? Layers = null);

/// <summary>One opening Revit subtracted from a host, read on its own and never summed.</summary>
public sealed record OpeningReading(string UniqueId, double AreaSquareFeet);

/// <summary>An opening that cuts the host but whose area could not be measured, and why.</summary>
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
    public const string WallsKey = "Walls";

    public const string FloorsKey = "Floors";

    public const string RoofsKey = "Roofs";

    /// <summary>The source the built-in Walls, Floors and Roofs criteria read.</summary>
    public const string ComputedAreaSource = "HOST_AREA_COMPUTED";

    /// <summary>A host not taken off by layer, which needs square metres only.</summary>
    /// <exception cref="ArgumentException">The reading has layers, which need volumes and widths converted too.</exception>
    public static ElementTakeoff From(HostReading reading, Func<double, double> squareFeetToSquareMetres)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(squareFeetToSquareMetres);

        return reading.Layers is null
            ? From(reading, new SeamUnits(squareFeetToSquareMetres, NotLayered, NotLayered))
            : throw new ArgumentException("A layered host needs cubic metres and metres converted too.", nameof(reading));
    }

    /// <param name="reading">The host as read, in Revit's internal units.</param>
    /// <param name="units">
    /// Inside Revit, <c>UnitUtils.ConvertFromInternalUnits</c>. Injected rather
    /// than written here as factors, so the conversions stay Revit's own and
    /// this mapping stays runnable outside it.
    /// </param>
    public static ElementTakeoff From(HostReading reading, SeamUnits units)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(units);

        Func<double, double> squareFeetToSquareMetres = units.SquareMetres;
        (IReadOnlyList<RawQuantity> layered, LayerStructure? structure) = reading.Layers is LayerReading layers
            ? LayerTakeoff.From(layers, units)
            : ([], null);

        // Rounded to the nano-square-metre: far below any real quantity, and
        // enough to keep the feet round trip's noise from deciding whether an
        // opening modelled at exactly the threshold is added back.
        Quantity SquareMetres(double squareFeet) =>
            new(Math.Round(squareFeetToSquareMetres(squareFeet), 9), QuantityUnit.SquareMetre);

        return new ElementTakeoff(
            UniqueId: reading.UniqueId,
            CategoryName: reading.CategoryKey,
            FamilyName: reading.FamilyName,
            TypeName: reading.TypeName,
            TypeKey: reading.TypeUniqueId,
            Codes: new CodificationReadings(
                reading.AssemblyCode,
                reading.Keynote,
                sharedParameters: reading.SharedParameters ?? new Dictionary<string, string?>()),
            // No quantity rather than zero: the domain then reports that no
            // source had a value, instead of pricing the host at nothing.
            Quantities: [.. reading.ComputedAreaSquareFeet is double area
                ? [new RawQuantity(ComputedAreaSource, SquareMetres(area))]
                : Array.Empty<RawQuantity>(), .. layered],
            Openings: [.. reading.Openings.Select(opening =>
                new OpeningQuantity(opening.UniqueId, SquareMetres(opening.AreaSquareFeet)))],
            Layers: structure);
    }

    private static double NotLayered(double value) =>
        throw new InvalidOperationException("Only a layered host converts volumes or widths.");

    /// <summary>
    /// One warning per opening that cuts the host but could not be measured.
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
            $"Opening {opening.UniqueId} cuts this {Noun(reading.CategoryKey)} but its area could not be measured ({opening.Reason}). "
            + "Revit's deduction of it is kept, so it is never added back, even below the threshold."))];
    }

    /// <summary>
    /// A floor or roof modelled in place: a family instance with no computed
    /// area, which this increment does not measure. The warning is what keeps
    /// its absence from the budget from being silent.
    /// </summary>
    public static ValidationWarning NotRead(string uniqueId, string categoryKey, string familyName, string typeName) =>
        new(uniqueId, categoryKey, familyName, typeName,
            $"This in-place {Noun(categoryKey)} is a family instance with no computed area, which Metrado does not measure: "
            + $"it is in no line of the budget. Measure it by hand, or model it as a {Noun(categoryKey)}.");

    internal static string Noun(string categoryKey) => categoryKey switch
    {
        WallsKey => "wall",
        FloorsKey => "floor",
        RoofsKey => "roof",
        _ => "element",
    };
}

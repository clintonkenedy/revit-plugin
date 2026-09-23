using Metrado.Domain;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins how an element other than a wall becomes the domain's takeoff (task
/// 2.6): railings carry their length in metres, doors and windows carry no
/// quantity at all (they are counted), and every element carries the codes
/// its type holds, keyed by the category the criteria know it by.
/// </summary>
public sealed class ElementTakeoffsTests
{
    private static readonly CodesReading Codes = new("C1020", "08 14 16.A1", new Dictionary<string, string?> { ["guid-1"] = "S-10" });

    [Fact]
    public void ARailingCarriesItsLengthInMetres()
    {
        ElementReading railing = new("r-1", "Railings", "Railing", "900mm Pipe", "t-1", Codes, [new QuantityReading("CURVE_ELEM_LENGTH", QuantityKind.Length, 10.0)]);

        ElementTakeoff takeoff = ElementTakeoffs.From(railing, feet => feet * 0.3048);

        RawQuantity length = Assert.Single(takeoff.Quantities);
        Assert.Equal("CURVE_ELEM_LENGTH", length.SourceKey);
        Assert.Equal(new Quantity(3.048, QuantityUnit.Metre), length.Amount);
    }

    /// <summary>A door has no quantity to read: it is counted, one per instance, by its criterion.</summary>
    [Fact]
    public void ADoorCarriesNoQuantity()
    {
        ElementTakeoff takeoff = ElementTakeoffs.From(new ElementReading("d-1", "Doors", "Single-Flush", "0915 x 2134mm", "t-2", Codes, []), feet => feet);

        Assert.Equal("Doors", takeoff.CategoryName);
        Assert.Empty(takeoff.Quantities);
        Assert.Empty(takeoff.Openings);
    }

    [Fact]
    public void EveryCodeTheTypeHoldsReachesTheTakeoff()
    {
        ElementTakeoff takeoff = ElementTakeoffs.From(new ElementReading("w-1", "Windows", "Fixed", "0915 x 1220mm", "t-3", Codes, []), feet => feet);

        Assert.Equal("C1020", takeoff.Codes.AssemblyCode);
        Assert.Equal("08 14 16.A1", takeoff.Codes.Keynote);
        Assert.Equal("S-10", takeoff.Codes.SharedParameters["guid-1"]);
        Assert.Equal("t-3", takeoff.TypeKey);
    }

    /// <summary>A length Revit could not give (a railing with no path) is left out, never read as zero.</summary>
    [Fact]
    public void AMissingLengthIsLeftOutNotZero()
    {
        ElementReading railing = new("r-2", "Railings", "Railing", "900mm Pipe", "t-1", Codes, [new QuantityReading("CURVE_ELEM_LENGTH", QuantityKind.Length, double.NaN)]);

        Assert.Empty(ElementTakeoffs.From(railing, feet => feet * 0.3048).Quantities);
    }

    /// <summary>A wall's Keynote reaches its takeoff too, so the chain's second link has something to read.</summary>
    [Fact]
    public void AWallsKeynoteReachesItsTakeoff()
    {
        WallReading wall = new("w-1", "Basic Wall", "Generic - 200mm", "t-4", AssemblyCode: null, 100.0, [], [], Keynote: "04 21 00.A1");

        Assert.Equal("04 21 00.A1", WallTakeoff.From(wall, squareFeet => squareFeet).Codes.Keynote);
    }

    [Fact]
    public void AWallsSharedParameterReachesItsTakeoff()
    {
        WallReading wall = new("w-1", "Basic Wall", "Generic - 200mm", "t-4", AssemblyCode: null, 100.0, [], [],
            SharedParameters: new Dictionary<string, string?> { ["guid-1"] = "S-10" });

        Assert.Equal("S-10", WallTakeoff.From(wall, squareFeet => squareFeet).Codes.SharedParameters["guid-1"]);
    }
}

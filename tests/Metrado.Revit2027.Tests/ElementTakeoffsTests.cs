using Metrado.Domain;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins how an element other than a wall, as read, becomes the domain's
/// takeoff (task 2.6): a railing's length arrives in metres, a door or
/// window read with no quantity stays without one (they are counted), and
/// the codes and the category key pass through. What the reader itself
/// reads from Revit is proved on the host, not here.
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

    /// <summary>Every name the reading carries passes through as read, and a length is rounded to the nanometre only.</summary>
    [Fact]
    public void IdentityNamesAndRoundingPassThrough()
    {
        ElementReading railing = new("r-1", "Railings", "Railing", "900mm Pipe", "t-1", Codes, [new QuantityReading("CURVE_ELEM_LENGTH", QuantityKind.Length, 10.123456789012)]);

        ElementTakeoff takeoff = ElementTakeoffs.From(railing, feet => feet * 0.3048);

        Assert.Equal(("r-1", "Railings", "Railing", "900mm Pipe"), (takeoff.UniqueId, takeoff.CategoryName, takeoff.FamilyName, takeoff.TypeName));
        Assert.Equal(Math.Round(10.123456789012 * 0.3048, 9), Assert.Single(takeoff.Quantities).Amount.Value);
        Assert.NotEqual(10.123456789012 * 0.3048, Assert.Single(takeoff.Quantities).Amount.Value);
    }

    /// <summary>
    /// A multistory stair repeats its railing on every storey as subelements
    /// no collector returns, and Revit gives one storey's length. The number
    /// stays Revit's, but never silently.
    /// </summary>
    [Fact]
    public void ARailingRepeatedOnSeveralStoreysIsWarnedAbout()
    {
        string condition = Assert.IsType<string>(ElementTakeoffs.RepeatedOn(8));
        ElementReading railing = new("r-1", "Railings", "Railing", "900mm Pipe", "t-1", Codes, [], Conditions: [condition]);

        ValidationWarning warning = Assert.Single(ElementTakeoffs.Warnings(ElementTakeoffs.From(railing, feet => feet), railing));

        Assert.Equal(("r-1", "Railings"), (warning.UniqueId, warning.CategoryName));
        Assert.Contains("8 storeys", warning.Condition, StringComparison.Ordinal);
        Assert.Contains("other 7", warning.Condition, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void ARailingOnOneStoreyHasNothingToWarnAbout(int storeys)
    {
        ElementReading railing = new("r-1", "Railings", "Railing", "900mm Pipe", "t-1", Codes, []);

        Assert.Null(ElementTakeoffs.RepeatedOn(storeys));
        Assert.Empty(ElementTakeoffs.Warnings(ElementTakeoffs.From(railing, feet => feet), railing));
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
        HostReading wall = new("w-1", "Walls", "Basic Wall", "Generic - 200mm", "t-4", AssemblyCode: null, 100.0, [], [], Keynote: "04 21 00.A1");

        Assert.Equal("04 21 00.A1", HostTakeoff.From(wall, squareFeet => squareFeet).Codes.Keynote);
    }

    [Fact]
    public void AWallsSharedParameterReachesItsTakeoff()
    {
        HostReading wall = new("w-1", "Walls", "Basic Wall", "Generic - 200mm", "t-4", AssemblyCode: null, 100.0, [], [],
            SharedParameters: new Dictionary<string, string?> { ["guid-1"] = "S-10" });

        Assert.Equal("S-10", HostTakeoff.From(wall, squareFeet => squareFeet).Codes.SharedParameters["guid-1"]);
    }
}

namespace Metrado.Domain.Tests;

/// <summary>
/// The seam DTOs (decision D4). These types are why Domain and Excel build with
/// no Revit installed, so the guarantees the measurement rule depends on have to
/// be structural here rather than conventions the adapter promises to honour.
/// </summary>
public sealed class ElementTakeoffTests
{
    private static readonly CodificationReadings NoCodes =
        new(assemblyCode: null, keynote: null, sharedParameters: new Dictionary<string, string?>());

    private static ElementTakeoff Wall(
        string uniqueId = "wall-1",
        IReadOnlyList<RawQuantity>? quantities = null,
        IReadOnlyList<OpeningQuantity>? openings = null) =>
        new(
            UniqueId: uniqueId,
            CategoryName: "Walls",
            FamilyName: "Basic Wall",
            TypeName: "Generic - 200mm",
            TypeKey: "Basic Wall:Generic - 200mm",
            Codes: NoCodes,
            Quantities: quantities ?? [],
            Openings: openings ?? []);

    [Fact]
    public void ElementCarriesTheIdentityEveryWarningNeedsToAttributeIt()
    {
        ElementTakeoff wall = Wall(uniqueId: "abc-123");

        Assert.Equal("abc-123", wall.UniqueId);
        Assert.Equal("Walls", wall.CategoryName);
        Assert.Equal("Basic Wall", wall.FamilyName);
        Assert.Equal("Generic - 200mm", wall.TypeName);
        Assert.Equal("Basic Wall:Generic - 200mm", wall.TypeKey);
    }

    /// <summary>
    /// An element with no stable identifier cannot be named in a validation
    /// warning, so the budget would report a problem nobody can locate.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankElementUniqueIdIsRejectedAtConstruction(string? uniqueId)
    {
        Assert.Throws<ArgumentException>(() => Wall(uniqueId: uniqueId!));
    }

    [Fact]
    public void CopyingAnElementWithABlankUniqueIdIsAlsoRejected()
    {
        ElementTakeoff wall = Wall();

        Assert.Throws<ArgumentException>(() => wall with { UniqueId = "" });
    }

    /// <summary>
    /// A wall with no doors or windows has an empty openings list. The rule
    /// iterates that list unconditionally, so null would be a crash rather than
    /// the "element with no openings" scenario the specification describes.
    /// </summary>
    [Fact]
    public void AnElementWithNoOpeningsCarriesAnEmptyListRatherThanNull()
    {
        ElementTakeoff wall = Wall(openings: []);

        Assert.NotNull(wall.Openings);
        Assert.Empty(wall.Openings);
    }

    /// <summary>
    /// Constructed directly rather than through <see cref="Wall"/>, whose
    /// null-coalescing defaults would substitute an empty list and hide the very
    /// thing under test.
    /// </summary>
    [Fact]
    public void NullOpeningsAreRejectedAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new ElementTakeoff(
            "wall-1", "Walls", "Basic Wall", "Generic - 200mm",
            "Basic Wall:Generic - 200mm", NoCodes, [], null!));
    }

    [Fact]
    public void NullQuantitiesAreRejectedAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new ElementTakeoff(
            "wall-1", "Walls", "Basic Wall", "Generic - 200mm",
            "Basic Wall:Generic - 200mm", NoCodes, null!, []));
    }

    /// <summary>
    /// Decision D4: openings stay individual because the rule compares each one
    /// against the threshold. A pre-summed field would make "openings MUST NOT be
    /// summed against the threshold" unenforceable, so the DTO must preserve each
    /// opening as its own entry with its own identity.
    /// </summary>
    [Fact]
    public void OpeningsArePreservedIndividuallyAndNeverPreAggregated()
    {
        ElementTakeoff wall = Wall(openings:
        [
            new OpeningQuantity("door-1", new Quantity(0.4, QuantityUnit.SquareMetre)),
            new OpeningQuantity("window-1", new Quantity(0.9, QuantityUnit.SquareMetre)),
            new OpeningQuantity("window-2", new Quantity(2.5, QuantityUnit.SquareMetre)),
        ]);

        Assert.Equal(3, wall.Openings.Count);
        Assert.Equal(
            [0.4, 0.9, 2.5],
            wall.Openings.Select(opening => opening.Amount.Value));
        Assert.Equal(
            ["door-1", "window-1", "window-2"],
            wall.Openings.Select(opening => opening.UniqueId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankOpeningUniqueIdIsRejected(string? uniqueId)
    {
        Assert.Throws<ArgumentException>(
            () => new OpeningQuantity(uniqueId!, new Quantity(0.4, QuantityUnit.SquareMetre)));
    }

    [Fact]
    public void RawQuantityDescribesWhichSourceItWasReadFrom()
    {
        RawQuantity area = new("Area", new Quantity(18.0, QuantityUnit.SquareMetre));

        Assert.Equal("Area", area.SourceKey);
        Assert.Equal(new Quantity(18.0, QuantityUnit.SquareMetre), area.Amount);
    }

    /// <summary>
    /// A null material means the quantity describes the whole element; a populated
    /// one means a single material layer. I3 relies on that distinction.
    /// </summary>
    [Fact]
    public void RawQuantityWithoutAMaterialDescribesTheWholeElement()
    {
        Assert.Null(new RawQuantity("Area", new Quantity(18.0, QuantityUnit.SquareMetre)).Material);
    }

    [Fact]
    public void RawQuantityWithAMaterialDescribesOneLayer()
    {
        RawQuantity layer = new(
            "Volume",
            new Quantity(0.6, QuantityUnit.CubicMetre),
            new MaterialRef("m-17", "Concrete, Cast-in-Place"));

        Assert.Equal("m-17", layer.Material!.MaterialId);
        Assert.Equal("Concrete, Cast-in-Place", layer.Material.MaterialName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankSourceKeyIsRejected(string? sourceKey)
    {
        Assert.Throws<ArgumentException>(
            () => new RawQuantity(sourceKey!, new Quantity(18.0, QuantityUnit.SquareMetre)));
    }

    [Fact]
    public void CodificationReadingsTolerateEveryCodeBeingAbsent()
    {
        Assert.Null(NoCodes.AssemblyCode);
        Assert.Null(NoCodes.Keynote);
        Assert.Empty(NoCodes.SharedParameters);
    }

    [Fact]
    public void CodificationReadingsCarryTheCodesThatWereRead()
    {
        CodificationReadings codes = new(
            assemblyCode: "C1010",
            keynote: "M-030",
            sharedParameters: new Dictionary<string, string?> { ["Partida"] = "01.02.03" });

        Assert.Equal("C1010", codes.AssemblyCode);
        Assert.Equal("M-030", codes.Keynote);
        Assert.Equal("01.02.03", codes.SharedParameters["Partida"]);
    }

    [Fact]
    public void NullSharedParametersAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new CodificationReadings(null, null, null!));
    }
}

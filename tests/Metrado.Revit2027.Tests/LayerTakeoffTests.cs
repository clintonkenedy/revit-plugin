using Metrado.Domain;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins the Revit-free half of material-layer extraction (task 3.1): what a
/// layered host's reading, in Revit's internal units, becomes on the seam.
/// Each material Revit measures crosses as one volume and one area entry,
/// the whole element's volume beside them, and the type's layers as a
/// structure. The converters double, halve and triple, so a value converted
/// with the wrong one, twice or not at all shows.
/// </summary>
public sealed class LayerTakeoffTests
{
    private static readonly SeamUnits Units = new(SquareMetres: sqft => sqft * 2, CubicMetres: cuft => cuft * 3, Metres: ft => ft / 2);

    private static readonly IReadOnlyDictionary<string, string?> NoShared = new Dictionary<string, string?>();

    private static MaterialReading Material(string id, double volume, double area, string? keynote = null) =>
        new(id, $"Material {id}", volume, area, keynote, NoShared);

    /// <summary>A stud wall: gypsum on both faces (one material, two layers), a membrane, and the studs.</summary>
    private static LayerReading Wall() => new(
        ComputedVolumeCubicFeet: 10.0,
        Layers:
        [
            new("Finish1", 0.05, "gypsum", false, false),
            new("Membrane", 0.0, "vapour", false, false),
            new("Structure", 0.3, "studs", false, false),
            new("Finish2", 0.05, "gypsum", false, false),
        ],
        Materials: [Material("gypsum", 2.0, 40.0, " 09 29 00.A1 "), Material("vapour", 0.0, 40.0), Material("studs", 8.0, 20.0)],
        Paint: [],
        Type: new TypeFacts(false, false, false, false));

    [Fact]
    public void EachMaterialCrossesAsOneVolumeAndOneAreaBesideTheWholeVolume()
    {
        (IReadOnlyList<RawQuantity> quantities, _) = LayerTakeoff.From(Wall(), Units);

        Assert.Equal(
            [("HOST_VOLUME_COMPUTED", null, 30.0, QuantityUnit.CubicMetre),
             ("MATERIAL_VOLUME", "gypsum", 6.0, QuantityUnit.CubicMetre), ("MATERIAL_AREA", "gypsum", 80.0, QuantityUnit.SquareMetre),
             ("MATERIAL_VOLUME", "vapour", 0.0, QuantityUnit.CubicMetre), ("MATERIAL_AREA", "vapour", 80.0, QuantityUnit.SquareMetre),
             ("MATERIAL_VOLUME", "studs", 24.0, QuantityUnit.CubicMetre), ("MATERIAL_AREA", "studs", 40.0, QuantityUnit.SquareMetre)],
            quantities.Select(q => (q.SourceKey, q.Material?.MaterialId, q.Amount.Value, q.Amount.Unit)));
    }

    /// <summary>A material is coded by its own Keynote, kept as read, and its shared parameter; never an Assembly Code.</summary>
    [Fact]
    public void AMaterialCarriesItsNameAndItsOwnCodes()
    {
        LayerReading reading = Wall() with
        {
            Materials = [new("gypsum", "Gypsum Wall Board", 2.0, 40.0, " 09 29 00.A1 ", new Dictionary<string, string?> { ["guid"] = "T-1" })],
        };

        // Both entries, the area above all: an m2 line, the default, is coded from it.
        IEnumerable<MaterialRef> materials = LayerTakeoff.From(reading, Units).Quantities.Where(q => q.Material is not null).Select(q => q.Material!);

        Assert.Equal(["MATERIAL_VOLUME", "MATERIAL_AREA"], LayerTakeoff.From(reading, Units).Quantities.Where(q => q.Material is not null).Select(q => q.SourceKey));
        Assert.All(materials, material =>
        {
            Assert.Equal(("gypsum", "Gypsum Wall Board"), (material.MaterialId, material.MaterialName));
            Assert.Equal((null, " 09 29 00.A1 ", "T-1"), (material.Codes.AssemblyCode, material.Codes.Keynote, material.Codes.SharedParameters["guid"]));
        });
    }

    /// <summary>Rounded to the nano-unit, as areas already are, so the feet round trip's noise decides nothing.</summary>
    [Fact]
    public void EveryQuantityIsRoundedToTheNanoUnit()
    {
        LayerReading reading = Wall() with { ComputedVolumeCubicFeet = 1.23456789012 / 3, Materials = [Material("studs", 1.23456789012 / 3, 1.0)] };

        (IReadOnlyList<RawQuantity> quantities, _) = LayerTakeoff.From(reading, Units);

        Assert.All(quantities.Where(q => q.Amount.Unit == QuantityUnit.CubicMetre), q => Assert.Equal(1.23456789, q.Amount.Value));
    }

    /// <summary>A value Revit could not give is left out, never read as zero: the element then fails its checks and is measured whole.</summary>
    [Fact]
    public void AValueRevitCouldNotGiveIsLeftOut()
    {
        LayerReading reading = Wall() with { ComputedVolumeCubicFeet = null, Materials = [Material("studs", double.NaN, 20.0)] };

        (IReadOnlyList<RawQuantity> quantities, _) = LayerTakeoff.From(reading, Units);

        Assert.Equal([("MATERIAL_AREA", 40.0)], quantities.Select(q => (q.SourceKey, q.Amount.Value)));
    }

    /// <summary>The type's layers in order, each with its function by Revit's name, its width in metres and its material.</summary>
    [Fact]
    public void TheLayersCrossInTheTypesOrder()
    {
        LayerStructure structure = LayerTakeoff.From(Wall(), Units).Structure!;

        Assert.Equal(
            [(0, LayerFunction.Finish1, 0.025, "gypsum"), (1, LayerFunction.Membrane, 0.0, "vapour"), (2, LayerFunction.Structure, 0.15, "studs"), (3, LayerFunction.Finish2, 0.025, "gypsum")],
            structure.Layers.Select(layer => (layer.Position, layer.Function!.Value, layer.Width.Value, layer.MaterialId!)));
        Assert.Empty(structure.Conditions);
    }

    [Theory]
    [InlineData("StructuralDeck", LayerFunction.StructuralDeck)]
    [InlineData("Substrate", LayerFunction.Substrate)]
    [InlineData("Insulation", LayerFunction.Insulation)]
    [InlineData("None", null)]
    [InlineData("Bogus", null)]
    [InlineData("1", null)]
    public void AFunctionIsKnownByItsNameOrNotAtAll(string name, LayerFunction? function)
    {
        LayerReading reading = Wall() with { Layers = [new(name, 0.1, "studs", false, false)] };

        Assert.Equal(function, Assert.Single(LayerTakeoff.From(reading, Units).Structure!.Layers).Function);
    }

    /// <summary>A layer Revit gives the category's material says so; one with no material at all has none.</summary>
    [Fact]
    public void ALayersMaterialIsItsOwnTheCategorysOrNone()
    {
        LayerReading reading = Wall() with { Layers = [new("Structure", 0.1, "default-wall", true, false), new("Finish1", 0.1, null, false, false)] };

        IReadOnlyList<CompoundLayer> layers = LayerTakeoff.From(reading, Units).Structure!.Layers;

        Assert.Equal([("default-wall", true), (null, false)], layers.Select(layer => (layer.MaterialId, layer.MaterialFromCategory)));
    }

    public static TheoryData<TypeFacts, string?, AddBackCondition[]> Conditions() => new()
    {
        { new TypeFacts(true, false, false, false), null, [AddBackCondition.WrapsAtInserts] },
        { new TypeFacts(false, true, false, false), null, [AddBackCondition.VerticallyCompound] },
        { new TypeFacts(false, false, true, false), null, [AddBackCondition.VariableLayer] },
        { new TypeFacts(false, false, false, true), null, [AddBackCondition.ShapeEdited] },
        { new TypeFacts(false, false, false, false), "StructuralDeck", [AddBackCondition.StructuralDeck] },
        { new TypeFacts(true, true, true, true), "StructuralDeck", [AddBackCondition.WrapsAtInserts, AddBackCondition.VerticallyCompound, AddBackCondition.VariableLayer, AddBackCondition.ShapeEdited, AddBackCondition.StructuralDeck] },
    };

    [Theory]
    [MemberData(nameof(Conditions))]
    public void EachTypeFactBecomesItsCondition(TypeFacts type, string? deck, AddBackCondition[] conditions)
    {
        LayerReading reading = Wall() with { Type = type, Layers = [.. Wall().Layers, .. deck is null ? [] : new LayerFacts[] { new(deck, 0.1, "studs", false, false) }] };

        Assert.Equal(conditions, LayerTakeoff.From(reading, Units).Structure!.Conditions);
    }

    /// <summary>A type with no layers, or one whose width Revit could not give, has no structure: never an invented one.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LayersThatCannotBeReadGiveNoStructure(bool unreadableWidth)
    {
        LayerReading reading = Wall() with { Layers = unreadableWidth ? [new("Structure", double.NaN, "studs", false, false)] : [] };

        Assert.Null(LayerTakeoff.From(reading, Units).Structure);
    }

    /// <summary>Paint is not taken off (user decision, PR 27): each painted host names every paint and its area, to price by hand.</summary>
    [Fact]
    public void APaintedHostIsWarnedAboutOnceNamingEachPaint()
    {
        ElementTakeoff wall = new("w-1", "Walls", "Basic Wall", "Stud", "t-1", new CodificationReadings(null, null, NoShared), [], []);
        LayerReading reading = Wall() with { Paint = [new("Paint - Blue", 5.0), new("Paint - Red", 0.25)] };

        ValidationWarning warning = Assert.Single(LayerTakeoff.Paint(wall, reading, Units.SquareMetres));

        Assert.Equal("w-1", warning.UniqueId);
        Assert.Contains("Paint - Blue over 10 m2", warning.Condition, StringComparison.Ordinal);
        Assert.Contains("Paint - Red over 0.5 m2", warning.Condition, StringComparison.Ordinal);
        Assert.Contains("price it by hand", warning.Condition, StringComparison.Ordinal);
        Assert.Empty(LayerTakeoff.Paint(wall, Wall(), Units.SquareMetres));
    }

    /// <summary>A layered host keeps everything a whole one crosses with, and gains its layers.</summary>
    [Fact]
    public void ALayeredHostKeepsItsWholeReadingAndGainsItsLayers()
    {
        HostReading host = new("w-1", "Walls", "Basic Wall", "Stud", "t-1", "C1010", 50.0, [new OpeningReading("door", 21.0)], [], Layers: Wall());

        ElementTakeoff takeoff = HostTakeoff.From(host, Units);

        Assert.Equal(("HOST_AREA_COMPUTED", 100.0), (takeoff.Quantities[0].SourceKey, takeoff.Quantities[0].Amount.Value));
        Assert.Equal(7, takeoff.Quantities.Count - 1);
        Assert.Equal(42.0, Assert.Single(takeoff.Openings).Amount.Value);
        Assert.Equal(4, takeoff.Layers!.Layers.Count);
    }

    /// <summary>A host not layered crosses exactly as before I3, whichever overload maps it.</summary>
    [Fact]
    public void AHostNotLayeredCrossesAsBefore()
    {
        HostReading host = new("w-1", "Walls", "Basic Wall", "Stud", "t-1", "C1010", 50.0, [new OpeningReading("door", 21.0)], []);

        ElementTakeoff viaUnits = HostTakeoff.From(host, Units);
        ElementTakeoff viaArea = HostTakeoff.From(host, Units.SquareMetres);

        Assert.Null(viaUnits.Layers);
        Assert.Equal(viaArea.Quantities, viaUnits.Quantities);
        Assert.Equal(viaArea.Openings.Select(o => o.Amount), viaUnits.Openings.Select(o => o.Amount));
        Assert.Throws<ArgumentException>(() => HostTakeoff.From(host with { Layers = Wall() }, Units.SquareMetres));
    }
}

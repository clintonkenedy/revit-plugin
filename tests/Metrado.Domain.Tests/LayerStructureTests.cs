namespace Metrado.Domain.Tests;

/// <summary>
/// Pins how a layered element's structure and its materials' codes cross the
/// Revit seam (task 3.1): what a compound layer and a layer structure may
/// hold, and that everything measured before I3 crosses unchanged.
/// </summary>
public sealed class LayerStructureTests
{
    private static Quantity Metres(double value) => new(value, QuantityUnit.Metre);

    private static CompoundLayer Layer(int position, double width = 0.1, string? material = "m-1", LayerFunction? function = LayerFunction.Structure) =>
        new(position, function, Metres(width), material);

    /// <summary>The names are Revit's material takeoff fields and the built-in volume, which criteria and tests quote.</summary>
    [Fact]
    public void TheLayerSourcesAreNamedAsRevitNamesThem()
    {
        Assert.Equal(
            ["MATERIAL_VOLUME", "MATERIAL_AREA", "HOST_VOLUME_COMPUTED"],
            new[] { LayerSources.MaterialVolume, LayerSources.MaterialArea, LayerSources.HostVolume });
    }

    public static TheoryData<string, Func<CompoundLayer>> BadLayers() => new()
    {
        { "a width in m2", () => new CompoundLayer(0, LayerFunction.Finish1, new Quantity(0.1, QuantityUnit.SquareMetre), "m-1") },
        { "a width that is not a number", () => Layer(0, double.NaN) },
        { "an infinite width", () => Layer(0, double.PositiveInfinity) },
        { "a negative width", () => Layer(0, -0.01) },
        { "a negative position", () => Layer(-1) },
        { "an undeclared function", () => Layer(0, function: (LayerFunction)99) },
        { "a blank material", () => Layer(0, material: " ") },
        { "the category's material with no material", () => new CompoundLayer(0, LayerFunction.Structure, Metres(0.1), null, materialFromCategory: true) },
    };

    [Theory]
    [MemberData(nameof(BadLayers))]
    public void ALayerThatCannotBeTrueIsRefused(string what, Func<CompoundLayer> build)
    {
        Assert.Throws<ArgumentException>(build);
        Assert.NotEmpty(what);
    }

    /// <summary>A membrane has no thickness, and a layer Revit gives no material or function is still a layer.</summary>
    [Fact]
    public void AMembraneAndAnUnassignedLayerAreLayers()
    {
        CompoundLayer membrane = Layer(1, 0.0, function: LayerFunction.Membrane);
        CompoundLayer unassigned = Layer(2, 0.05, material: null, function: null);

        Assert.Equal(0.0, membrane.Width.Value);
        Assert.Null(unassigned.MaterialId);
        Assert.Null(unassigned.Function);
        Assert.False(unassigned.MaterialFromCategory);
    }

    [Fact]
    public void AStructureAddsUpItsLayersWidths()
    {
        LayerStructure structure = new([Layer(0, 0.0125), Layer(1, 0.09), Layer(2, 0.0125)], []);

        Assert.Equal(QuantityUnit.Metre, structure.Thickness.Unit);
        Assert.Equal(0.115, structure.Thickness.Value, 12);
    }

    public static TheoryData<string, Func<LayerStructure>> BadStructures() => new()
    {
        { "no layers", () => new LayerStructure([], []) },
        { "layers out of order", () => new LayerStructure([Layer(1), Layer(0)], []) },
        { "a position twice", () => new LayerStructure([Layer(0), Layer(0)], []) },
        { "a condition twice", () => new LayerStructure([Layer(0)], [AddBackCondition.WrapsAtInserts, AddBackCondition.WrapsAtInserts]) },
        { "an undeclared condition", () => new LayerStructure([Layer(0)], [(AddBackCondition)99]) },
    };

    [Theory]
    [MemberData(nameof(BadStructures))]
    public void AStructureThatCannotBeTrueIsRefused(string what, Func<LayerStructure> build)
    {
        Assert.Throws<ArgumentException>(build);
        Assert.NotEmpty(what);
    }

    /// <summary>Every material reference made before I3 still means what it meant: a material with no codes read.</summary>
    [Fact]
    public void AMaterialReferenceWithoutCodesCarriesNone()
    {
        MaterialRef material = new("mat-uid", "Gypsum Wall Board");

        Assert.Null(material.Codes.AssemblyCode);
        Assert.Null(material.Codes.Keynote);
        Assert.Empty(material.Codes.SharedParameters);
    }

    /// <summary>A material is coded by its own Keynote and shared parameter; an Assembly Code is a type's, never a material's.</summary>
    [Fact]
    public void AMaterialReferenceCarriesItsOwnCodes()
    {
        CodificationReadings codes = new(null, "09 29 00.A1", new Dictionary<string, string?> { ["guid"] = "T-1" });

        MaterialRef material = new("mat-uid", "Gypsum Wall Board", codes);

        Assert.Same(codes, material.Codes);
    }

    /// <summary>An element read before I3, or in a category not layered, has no layers: not an empty structure.</summary>
    [Fact]
    public void AnElementHasNoLayersUnlessTheyWereRead()
    {
        ElementTakeoff wall = new("w-1", "Walls", "Basic Wall", "Generic", "t-1", new CodificationReadings(null, null, new Dictionary<string, string?>()), [], []);
        LayerStructure structure = new([Layer(0)], []);

        Assert.Null(wall.Layers);
        Assert.Same(structure, (wall with { Layers = structure }).Layers);
        Assert.Equal(wall, wall with { });
    }
}

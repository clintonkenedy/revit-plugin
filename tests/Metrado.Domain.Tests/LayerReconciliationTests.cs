namespace Metrado.Domain.Tests;

/// <summary>
/// Pins when a layered element may be measured by its layers (task 3.2):
/// only when Revit's materials account for all of it — every layer, a
/// membrane included, attributed to a material Revit measures, every such
/// material to a layer, each read once and in its unit, and their volumes
/// adding up to the whole to a cubic centimetre. Otherwise the element is
/// measured whole, with the fault named: a layer line never makes up for a
/// missing layer, and a partly measured element never reaches the budget.
/// </summary>
public sealed class LayerReconciliationTests
{
    private static readonly CodificationReadings NoCodes = new(null, null, new Dictionary<string, string?>());

    private static CompoundLayer Layer(int position, LayerFunction? function, double width, string? material) =>
        new(position, function, new Quantity(width, QuantityUnit.Metre), material);

    private static RawQuantity Volume(string material, double value, QuantityUnit unit = QuantityUnit.CubicMetre) =>
        new(LayerSources.MaterialVolume, new Quantity(value, unit), new MaterialRef(material, material));

    private static RawQuantity Area(string material, double value) =>
        new(LayerSources.MaterialArea, new Quantity(value, QuantityUnit.SquareMetre), new MaterialRef(material, material));

    private static RawQuantity Whole(double value, QuantityUnit unit = QuantityUnit.CubicMetre) =>
        new(LayerSources.HostVolume, new Quantity(value, unit));

    /// <summary>A three-material wall: 10 mm tile, 130 mm brick, 15 mm plaster; 1.93905 m3 in all.</summary>
    private static readonly CompoundLayer[] WallLayers =
        [Layer(0, LayerFunction.Finish1, 0.010, "tile"), Layer(1, LayerFunction.Structure, 0.130, "brick"), Layer(2, LayerFunction.Finish2, 0.015, "plaster")];

    private static IReadOnlyList<RawQuantity> WallQuantities() =>
    [
        new RawQuantity("HOST_AREA_COMPUTED", new Quantity(12.51, QuantityUnit.SquareMetre)),
        Whole(1.93905),
        Volume("tile", 0.12510), Area("tile", 12.51),
        Volume("brick", 1.62630), Area("brick", 12.51),
        Volume("plaster", 0.18765), Area("plaster", 12.51),
    ];

    private static ElementTakeoff Wall(IReadOnlyList<RawQuantity>? quantities = null, IReadOnlyList<CompoundLayer>? layers = null, bool noLayers = false) =>
        new("w-1", "Walls", "Basic Wall", "Tiled brick", "t-1", NoCodes, quantities ?? WallQuantities(), [],
            noLayers ? null : new LayerStructure(layers ?? WallLayers, []));

    [Fact]
    public void AWallWhoseMaterialsAccountForAllOfItReconciles()
    {
        LayerReconciliation reconciliation = LayerMeasurement.Reconcile(Wall());

        Assert.True(reconciliation.Reconciles);
        Assert.Null(reconciliation.Fault);
        Assert.Equal(new Quantity(1.93905, QuantityUnit.CubicMetre), reconciliation.Whole);
        Assert.Equal(1.93905, reconciliation.Sum, 12);
        Assert.Equal(3, reconciliation.Terms);
    }

    /// <summary>A membrane Revit measures has no volume but its area: it reconciles, and is priced by its line.</summary>
    [Fact]
    public void AMembraneRevitMeasuresReconciles()
    {
        ElementTakeoff wall = Wall([.. WallQuantities(), Volume("vapour", 0.0), Area("vapour", 12.51)], [.. WallLayers, Layer(3, LayerFunction.Membrane, 0.0, "vapour")]);

        Assert.True(LayerMeasurement.Reconcile(wall).Reconciles);
    }

    public static TheoryData<string, Func<ElementTakeoff>, LayerFault> Faults() => new()
    {
        { "no layers read", () => Wall(noLayers: true), LayerFault.NoLayers },
        { "no whole volume", () => Wall([.. WallQuantities().Where(q => q.SourceKey != LayerSources.HostVolume)]), LayerFault.NoWholeVolume },
        { "a whole volume in m2", () => Wall([.. WallQuantities().Select(q => q.SourceKey == LayerSources.HostVolume ? Whole(1.93905, QuantityUnit.SquareMetre) : q)]), LayerFault.UnitMismatch },
        { "a material volume in m2", () => Wall([.. WallQuantities().Select(q => q.SourceKey == LayerSources.MaterialVolume && q.Material!.MaterialId == "tile" ? Volume("tile", 0.1251, QuantityUnit.SquareMetre) : q)]), LayerFault.UnitMismatch },
        { "a material read twice", () => Wall([.. WallQuantities(), Volume("tile", 0.0)]), LayerFault.DuplicateMaterial },
        { "a material's area read twice", () => Wall([.. WallQuantities(), Area("tile", 1.0)]), LayerFault.DuplicateMaterial },
        { "a material area in m3", () => Wall([.. WallQuantities().Select(q => q.SourceKey == LayerSources.MaterialArea && q.Material!.MaterialId == "tile" ? new RawQuantity(LayerSources.MaterialArea, new Quantity(12.51, QuantityUnit.CubicMetre), q.Material) : q)]), LayerFault.UnitMismatch },
        { "a negative volume", () => Wall([.. WallQuantities().Select(q => q.SourceKey == LayerSources.MaterialVolume && q.Material!.MaterialId == "tile" ? Volume("tile", -0.1) : q)]), LayerFault.NonFiniteOrNegative },
        { "a volume that is not a number", () => Wall([.. WallQuantities().Select(q => q.SourceKey == LayerSources.HostVolume ? Whole(double.NaN) : q)]), LayerFault.NonFiniteOrNegative },
        { "a material with no area", () => Wall([.. WallQuantities().Where(q => !(q.SourceKey == LayerSources.MaterialArea && q.Material!.MaterialId == "tile"))]), LayerFault.NonFiniteOrNegative },
        { "a material with no volume", () => Wall([.. WallQuantities().Where(q => !(q.SourceKey == LayerSources.MaterialVolume && q.Material!.MaterialId == "tile")), Whole(1.81395)]), LayerFault.NonFiniteOrNegative },
        { "a negative area", () => Wall([.. WallQuantities().Select(q => q.SourceKey == LayerSources.MaterialArea && q.Material!.MaterialId == "tile" ? Area("tile", -1.0) : q)]), LayerFault.NonFiniteOrNegative },
        { "a layer Revit does not measure", () => Wall(layers: [.. WallLayers, Layer(3, LayerFunction.Insulation, 0.05, "wool")]), LayerFault.UnattributedLayer },
        { "a layer with no material", () => Wall(layers: [.. WallLayers, Layer(3, LayerFunction.Insulation, 0.05, null)]), LayerFault.UnattributedLayer },
        { "a membrane with no material", () => Wall(layers: [.. WallLayers, Layer(3, LayerFunction.Membrane, 0.0, null)]), LayerFault.UnattributedLayer },
        { "a membrane Revit does not measure", () => Wall(layers: [.. WallLayers, Layer(3, LayerFunction.Membrane, 0.0, "vapour")]), LayerFault.UnattributedLayer },
        { "a material on no layer", () => Wall([.. WallQuantities(), Volume("paint", 0.0), Area("paint", 1.0)]), LayerFault.UnattributedMaterial },
        { "a layer with no function", () => Wall(layers: [WallLayers[0], Layer(1, null, 0.130, "brick"), WallLayers[2]]), LayerFault.NoFunction },
        { "volumes that miss the whole", () => Wall([.. WallQuantities().Select(q => q.SourceKey == LayerSources.HostVolume ? Whole(1.94) : q)]), LayerFault.OutOfTolerance },
    };

    [Theory]
    [MemberData(nameof(Faults))]
    public void AnElementItsMaterialsDoNotAccountForIsNotMeasuredByLayer(string what, Func<ElementTakeoff> build, LayerFault fault)
    {
        LayerReconciliation reconciliation = LayerMeasurement.Reconcile(build());

        Assert.False(reconciliation.Reconciles, what);
        Assert.Equal(fault, reconciliation.Fault);
    }

    /// <summary>A fault about one material names it as Revit's UI does, never by its UniqueId.</summary>
    [Theory]
    [InlineData("a material read twice", "material Tile, enchape was read twice")]
    [InlineData("a material with no area", "a volume or area of Tile, enchape could not be read")]
    [InlineData("a material on no layer", "material Paint, latex, which Revit measures in the element, is on none of its layers")]
    public void AFaultNamesItsMaterialByName(string what, string phrase)
    {
        List<RawQuantity> quantities = what switch
        {
            "a material read twice" => [.. WallQuantities(), Volume("tile", 0.0)],
            "a material with no area" => [.. WallQuantities().Where(q => !(q.SourceKey == LayerSources.MaterialArea && q.Material!.MaterialId == "tile"))],
            _ => [.. WallQuantities(), Volume("paint", 0.0), Area("paint", 1.0)],
        };
        Dictionary<string, string> names = new() { ["tile"] = "Tile, enchape", ["brick"] = "Brick", ["plaster"] = "Plaster", ["paint"] = "Paint, latex" };
        ElementTakeoff wall = Wall([.. quantities.Select(q => q.Material is MaterialRef m ? new RawQuantity(q.SourceKey, q.Amount, new MaterialRef(m.MaterialId, names[m.MaterialId])) : q)]);

        string warning = LayerMeasurement.Reconcile(wall).WarningFor(wall)!.Condition;

        Assert.Contains(phrase, warning, StringComparison.Ordinal);
    }

    /// <summary>A cubic centimetre, fixed: the seam's rounding is a thousandth of that, the smallest real material three thousand times it.</summary>
    [Theory]
    [InlineData(1.93905 + 0.9e-6, true)]
    [InlineData(1.93905 - 0.9e-6, true)]
    [InlineData(1.93905 + 1.1e-6, false)]
    [InlineData(1.93905 - 1.1e-6, false)]
    public void TheToleranceIsACubicCentimetre(double whole, bool reconciles)
    {
        ElementTakeoff wall = Wall([.. WallQuantities().Select(q => q.SourceKey == LayerSources.HostVolume ? Whole(whole) : q)]);

        Assert.Equal(reconciles, LayerMeasurement.Reconcile(wall).Reconciles);
    }

    /// <summary>The warning names the element, the fault in words and that it is measured whole.</summary>
    [Fact]
    public void AFaultIsReportedAsTheElementMeasuredWhole()
    {
        ElementTakeoff wall = Wall([.. WallQuantities().Select(q => q.SourceKey == LayerSources.HostVolume ? Whole(1.94) : q)]);

        ValidationWarning warning = LayerMeasurement.Reconcile(wall).WarningFor(wall)!;

        Assert.Equal("w-1", warning.UniqueId);
        Assert.Contains("1.94", warning.Condition, StringComparison.Ordinal);
        Assert.Contains("1.93905", warning.Condition, StringComparison.Ordinal);
        Assert.Contains("measured whole", warning.Condition, StringComparison.Ordinal);
        Assert.Null(LayerMeasurement.Reconcile(Wall()).WarningFor(Wall()));
    }
}

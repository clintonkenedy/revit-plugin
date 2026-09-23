namespace Metrado.Domain.Tests;

/// <summary>
/// Pins how a category's material-layer takeoff is stated (task 3.2, the
/// user's choice in PR 27): each layer function has a unit, m2 by default
/// and m3 where asked; a membrane has no thickness, so it is always m2; and
/// only a category measured by area from a source can be taken off by layer.
/// </summary>
public sealed class LayerCriteriaTests
{
    private static readonly LayerFunction[] Functions =
        [LayerFunction.Structure, LayerFunction.Substrate, LayerFunction.Insulation, LayerFunction.Finish1, LayerFunction.Finish2, LayerFunction.Membrane, LayerFunction.StructuralDeck];

    [Fact]
    public void ByDefaultEveryFunctionIsInSquareMetres()
    {
        Assert.All(Functions, function => Assert.Equal(QuantityUnit.SquareMetre, LayerCriterion.Default.UnitOf(function)));
    }

    [Theory]
    [InlineData(LayerFunction.Structure)]
    [InlineData(LayerFunction.Substrate)]
    [InlineData(LayerFunction.Insulation)]
    [InlineData(LayerFunction.Finish1)]
    [InlineData(LayerFunction.Finish2)]
    [InlineData(LayerFunction.StructuralDeck)]
    public void AFunctionStatedInCubicMetresIsTheOnlyOneThatChanges(LayerFunction stated)
    {
        LayerCriterion layers = LayerCriterion.TryCreate(new Dictionary<LayerFunction, QuantityUnit> { [stated] = QuantityUnit.CubicMetre }, "Floors").Value;

        Assert.Equal(QuantityUnit.CubicMetre, layers.UnitOf(stated));
        Assert.All(Functions.Where(function => function != stated), function => Assert.Equal(QuantityUnit.SquareMetre, layers.UnitOf(function)));
        Assert.NotEqual(LayerCriterion.Default, layers);
        Assert.Equal(LayerCriterion.Default, LayerCriterion.TryCreate(new Dictionary<LayerFunction, QuantityUnit>(), "Floors").Value);
    }

    public static TheoryData<LayerFunction, QuantityUnit, string> Refused() => new()
    {
        { LayerFunction.Membrane, QuantityUnit.CubicMetre, "Membrane" },
        { LayerFunction.Finish1, QuantityUnit.Each, "m2 or m3" },
        { LayerFunction.Finish1, QuantityUnit.Metre, "m2 or m3" },
        { (LayerFunction)99, QuantityUnit.SquareMetre, "99" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void AUnitNoLayerCanHaveIsRefused(LayerFunction function, QuantityUnit unit, string phrase)
    {
        Result<LayerCriterion, ConfigError> layers = LayerCriterion.TryCreate(new Dictionary<LayerFunction, QuantityUnit> { [function] = unit }, "Walls");

        Assert.False(layers.IsOk);
        Assert.Contains(phrase, layers.Error.Message, StringComparison.Ordinal);
        Assert.Equal("Walls", layers.Error.Category);
    }

    [Fact]
    public void AnUndeclaredFunctionHasNoUnit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LayerCriterion.Default.UnitOf((LayerFunction)99));
    }

    /// <summary>Its openings and threshold are areas, so only an area category measured from a source can be layered.</summary>
    [Fact]
    public void OnlyAnAreaCategoryWithASourceCanBeLayered()
    {
        CategoryCriterion walls = CriteriaSet.Default.ByCategory["Walls"];
        CategoryCriterion doors = CriteriaSet.Default.ByCategory["Doors"];

        Assert.Same(LayerCriterion.Default, new CategoryCriterion("Walls", walls.Unit, walls.Sources, walls.Threshold, LayerCriterion.Default).Layers);
        Assert.Throws<ArgumentException>(() => new CategoryCriterion("Doors", doors.Unit, doors.Sources, doors.Threshold, LayerCriterion.Default));
        Assert.Throws<ArgumentException>(() => new CategoryCriterion("Railings", walls.Unit, walls.Sources, walls.Threshold, LayerCriterion.Default));
        Assert.Throws<ArgumentException>(() => new CategoryCriterion("Walls", QuantityUnit.CubicMetre, walls.Sources,
            OpeningsThreshold.TryCreate(1, QuantityUnit.CubicMetre, BoundaryMode.Exclusive, "Walls").Value, LayerCriterion.Default));
    }

    /// <summary>
    /// Layer lines are corrected without the whole path's unit check, so a
    /// layered criterion's threshold must be an area: one in m3, or a default
    /// threshold with no unit and no mode, would be added into m2 lines.
    /// </summary>
    [Fact]
    public void ALayeredCriterionsThresholdIsAnArea()
    {
        CategoryCriterion walls = CriteriaSet.Default.ByCategory["Walls"];

        Assert.Throws<ArgumentException>(() => new CategoryCriterion("Walls", walls.Unit, walls.Sources,
            OpeningsThreshold.TryCreate(1, QuantityUnit.CubicMetre, BoundaryMode.Exclusive, "Walls").Value, LayerCriterion.Default));
        Assert.Throws<ArgumentException>(() => new CategoryCriterion("Walls", walls.Unit, walls.Sources, default, LayerCriterion.Default));
        Assert.Null(new CategoryCriterion("Walls", walls.Unit, walls.Sources, default).Layers);
    }

    /// <summary>A unit Metrado does not declare is refused, never thrown on while describing it.</summary>
    [Fact]
    public void AnUndeclaredUnitIsRefused()
    {
        Result<LayerCriterion, ConfigError> layers = LayerCriterion.TryCreate(new Dictionary<LayerFunction, QuantityUnit> { [LayerFunction.Structure] = (QuantityUnit)99 }, "Walls");

        Assert.False(layers.IsOk);
        Assert.Contains("not an undeclared unit", layers.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoBuiltInCategoryIsLayered()
    {
        Assert.All(CriteriaSet.Default.ByCategory.Values, criterion => Assert.Null(criterion.Layers));
    }

    [Fact]
    public void AFileTurnsLayersOnOrOffPerCategory()
    {
        CriteriaSet on = Merge(CriteriaSet.Default, new CategoryOverride("Walls", layers: LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit>())));
        CriteriaSet cubic = Merge(CriteriaSet.Default, new CategoryOverride("Floors", layers: LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit> { [LayerFunction.Structure] = QuantityUnit.CubicMetre })));
        CriteriaSet off = Merge(on, new CategoryOverride("Walls", layers: LayerOverride.Off));

        Assert.Equal(LayerCriterion.Default, on.ByCategory["Walls"].Layers);
        Assert.Null(on.ByCategory["Floors"].Layers);
        Assert.Equal(QuantityUnit.CubicMetre, cubic.ByCategory["Floors"].Layers!.UnitOf(LayerFunction.Structure));
        Assert.Null(off.ByCategory["Walls"].Layers);
    }

    /// <summary>An entry that says nothing of layers keeps what it inherits, layered or not.</summary>
    [Fact]
    public void AnEntryNotMentioningLayersInheritsThem()
    {
        CriteriaSet layered = Merge(CriteriaSet.Default, new CategoryOverride("Walls", layers: LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit>())));

        CriteriaSet rethresholded = Merge(layered, new CategoryOverride("Walls", threshold: 0.5));

        Assert.Equal(LayerCriterion.Default, rethresholded.ByCategory["Walls"].Layers);
    }

    public static TheoryData<CategoryOverride, string> RefusedLayered() => new()
    {
        { new CategoryOverride("Doors", layers: LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit>())), "'Doors' cannot be taken off by material layer: only walls, floors and roofs have layers." },
        { new CategoryOverride("Railings", layers: LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit>())), "'Railings' cannot be taken off by material layer: only walls, floors and roofs have layers." },
        { new CategoryOverride("Railings", QuantityUnit.SquareMetre, layers: LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit>())), "'Railings' cannot be taken off by material layer: only walls, floors and roofs have layers." },
        { new CategoryOverride("Walls", QuantityUnit.CubicMetre, ["HOST_VOLUME_COMPUTED"], layers: LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit>())), "'Walls' cannot be taken off by material layer: a layered category is measured in m2" },
        { new CategoryOverride("Walls", layers: LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit> { [LayerFunction.Membrane] = QuantityUnit.CubicMetre })), "Membrane" },
    };

    [Theory]
    [MemberData(nameof(RefusedLayered))]
    public void LayersACategoryCannotHaveAreRefused(CategoryOverride entry, string phrase)
    {
        Result<CriteriaSet, ConfigError> merged = CriteriaSet.Merge(CriteriaSet.Default, [entry]);

        Assert.False(merged.IsOk);
        Assert.Contains(phrase, merged.Error.Message, StringComparison.Ordinal);

        // What the file wrote is true or an object, never a value to quote back.
        Assert.Null(merged.Error.InvalidValue);
    }

    [Fact]
    public void LayerOverridesMatchTheirState()
    {
        Dictionary<LayerFunction, QuantityUnit> units = new() { [LayerFunction.Structure] = QuantityUnit.CubicMetre };

        Assert.Equal("off", LayerOverride.Off.Match(() => "off", _ => "on"));
        Assert.Same(units, LayerOverride.On(units).Match<IReadOnlyDictionary<LayerFunction, QuantityUnit>?>(() => null, stated => stated));
    }

    private static CriteriaSet Merge(CriteriaSet baseline, CategoryOverride entry)
    {
        Result<CriteriaSet, ConfigError> merged = CriteriaSet.Merge(baseline, [entry]);
        Assert.True(merged.IsOk, merged.IsOk ? string.Empty : merged.Error.Message);
        return merged.Value;
    }
}

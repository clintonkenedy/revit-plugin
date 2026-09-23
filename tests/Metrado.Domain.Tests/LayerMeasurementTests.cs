namespace Metrado.Domain.Tests;

/// <summary>
/// Pins how a layered element is measured by its materials (task 3.2):
/// one line per material Revit measures, exterior (or top) first, in its
/// layers' unit. An opening is decided once, at the host's threshold; one
/// added back returns to each m2 line its area times the material's layer
/// count, the share PR 28 found exact against deleting each opening; an m3
/// line, and every line under a condition, keeps Revit's deduction, with a
/// warning saying how much.
/// </summary>
/// <remarks>
/// The worked example: a 5.00 x 3.00 m wall, a 0.60 m2 window (added back
/// under the 1.0 m2 exclusive threshold) and a 1.89 m2 door (kept
/// deducted); 10 mm tile, 130 mm brick, 15 mm plaster; 12.51 m2 of each.
/// </remarks>
public sealed class LayerMeasurementTests
{
    private static readonly CodificationReadings NoCodes = new(null, null, new Dictionary<string, string?>());

    private static readonly OpeningsThreshold Exclusive = OpeningsThreshold.TryCreate(1.0, QuantityUnit.SquareMetre, BoundaryMode.Exclusive, "Walls").Value;

    private static CompoundLayer Layer(int position, LayerFunction function, double width, string material) =>
        new(position, function, new Quantity(width, QuantityUnit.Metre), material);

    private static IEnumerable<RawQuantity> Material(string id, double volume, double area) =>
    [
        new(LayerSources.MaterialVolume, new Quantity(volume, QuantityUnit.CubicMetre), new MaterialRef(id, $"Material {id}")),
        new(LayerSources.MaterialArea, new Quantity(area, QuantityUnit.SquareMetre), new MaterialRef(id, $"Material {id}")),
    ];

    private static ElementTakeoff Wall(
        IReadOnlyList<CompoundLayer>? layers = null,
        IEnumerable<RawQuantity>? materials = null,
        IReadOnlyList<AddBackCondition>? conditions = null,
        IReadOnlyList<OpeningQuantity>? openings = null)
    {
        List<RawQuantity> quantities = materials?.ToList() ?? [.. Material("plaster", 0.18765, 12.51), .. Material("tile", 0.12510, 12.51), .. Material("brick", 1.62630, 12.51)];
        double volume = quantities.Where(q => q.SourceKey == LayerSources.MaterialVolume).Sum(q => q.Amount.Value);
        return new ElementTakeoff(
            "w-1", "Walls", "Basic Wall", "Tiled brick", "t-1", NoCodes,
            [new RawQuantity("HOST_AREA_COMPUTED", new Quantity(12.51, QuantityUnit.SquareMetre)), new RawQuantity(LayerSources.HostVolume, new Quantity(volume, QuantityUnit.CubicMetre)), .. quantities],
            openings ?? [new OpeningQuantity("window", new Quantity(0.60, QuantityUnit.SquareMetre)), new OpeningQuantity("door", new Quantity(1.89, QuantityUnit.SquareMetre))],
            new LayerStructure(layers ?? [Layer(0, LayerFunction.Finish1, 0.010, "tile"), Layer(1, LayerFunction.Structure, 0.130, "brick"), Layer(2, LayerFunction.Finish2, 0.015, "plaster")], conditions ?? []));
    }

    private static CategoryCriterion Walls(LayerCriterion? layers = null, OpeningsThreshold? threshold = null) =>
        new("Walls", QuantityUnit.SquareMetre, ["HOST_AREA_COMPUTED"], threshold ?? Exclusive, layers ?? LayerCriterion.Default);

    private static (IReadOnlyList<LayerLine> Lines, IReadOnlyList<ValidationWarning> Warnings) ByLayer(ElementTakeoff wall, CategoryCriterion? criterion = null) =>
        LayerMeasurement.Measure(wall, criterion ?? Walls()).Match(
            byLayer: (lines, warnings) => (lines, warnings),
            whole: why => throw new Xunit.Sdk.XunitException($"Measured whole: {why.Condition}"));

    [Fact]
    public void EachMaterialIsOneLineExteriorFirstWithTheWindowAddedBack()
    {
        (IReadOnlyList<LayerLine> lines, IReadOnlyList<ValidationWarning> warnings) = ByLayer(Wall());

        Assert.Equal(
            [("tile", 13.11, 12.51, 15.00), ("brick", 13.11, 12.51, 15.00), ("plaster", 13.11, 12.51, 15.00)],
            lines.Select(line => (line.Layer.Material.MaterialId, Math.Round(line.Metrado.Metrado.Value, 9), line.Metrado.Raw.Value, Math.Round(line.Metrado.Gross.Value, 9))));
        Assert.All(lines, line => Assert.Equal(QuantityUnit.SquareMetre, line.Metrado.Metrado.Unit));
        Assert.Empty(warnings);
    }

    /// <summary>Revit merges a material on two layers into one; it is one line, getting the opening back once per layer.</summary>
    [Fact]
    public void AMaterialOnTwoLayersIsOneLineGettingEachOpeningBackPerLayer()
    {
        ElementTakeoff wall = Wall(
            layers: [Layer(0, LayerFunction.Finish1, 0.015, "plaster"), Layer(1, LayerFunction.Structure, 0.130, "brick"), Layer(2, LayerFunction.Finish2, 0.015, "plaster")],
            materials: [.. Material("plaster", 0.37530, 25.02), .. Material("brick", 1.62630, 12.51)]);

        LayerLine plaster = ByLayer(wall).Lines[0];

        Assert.Equal(("plaster", 2, 0), (plaster.Layer.Material.MaterialId, plaster.Layer.LayerCount, plaster.Layer.FirstPosition));
        Assert.Equal(26.22, plaster.Metrado.Metrado.Value, 9);
        Assert.Equal(0.030, plaster.Layer.Width.Value, 9);
    }

    [Fact]
    public void AMembraneIsInSquareMetres()
    {
        ElementTakeoff wall = Wall(
            layers: [Layer(0, LayerFunction.Structure, 0.130, "brick"), Layer(1, LayerFunction.Membrane, 0.0, "vapour")],
            materials: [.. Material("brick", 1.62630, 12.51), .. Material("vapour", 0.0, 12.51)]);

        LayerLine membrane = ByLayer(wall, Walls(Structure(QuantityUnit.CubicMetre))).Lines[1];

        Assert.Equal(new Quantity(13.11, QuantityUnit.SquareMetre), new Quantity(Math.Round(membrane.Metrado.Metrado.Value, 9), membrane.Metrado.Metrado.Unit));
    }

    /// <summary>A volume share is not exact against deletion (PR 28): an m3 line keeps Revit's deduction, and is told about how much.</summary>
    [Fact]
    public void AnM3LineKeepsRevitsDeductionAndSaysHowMuch()
    {
        (IReadOnlyList<LayerLine> lines, IReadOnlyList<ValidationWarning> warnings) = ByLayer(Wall(), Walls(Structure(QuantityUnit.CubicMetre)));

        LayerLine brick = lines[1];
        Assert.Equal((QuantityUnit.CubicMetre, 1.62630, 1.62630, 1.62630), (brick.Metrado.Metrado.Unit, brick.Metrado.Metrado.Value, brick.Metrado.Raw.Value, brick.Metrado.Gross.Value));
        Assert.Equal(13.11, lines[0].Metrado.Metrado.Value, 9);
        Assert.Equal(
            "The openings the threshold adds back to this element (window, 0.6 m2 in all) are not returned to its layer lines in m3, since a share of volume is not exact: "
            + "each keeps Revit's deduction of them, about their area times its layers' width (Material brick 0.078 m3).",
            Assert.Single(warnings).Condition);
    }

    /// <summary>A pipe sleeve's amount is stated, not rounded away: the openings added back are the small ones.</summary>
    [Fact]
    public void ASmallOpeningsAmountIsStatedToTheCubicCentimetre()
    {
        ElementTakeoff wall = Wall(openings: [new OpeningQuantity("sleeve", new Quantity(0.0004, QuantityUnit.SquareMetre))]);

        string warning = Assert.Single(ByLayer(wall, Walls(Structure(QuantityUnit.CubicMetre))).Warnings).Condition;

        Assert.Contains("(sleeve, 0.0004 m2 in all)", warning, StringComparison.Ordinal);
        Assert.EndsWith("(Material brick 0.000052 m3).", warning, StringComparison.Ordinal);
    }

    /// <summary>Each m3 line is given its own amount, by its own layers' summed width.</summary>
    [Fact]
    public void EachM3LineIsToldItsOwnAmount()
    {
        ElementTakeoff wall = Wall(
            layers: [Layer(0, LayerFunction.Structure, 0.100, "block"), Layer(1, LayerFunction.Substrate, 0.050, "screed"), Layer(2, LayerFunction.Structure, 0.100, "block")],
            materials: [.. Material("block", 2.50200, 25.02), .. Material("screed", 0.62550, 12.51)]);
        LayerCriterion cubic = LayerCriterion.TryCreate(new Dictionary<LayerFunction, QuantityUnit> { [LayerFunction.Structure] = QuantityUnit.CubicMetre, [LayerFunction.Substrate] = QuantityUnit.CubicMetre }, "Walls").Value;

        Assert.EndsWith("(Material block 0.12 m3, Material screed 0.03 m3).", Assert.Single(ByLayer(wall, Walls(cubic)).Warnings).Condition, StringComparison.Ordinal);
    }

    /// <summary>Under a condition no share can be told from a width: every line keeps Revit's deduction, and the warning says so.</summary>
    [Fact]
    public void UnderAConditionEveryLineKeepsRevitsDeduction()
    {
        (IReadOnlyList<LayerLine> lines, IReadOnlyList<ValidationWarning> warnings) = ByLayer(Wall(conditions: [AddBackCondition.WrapsAtInserts]));

        Assert.All(lines, line => Assert.Equal(line.Metrado.Raw.Value, line.Metrado.Metrado.Value));
        Assert.Equal(
            "The openings the threshold adds back to this element (window, 0.6 m2 in all) are not returned to its layer lines Material tile, Material brick, Material plaster: "
            + "its layers wrap at inserts, or a door or window in it sets its own Wall Closure, so how much each layer lost to them cannot be told from its width. "
            + "Each of those lines keeps Revit's deduction of them, whatever it was.",
            Assert.Single(warnings).Condition);
    }

    /// <summary>
    /// Under a condition no amount is stated, not even for an m3 line: a
    /// vertically compound tile band an opening misses lost nothing to it.
    /// </summary>
    [Fact]
    public void UnderSeveralConditionsEachIsNamedAndNoAmountIsGiven()
    {
        (_, IReadOnlyList<ValidationWarning> warnings) = ByLayer(
            Wall(conditions: [AddBackCondition.VerticallyCompound, AddBackCondition.VariableLayer, AddBackCondition.ShapeEdited, AddBackCondition.StructuralDeck]),
            Walls(Structure(QuantityUnit.CubicMetre)));

        string warning = Assert.Single(warnings).Condition;
        Assert.Contains(
            ": its type is vertically compound; a layer of its type varies in thickness; its shape is edited; its type has a structural deck, so how much each layer lost",
            warning,
            StringComparison.Ordinal);
        Assert.DoesNotContain(" m3", warning, StringComparison.Ordinal);
    }

    /// <summary>With nothing added back there is nothing kept to warn about.</summary>
    [Fact]
    public void NothingAddedBackMeansNothingToWarnAbout()
    {
        ElementTakeoff wall = Wall(conditions: [AddBackCondition.WrapsAtInserts], openings: [new OpeningQuantity("door", new Quantity(1.89, QuantityUnit.SquareMetre))]);

        Assert.Empty(ByLayer(wall, Walls(Structure(QuantityUnit.CubicMetre))).Warnings);
    }

    [Theory]
    [InlineData(BoundaryMode.Exclusive, 12.51)]
    [InlineData(BoundaryMode.Inclusive, 13.51)]
    public void AnOpeningAtTheThresholdFollowsTheModeOnEveryLine(BoundaryMode mode, double metrado)
    {
        ElementTakeoff wall = Wall(openings: [new OpeningQuantity("hatch", new Quantity(1.00, QuantityUnit.SquareMetre))]);
        OpeningsThreshold threshold = OpeningsThreshold.TryCreate(1.0, QuantityUnit.SquareMetre, mode, "Walls").Value;

        Assert.All(ByLayer(wall, Walls(threshold: threshold)).Lines, line => Assert.Equal(metrado, line.Metrado.Metrado.Value, 9));
    }

    [Fact]
    public void AZeroThresholdAddsNothingBack()
    {
        OpeningsThreshold zero = OpeningsThreshold.TryCreate(0, QuantityUnit.SquareMetre, BoundaryMode.Exclusive, "Walls").Value;

        Assert.All(ByLayer(Wall(), Walls(threshold: zero)).Lines, line => Assert.Equal(12.51, line.Metrado.Metrado.Value));
    }

    public static TheoryData<string, Func<ElementTakeoff>, Func<CategoryCriterion>, string> Whole() => new()
    {
        { "unreconciled", () => Wall(materials: [.. Material("plaster", 0.18765, 12.51), .. Material("tile", 0.12510, 12.51), .. Material("brick", 1.62630, 12.51)]) with { Quantities = [.. Wall().Quantities.Select(q => q.SourceKey == LayerSources.HostVolume ? new RawQuantity(LayerSources.HostVolume, new Quantity(2.5, QuantityUnit.CubicMetre)) : q)] }, () => Walls(), "measured whole" },
        { "a material in two units", () => Wall(layers: [Layer(0, LayerFunction.Substrate, 0.015, "board"), Layer(1, LayerFunction.Structure, 0.130, "board")], materials: Material("board", 1.8765, 25.02)), () => Walls(Structure(QuantityUnit.CubicMetre)), "different units" },
        { "an opening in m3", () => Wall(openings: [new OpeningQuantity("odd", new Quantity(0.6, QuantityUnit.CubicMetre))]), () => Walls(), "wrong unit" },
    };

    [Theory]
    [MemberData(nameof(Whole))]
    public void AnElementThatCannotBeMeasuredByLayerIsMeasuredWhole(string what, Func<ElementTakeoff> wall, Func<CategoryCriterion> criterion, string phrase)
    {
        string why = LayerMeasurement.Measure(wall(), criterion()).Match(byLayer: (_, _) => "by layer", whole: warning => warning.Condition);

        Assert.Contains(phrase, why, StringComparison.Ordinal);
        Assert.NotEmpty(what);
    }

    [Fact]
    public void OnlyALayeredCriterionMeasuresByLayer()
    {
        CategoryCriterion whole = new("Walls", QuantityUnit.SquareMetre, ["HOST_AREA_COMPUTED"], Exclusive);

        Assert.Throws<ArgumentException>(() => LayerMeasurement.Measure(Wall(), whole));
    }

    private static LayerCriterion Structure(QuantityUnit unit) =>
        LayerCriterion.TryCreate(new Dictionary<LayerFunction, QuantityUnit> { [LayerFunction.Structure] = unit }, "Walls").Value;
}

using Metrado.Configuration;
using Metrado.Domain;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// The export under criteria that take walls off by material layer (tasks
/// 3.1, 3.2 and 3.5 end to end): one line per material, coded by the
/// material; an element whose materials do not account for it measured
/// whole, under its own code, with the reason; every other category as before.
/// </summary>
/// <remarks>
/// The wall is the domain suite's: 12.51 m2 of 10 mm tile, 130 mm brick and
/// 15 mm plaster, with a 0.60 m2 window the 1 m2 threshold adds back and a
/// 1.89 m2 door it keeps deducted. Each m2 line is 12.51 + 0.60 = 13.11.
/// </remarks>
public sealed class LayeredExportTests
{
    [Fact]
    public void AThreeMaterialWallIsThreeLinesUnderItsUniqueIdCodedByTheirMaterials()
    {
        TakeoffExport.Outcome outcome = TakeoffExport.Run(Layered(), [Wall()], []);

        List<Linea> lines = [.. outcome.Result.Partidas.SelectMany(partida => partida.Lineas)];
        Assert.Equal(3, lines.Count);
        Assert.All(lines, line => Assert.Equal("w-1", line.Element.UniqueId));
        Assert.Equal(
            ["02.01.01 Ladrillo 13.11", "02.04.01 Tarrajeo 13.11", "02.05.01 Enchape 13.11"],
            outcome.Result.Partidas.SelectMany(partida => partida.Lineas.Select(line => $"{partida.Key.PartidaCode} {line.Layer!.Material.MaterialName} {line.Metrado.Metrado.Value:0.##}")).Order(StringComparer.Ordinal));
        Assert.Empty(outcome.Report.Warnings);
    }

    /// <summary>The host's code names the assembly, never a layer: an uncoded material is unclassified.</summary>
    [Fact]
    public void AnUncodedMaterialIsUnclassifiedThoughItsHostIsCoded()
    {
        TakeoffExport.Outcome outcome = TakeoffExport.Run(Layered(), [Wall(plasterKeynote: null)], []);

        Partida unclassified = Assert.Single(outcome.Result.Partidas, partida => partida.IsUnclassified);
        Assert.Equal("Tarrajeo", Assert.Single(unclassified.Lineas).Layer!.Material.MaterialName);
        Assert.DoesNotContain(outcome.Result.Partidas, partida => partida.Key.PartidaCode == "B2010");
        Assert.Equal((1, 1), (outcome.Report.UnclassifiedCount, outcome.Report.UnclassifiedElements));
    }

    /// <summary>Materials that do not add up to the wall: one whole line under the wall's own code, and why.</summary>
    [Fact]
    public void AWallWhoseMaterialsDoNotAccountForItIsMeasuredWholeWithTheReason()
    {
        TakeoffExport.Outcome outcome = TakeoffExport.Run(Layered(), [Wall(wholeVolumeOver: 0.01)], []);

        Linea line = Assert.Single(Assert.Single(outcome.Result.Partidas).Lineas);
        Assert.Equal(("B2010", null, 13.11), (outcome.Result.Partidas[0].Key.PartidaCode, line.Layer, Math.Round(line.Metrado.Metrado.Value, 9)));
        ValidationWarning why = Assert.Single(outcome.Report.Warnings);
        Assert.Equal("w-1", why.UniqueId);
        Assert.StartsWith("Not taken off by material layer, so measured whole: its materials' volumes add up to", why.Condition, StringComparison.Ordinal);
    }

    /// <summary>A category the criteria do not layer is measured whole, even if its layers were read.</summary>
    [Fact]
    public void ACategoryNotTakenOffByLayerIsMeasuredWholeAsBefore()
    {
        TakeoffExport.Outcome outcome = TakeoffExport.Run(Defaults(), [Wall()], []);

        Linea line = Assert.Single(Assert.Single(outcome.Result.Partidas).Lineas);
        Assert.Equal(("B2010", null), (outcome.Result.Partidas[0].Key.PartidaCode, line.Layer));
        Assert.Empty(outcome.Report.Warnings);
    }

    /// <summary>An opening is decided once per host, so it is flagged once, not once per layer line.</summary>
    [Fact]
    public void AnOpeningNearTheThresholdIsFlaggedOnceForALayeredWall()
    {
        ElementTakeoff wall = Wall() with { Openings = [new OpeningQuantity("window", new Quantity(0.995, QuantityUnit.SquareMetre))] };

        TakeoffExport.Outcome outcome = TakeoffExport.Run(Layered(), [wall], []);

        Assert.Equal(3, outcome.Result.LineCount);
        Assert.Single(outcome.Report.Warnings, warning => warning.Condition.Contains("Opening window", StringComparison.Ordinal));
    }

    /// <summary>With brick in m3, the added-back window stays deducted from it, and the report says so.</summary>
    [Fact]
    public void TheLayerMeasurementsOwnWarningsReachTheReport()
    {
        TakeoffExport.Outcome outcome = TakeoffExport.Run(Layered(new Dictionary<LayerFunction, QuantityUnit> { [LayerFunction.Structure] = QuantityUnit.CubicMetre }), [Wall()], []);

        Assert.Equal(3, outcome.Result.LineCount);
        ValidationWarning kept = Assert.Single(outcome.Report.Warnings);
        Assert.EndsWith("(Ladrillo 0.078 m3).", kept.Condition, StringComparison.Ordinal);
    }

    private static EffectiveCriteria Defaults() => CriteriaResolver.Resolve(CriteriaFileLookup.Absent, path: null).Value;

    private static EffectiveCriteria Layered(IReadOnlyDictionary<LayerFunction, QuantityUnit>? units = null) =>
        new(
            CriteriaSet.Merge(CriteriaSet.Default, [new CategoryOverride("Walls", layers: LayerOverride.On(units ?? new Dictionary<LayerFunction, QuantityUnit>()))]).Value,
            ConfigSource.File,
            @"C:\Projects\Office\metrado.criteria.json");

    private static ElementTakeoff Wall(string? plasterKeynote = "02.04.01", double wholeVolumeOver = 0)
    {
        (string Id, string Name, string? Keynote, LayerFunction Function, double Width)[] materials =
        [
            ("tile", "Enchape", "02.05.01", LayerFunction.Finish1, 0.010),
            ("brick", "Ladrillo", "02.01.01", LayerFunction.Structure, 0.130),
            ("plaster", "Tarrajeo", plasterKeynote, LayerFunction.Finish2, 0.015),
        ];

        List<RawQuantity> quantities = [new RawQuantity("HOST_AREA_COMPUTED", new Quantity(12.51, QuantityUnit.SquareMetre))];
        foreach ((string id, string name, string? keynote, _, double width) in materials)
        {
            MaterialRef material = new(id, name, new CodificationReadings(assemblyCode: null, keynote, new Dictionary<string, string?>()));
            quantities.Add(new RawQuantity(LayerSources.MaterialVolume, new Quantity(Math.Round(12.51 * width, 9), QuantityUnit.CubicMetre), material));
            quantities.Add(new RawQuantity(LayerSources.MaterialArea, new Quantity(12.51, QuantityUnit.SquareMetre), material));
        }

        double volume = materials.Sum(material => Math.Round(12.51 * material.Width, 9)) + wholeVolumeOver;
        quantities.Add(new RawQuantity(LayerSources.HostVolume, new Quantity(volume, QuantityUnit.CubicMetre)));

        return new ElementTakeoff(
            UniqueId: "w-1",
            CategoryName: "Walls",
            FamilyName: "Basic Wall",
            TypeName: "Tiled brick",
            TypeKey: "t-1",
            Codes: new CodificationReadings("B2010", keynote: null, sharedParameters: new Dictionary<string, string?>()),
            Quantities: quantities,
            Openings:
            [
                new OpeningQuantity("window", new Quantity(0.60, QuantityUnit.SquareMetre)),
                new OpeningQuantity("door", new Quantity(1.89, QuantityUnit.SquareMetre)),
            ])
        {
            Layers = new LayerStructure(
                [.. materials.Select((material, position) => new CompoundLayer(position, material.Function, new Quantity(material.Width, QuantityUnit.Metre), material.Id))],
                []),
        };
    }
}

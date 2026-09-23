namespace Metrado.Domain.Tests;

/// <summary>
/// Pins how a layer line is coded and reported (task 3.2, the user's choice
/// in PR 27): by its material's own codes through the standard chain, never
/// by the host's, since a partida is one priced item and a wall is several;
/// and with the unit its threshold is really in, which for a layer line in
/// m3 is still the host's m2.
/// </summary>
public sealed class LayerCodificationTests
{
    private static ElementTakeoff Host() =>
        MeasurementFixture.Wall("w-1") with { Codes = new CodificationReadings("C1010", "04 21 00.A1", new Dictionary<string, string?>()) };

    private static MaterialRef Material(string? keynote, string? shared = null) =>
        new("plaster", "Tarrajeo frotachado", new CodificationReadings(null, keynote, shared is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["guid"] = shared }));

    [Fact]
    public void ALayerIsCodedByItsMaterialsKeynoteTrimmedNeverByTheHost()
    {
        Assert.Equal("02.04.01", CodificationChain.Standard(null).ResolveLayer(Host(), Material(" 02.04.01 ")));
    }

    [Fact]
    public void ABlankKeynotePassesToTheMaterialsSharedParameter()
    {
        Assert.Equal("S-10", CodificationChain.Standard("guid").ResolveLayer(Host(), Material("  ", "S-10")));
    }

    /// <summary>A material with no code of its own is unclassified, not the wall's C1010.</summary>
    [Fact]
    public void AnUncodedMaterialIsUnclassifiedWhateverTheHostCarries()
    {
        Assert.Equal(UnclassifiedResolver.Code, CodificationChain.Standard(null).ResolveLayer(Host(), Material(null)));
    }

    /// <summary>Nor the host's nominated shared parameter: the material's codes replace the host's, all of them.</summary>
    [Fact]
    public void TheHostsSharedParameterNeverCodesALayer()
    {
        ElementTakeoff host = Host() with { Codes = new CodificationReadings(null, null, new Dictionary<string, string?> { ["guid"] = "H-1" }) };

        Assert.Equal("H-1", CodificationChain.Standard("guid").Resolve(host));
        Assert.Equal(UnclassifiedResolver.Code, CodificationChain.Standard("guid").ResolveLayer(host, Material(null)));
    }

    [Fact]
    public void ALineaCarriesTheLayerItMeasures()
    {
        MaterialLayers layer = new(Material("02.04.01"), [new CompoundLayer(0, LayerFunction.Finish1, new Quantity(0.015, QuantityUnit.Metre), "plaster")]);

        Assert.Null(TakeoffFixture.Line("w-1", "C1010").Layer);
        Assert.Same(layer, new Linea(Host(), "02.04.01", TakeoffFixture.Measured(13.11), layer).Layer);
    }

    /// <summary>Every metrado states its threshold's unit: the measured unit for a whole element, m2 for a layer line in m3.</summary>
    [Fact]
    public void AMetradoStatesTheUnitItsThresholdIsIn()
    {
        OpeningsThreshold threshold = OpeningsThreshold.TryCreate(1.0, QuantityUnit.SquareMetre, BoundaryMode.Exclusive, "Walls").Value;
        MetradoResult whole = Measurement.Apply(Host(), new Quantity(12.0, QuantityUnit.SquareMetre), [], threshold).Result!;
        MaterialRef brick = new("brick", "Ladrillo KK");
        ElementTakeoff layered = Host() with
        {
            Quantities =
            [
                new RawQuantity(LayerSources.HostVolume, new Quantity(1.6, QuantityUnit.CubicMetre)),
                new RawQuantity(LayerSources.MaterialVolume, new Quantity(1.6, QuantityUnit.CubicMetre), brick),
                new RawQuantity(LayerSources.MaterialArea, new Quantity(12.3, QuantityUnit.SquareMetre), brick),
            ],
            Openings = [],
            Layers = new LayerStructure([new CompoundLayer(0, LayerFunction.Structure, new Quantity(0.13, QuantityUnit.Metre), "brick")], []),
        };
        CategoryCriterion cubic = new("Walls", QuantityUnit.SquareMetre, ["HOST_AREA_COMPUTED"], threshold,
            LayerCriterion.TryCreate(new Dictionary<LayerFunction, QuantityUnit> { [LayerFunction.Structure] = QuantityUnit.CubicMetre }, "Walls").Value);
        MetradoResult volume = LayerMeasurement.Measure(layered, cubic).Match((lines, _) => lines[0].Metrado, why => throw new Xunit.Sdk.XunitException(why.Condition));

        Assert.Equal(QuantityUnit.SquareMetre, whole.AppliedThresholdUnit);
        Assert.Equal((QuantityUnit.CubicMetre, QuantityUnit.SquareMetre), (volume.Metrado.Unit, volume.AppliedThresholdUnit));
        Assert.Equal(QuantityUnit.CubicMetre, TakeoffFixture.Measured(1.0, QuantityUnit.CubicMetre).AppliedThresholdUnit);
    }

    [Fact]
    public void TheReportStatesEachCapitulosThresholdInItsOwnUnit()
    {
        MetradoResult volume = TakeoffFixture.Measured(1.6, QuantityUnit.CubicMetre) with { AppliedThresholdUnit = QuantityUnit.SquareMetre };
        TakeoffResult result = TakeoffResult.Group([new Linea(Host(), "02.01.01", volume)]);

        AppliedCriterion applied = Assert.Single(RunReport.For(result, []).Applied);

        Assert.Equal((QuantityUnit.CubicMetre, QuantityUnit.SquareMetre), (applied.Unit, applied.ThresholdUnit));
    }

    /// <summary>Unclassified layer lines are counted as lines and, apart, as the elements they belong to.</summary>
    [Fact]
    public void TheReportCountsUnclassifiedElementsApartFromTheirLines()
    {
        TakeoffResult result = TakeoffResult.Group(
        [
            TakeoffFixture.Line("w-1", UnclassifiedResolver.Code),
            TakeoffFixture.Line("w-1", UnclassifiedResolver.Code, amount: 1.0, unit: QuantityUnit.CubicMetre),
            TakeoffFixture.Line("w-2", UnclassifiedResolver.Code),
            TakeoffFixture.Line("w-3", "C1010"),
        ]);

        RunReport report = RunReport.For(result, []);

        Assert.Equal((3, 2), (report.UnclassifiedCount, report.UnclassifiedElements));
    }
}

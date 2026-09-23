namespace Metrado.Domain.Tests;

/// <summary>
/// <c>model-validation-warnings</c>, "Pre-Export Validation Pass" and
/// "Suspect Metrado Detection" (task 3.3): a clean model raises no warning,
/// and each condition the pass detects names its element by UniqueId,
/// category, family and type, and states the condition.
/// </summary>
public sealed class TakeoffPassTests
{
    private static readonly CodificationChain Chain = CodificationChain.Standard(sharedParameter: null);

    /// <summary>Coded walls with openings clear of the threshold, counted doors and a railing: nothing to warn about.</summary>
    [Fact]
    public void ACleanModelRaisesNoWarning()
    {
        TakeoffPass.Outcome outcome = TakeoffPass.Run(
            CriteriaSet.Default,
            [
                Element("w-1", "Walls", "B2010", Area(18.0), openings: [("window", 0.6), ("door", 1.89)]),
                Element("w-2", "Walls", "B2010", Area(12.0)),
                Element("d-1", "Doors", "C1020"),
                Element("r-1", "Railings", "B1080", new RawQuantity("CURVE_ELEM_LENGTH", new Quantity(7.5, QuantityUnit.Metre))),
            ],
            Chain);

        Assert.Empty(outcome.Warnings);
        Assert.Equal(4, outcome.Lines.Count);
    }

    public static TheoryData<Func<ElementTakeoff>, string> Suspect() => new()
    {
        { () => Element("x-1", "Columns", "B1010", Area(3.0)), "No criterion is configured for category 'Columns'" },
        { () => Element("x-2", "Walls", "B2010"), "No quantity source had a value" },
        { () => Element("x-3", "Walls", "B2010", Area(-1.0), openings: [("window", 0.5)]), "Metrado clamped to the gross quantity" },
        { () => Layered("x-4", wholeVolumeOver: 0.01), "Not taken off by material layer, so measured whole" },
        { () => Element("x-5", "Walls", "B2010", Area(18.0), openings: [("window", 0.995)]), "within 0.01 of the 1 m2 threshold" },
    };

    [Theory]
    [MemberData(nameof(Suspect))]
    public void EachConditionNamesItsElementAndWhatWasDetected(Func<ElementTakeoff> build, string phrase)
    {
        ElementTakeoff element = build();

        TakeoffPass.Outcome outcome = TakeoffPass.Run(LayeredWalls(), [element], Chain);

        ValidationWarning warning = Assert.Single(outcome.Warnings, warning => warning.Condition.Contains(phrase, StringComparison.Ordinal));
        Assert.Equal(
            (element.UniqueId, element.CategoryName, element.FamilyName, element.TypeName),
            (warning.UniqueId, warning.CategoryName, warning.FamilyName, warning.TypeName));
    }

    /// <summary>
    /// The band is inclusive: an opening exactly its width from the threshold
    /// is flagged. A zero threshold and an opening of 0.01 m2 are exactly that
    /// far apart in binary too, which 0.99 and 1.0 are not.
    /// </summary>
    [Fact]
    public void AnOpeningExactlyTheBandAwayIsFlagged()
    {
        CriteriaSet zero = CriteriaSet.Merge(CriteriaSet.Default, [new CategoryOverride("Walls", threshold: 0)]).Value;

        TakeoffPass.Outcome outcome = TakeoffPass.Run(zero, [Element("w-1", "Walls", "B2010", Area(18.0), openings: [("hatch", 0.01)])], Chain);

        Assert.Contains("Opening hatch measures 0.01 m2, within 0.01 of the 0 m2 threshold", Assert.Single(outcome.Warnings).Condition, StringComparison.Ordinal);
    }

    /// <summary>
    /// "Zero metrado on a modelled element": a wall whose metrado is zero, or
    /// less, is warned about, and its line is still written as measured.
    /// </summary>
    [Theory]
    [InlineData(0.0, "Its metrado is 0 m2")]
    [InlineData(-2.0, "Its metrado is -2 m2")]
    public void AZeroOrNegativeMetradoIsWarnedAbout(double area, string phrase)
    {
        TakeoffPass.Outcome outcome = TakeoffPass.Run(CriteriaSet.Default, [Element("w-0", "Walls", "B2010", Area(area))], Chain);

        Assert.Equal("w-0", Assert.Single(outcome.Lines).Element.UniqueId);
        ValidationWarning warning = Assert.Single(outcome.Warnings, warning => warning.Condition.StartsWith(phrase, StringComparison.Ordinal));
        Assert.Equal(("w-0", "Walls", "Walls family", "Walls type"), (warning.UniqueId, warning.CategoryName, warning.FamilyName, warning.TypeName));
        Assert.EndsWith("so check its geometry; the line is written as measured.", warning.Condition, StringComparison.Ordinal);
    }

    /// <summary>A layer line with no area is named by its material, and the wall's other lines raise nothing.</summary>
    [Fact]
    public void AZeroLayerLineIsWarnedAboutByItsMaterial()
    {
        ElementTakeoff wall = Layered("w-4", wholeVolumeOver: 0);
        wall = wall with
        {
            Quantities = [.. wall.Quantities.Select(q => q.SourceKey == LayerSources.MaterialArea && q.Material!.MaterialId == "plaster" ? new RawQuantity(q.SourceKey, new Quantity(0, QuantityUnit.SquareMetre), q.Material) : q)],
        };

        TakeoffPass.Outcome outcome = TakeoffPass.Run(LayeredWalls(), [wall], Chain);

        Assert.Equal(2, outcome.Lines.Count);
        Assert.StartsWith("Its Tarrajeo line measures 0 m2", Assert.Single(outcome.Warnings).Condition, StringComparison.Ordinal);
    }

    /// <summary>A count is one per instance, never zero: a counted element raises nothing.</summary>
    [Fact]
    public void ACountedElementIsNeverWarnedAboutAsZero()
    {
        Assert.Empty(TakeoffPass.Run(CriteriaSet.Default, [Element("d-1", "Doors", "C1020")], Chain).Warnings);
    }

    /// <summary>A warning never stops the pass: the element beside a suspect one is still measured and coded.</summary>
    [Fact]
    public void ASuspectElementDoesNotStopThePass()
    {
        TakeoffPass.Outcome outcome = TakeoffPass.Run(
            CriteriaSet.Default,
            [Element("x-2", "Walls", "B2010"), Element("w-1", "Walls", "B2010", Area(18.0))],
            Chain);

        Assert.Equal("w-1", Assert.Single(outcome.Lines).Element.UniqueId);
        Assert.Single(outcome.Warnings);
    }

    /// <summary>A layered wall is measured by its materials, each line coded by its own material.</summary>
    [Fact]
    public void ALayeredWallGivesALinePerMaterialCodedByIt()
    {
        TakeoffPass.Outcome outcome = TakeoffPass.Run(LayeredWalls(), [Layered("w-3", wholeVolumeOver: 0)], Chain);

        Assert.Equal(["02.01.01", "02.04.01"], outcome.Lines.Select(line => line.PartidaCode).Order(StringComparer.Ordinal));
        Assert.Empty(outcome.Warnings);
    }

    private static CriteriaSet LayeredWalls() =>
        CriteriaSet.Merge(CriteriaSet.Default, [new CategoryOverride("Walls", layers: LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit>()))]).Value;

    private static RawQuantity Area(double squareMetres) => new("HOST_AREA_COMPUTED", new Quantity(squareMetres, QuantityUnit.SquareMetre));

    private static ElementTakeoff Element(string uniqueId, string category, string code, RawQuantity? quantity = null, IReadOnlyList<(string Id, double Area)>? openings = null) =>
        new(
            UniqueId: uniqueId,
            CategoryName: category,
            FamilyName: $"{category} family",
            TypeName: $"{category} type",
            TypeKey: $"{category}-type",
            Codes: new CodificationReadings(code, keynote: null, sharedParameters: new Dictionary<string, string?>()),
            Quantities: quantity is null ? [] : [quantity],
            Openings: [.. (openings ?? []).Select(opening => new OpeningQuantity(opening.Id, new Quantity(opening.Area, QuantityUnit.SquareMetre)))]);

    /// <summary>12.51 m2 of 130 mm brick and 15 mm plaster, each keyed.</summary>
    private static ElementTakeoff Layered(string uniqueId, double wholeVolumeOver)
    {
        MaterialRef brick = new("brick", "Ladrillo", new CodificationReadings(null, "02.01.01", new Dictionary<string, string?>()));
        MaterialRef plaster = new("plaster", "Tarrajeo", new CodificationReadings(null, "02.04.01", new Dictionary<string, string?>()));
        return Element(uniqueId, "Walls", "B2010", Area(12.51)) with
        {
            Quantities =
            [
                Area(12.51),
                new RawQuantity(LayerSources.HostVolume, new Quantity(1.81395 + wholeVolumeOver, QuantityUnit.CubicMetre)),
                new RawQuantity(LayerSources.MaterialVolume, new Quantity(1.62630, QuantityUnit.CubicMetre), brick),
                new RawQuantity(LayerSources.MaterialArea, new Quantity(12.51, QuantityUnit.SquareMetre), brick),
                new RawQuantity(LayerSources.MaterialVolume, new Quantity(0.18765, QuantityUnit.CubicMetre), plaster),
                new RawQuantity(LayerSources.MaterialArea, new Quantity(12.51, QuantityUnit.SquareMetre), plaster),
            ],
            Layers = new LayerStructure(
                [
                    new CompoundLayer(0, LayerFunction.Structure, new Quantity(0.130, QuantityUnit.Metre), "brick"),
                    new CompoundLayer(1, LayerFunction.Finish2, new Quantity(0.015, QuantityUnit.Metre), "plaster"),
                ],
                []),
        };
    }
}

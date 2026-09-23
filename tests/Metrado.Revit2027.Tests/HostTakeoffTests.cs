using Metrado.Domain;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins the Revit-free half of wall extraction: what one wall's reading, taken
/// in Revit's internal units, becomes as the domain's <see cref="ElementTakeoff"/>.
///
/// The conversion is injected. Inside Revit it is <c>UnitUtils</c>, which cannot
/// run in a test process; here a converter that cannot be mistaken for the real
/// factor proves that every quantity passes through it exactly once and nothing
/// else does.
/// </summary>
public sealed class HostTakeoffTests
{
    /// <summary>Doubles, so a value converted twice or not at all is visible.</summary>
    private static double Doubled(double squareFeet) => squareFeet * 2;

    [Fact]
    public void TheCategoryIsTheCriteriaKeyNotTheLocalizedCategoryName()
    {
        ElementTakeoff takeoff = HostTakeoff.From(Reading(), Doubled);

        Assert.Equal("Walls", takeoff.CategoryName);
        Assert.True(
            CriteriaSet.Default.ByCategory.ContainsKey(takeoff.CategoryName),
            "The category key does not name a built-in criterion, so no wall would ever be measured.");
    }

    /// <summary>Floors and roofs share the reading, each under the key its own criterion is known by.</summary>
    [Theory]
    [InlineData("Floors")]
    [InlineData("Roofs")]
    public void AFloorOrRoofKeepsItsOwnCategoryKey(string key)
    {
        ElementTakeoff takeoff = HostTakeoff.From(Reading() with { CategoryKey = key }, Doubled);

        Assert.Equal(key, takeoff.CategoryName);
        Assert.Contains(Assert.Single(takeoff.Quantities).SourceKey, CriteriaSet.Default.ByCategory[key].Sources);
    }

    /// <summary>A warning says what was cut, so a floor's is not read as a wall's.</summary>
    [Theory]
    [InlineData("Walls", "cuts this wall")]
    [InlineData("Floors", "cuts this floor")]
    [InlineData("Roofs", "cuts this roof")]
    public void AWarningNamesWhatTheOpeningCuts(string key, string phrase)
    {
        HostReading reading = Reading(unmeasured: [new UnmeasuredOpening("shaft", "no hole")]) with { CategoryKey = key };

        Assert.Contains(phrase, Assert.Single(HostTakeoff.Warnings(HostTakeoff.From(reading, Doubled), reading)).Condition);
    }

    /// <summary>An in-place floor or roof is not measured, and the warning names it so its absence is not silent.</summary>
    [Theory]
    [InlineData("Floors", "in-place floor")]
    [InlineData("Roofs", "in-place roof")]
    public void AnInPlaceHostIsNamedAsNotMeasured(string key, string phrase)
    {
        ValidationWarning warning = HostTakeoff.NotRead("f-1", key, "Terrace slab", "Tiled 60mm");

        Assert.Equal(("f-1", key, "Terrace slab", "Tiled 60mm"), (warning.UniqueId, warning.CategoryName, warning.FamilyName, warning.TypeName));
        Assert.Contains(phrase, warning.Condition, StringComparison.Ordinal);
        Assert.Contains("no line of the budget", warning.Condition, StringComparison.Ordinal);
    }

    [Fact]
    public void TheComputedAreaIsTheOnlyQuantityConvertedOnceInSquareMetres()
    {
        ElementTakeoff takeoff = HostTakeoff.From(Reading(computedArea: 107.639), Doubled);

        RawQuantity raw = Assert.Single(takeoff.Quantities);
        Assert.Equal("HOST_AREA_COMPUTED", raw.SourceKey);
        Assert.Equal(new Quantity(215.278, QuantityUnit.SquareMetre), raw.Amount);
        Assert.Null(raw.Material);
    }

    /// <summary>
    /// The source key must be one the built-in Walls criterion reads, or the
    /// wall reaches the domain with a quantity no criterion ever selects.
    /// </summary>
    [Fact]
    public void TheQuantitySourceIsOneTheWallsCriterionReads()
    {
        ElementTakeoff takeoff = HostTakeoff.From(Reading(), Doubled);

        Assert.Contains(Assert.Single(takeoff.Quantities).SourceKey, CriteriaSet.Default.ByCategory["Walls"].Sources);
    }

    /// <summary>
    /// The product's core rule compares each opening on its own. The adapter
    /// keeps them apart, in the order read, each converted once and carrying
    /// its own identity.
    /// </summary>
    [Fact]
    public void EachOpeningStaysIndividualConvertedAndIdentified()
    {
        HostReading reading = Reading(openings:
        [
            new OpeningReading("door-1", 21.0),
            new OpeningReading("window-1", 12.5),
            new OpeningReading("window-2", 12.5),
        ]);

        ElementTakeoff takeoff = HostTakeoff.From(reading, Doubled);

        Assert.Equal(
            [
                new OpeningQuantity("door-1", new Quantity(42.0, QuantityUnit.SquareMetre)),
                new OpeningQuantity("window-1", new Quantity(25.0, QuantityUnit.SquareMetre)),
                new OpeningQuantity("window-2", new Quantity(25.0, QuantityUnit.SquareMetre)),
            ],
            takeoff.Openings);
    }

    [Fact]
    public void AWallWithNoOpeningsHasAnEmptyListNotNull()
    {
        ElementTakeoff takeoff = HostTakeoff.From(Reading(openings: []), Doubled);

        Assert.NotNull(takeoff.Openings);
        Assert.Empty(takeoff.Openings);
    }

    [Fact]
    public void IdentityAndNamesPassThroughUnchanged()
    {
        ElementTakeoff takeoff = HostTakeoff.From(Reading(), Doubled);

        Assert.Equal("wall-unique-id", takeoff.UniqueId);
        Assert.Equal("Basic Wall", takeoff.FamilyName);
        Assert.Equal("Generic - 200mm", takeoff.TypeName);
        Assert.Equal("type-unique-id", takeoff.TypeKey);
    }

    /// <summary>
    /// The assembly code is passed on as read. Trimming and treating blank as
    /// unresolved belong to the codification chain, which already does both;
    /// doing it here too would hide what the model actually holds.
    /// </summary>
    [Theory]
    [InlineData("B2010")]
    [InlineData("  B2010 ")]
    [InlineData("")]
    [InlineData(null)]
    public void TheAssemblyCodeIsPassedOnAsRead(string? assemblyCode)
    {
        ElementTakeoff takeoff = HostTakeoff.From(Reading(assemblyCode: assemblyCode), Doubled);

        Assert.Equal(assemblyCode, takeoff.Codes.AssemblyCode);
        Assert.Null(takeoff.Codes.Keynote);
        Assert.Empty(takeoff.Codes.SharedParameters);
    }

    /// <summary>
    /// A wall whose area could not be read reaches the domain with no quantity,
    /// which the domain reports as "no source had a value" — never as a
    /// measured 0.0, which would price the wall at nothing without a word.
    /// </summary>
    [Fact]
    public void AnUnreadableComputedAreaGivesNoQuantityNotZero()
    {
        ElementTakeoff takeoff = HostTakeoff.From(Reading(computedArea: null), Doubled);

        Assert.Empty(takeoff.Quantities);
    }

    /// <summary>
    /// Double precision noise from the feet-to-metres round trip must not
    /// decide the product's boundary case: an opening modelled at exactly the
    /// threshold has to compare as exactly the threshold.
    /// </summary>
    [Fact]
    public void ConvertedQuantitiesAreRoundedToTheNanoSquareMetre()
    {
        HostReading reading = Reading(computedArea: 1.0, openings: [new OpeningReading("window-1", 1.0)]);

        ElementTakeoff takeoff = HostTakeoff.From(reading, _ => 0.9999999999999999);

        Assert.Equal(1.0, Assert.Single(takeoff.Quantities).Amount.Value);
        Assert.Equal(1.0, Assert.Single(takeoff.Openings).Amount.Value);
    }

    /// <summary>
    /// Noise larger than one unit in the last place is rounded away too: a
    /// fourteen-nines value is still a metre squared, not a hair under it.
    /// </summary>
    [Theory]
    [InlineData(1.0000000000000002)]
    [InlineData(0.99999999999999)]
    [InlineData(1.0000000000001)]
    public void RoundingRemovesNoiseWellBeyondOneUlp(double noisy)
    {
        ElementTakeoff takeoff = HostTakeoff.From(Reading(), _ => noisy);

        Assert.Equal(1.0, Assert.Single(takeoff.Quantities).Amount.Value);
    }

    [Fact]
    public void EveryUnmeasuredOpeningGetsItsOwnWarning()
    {
        HostReading reading = Reading(unmeasured:
        [
            new UnmeasuredOpening("void-niche", "a void cut"),
            new UnmeasuredOpening("shadow-window", "hosted by another wall"),
        ]);

        IReadOnlyList<ValidationWarning> warnings = HostTakeoff.Warnings(HostTakeoff.From(reading, Doubled), reading);

        Assert.Collection(
            warnings,
            first => Assert.Contains("void-niche", first.Condition),
            second => Assert.Contains("shadow-window", second.Condition));
        Assert.Contains("a void cut", warnings[0].Condition);
        Assert.Contains("hosted by another wall", warnings[1].Condition);
    }

    /// <summary>The rounding removes noise only: a real ninth decimal survives it.</summary>
    [Fact]
    public void RoundingKeepsEveryRealDecimal()
    {
        ElementTakeoff takeoff = HostTakeoff.From(Reading(), _ => 1.234567891);

        Assert.Equal(1.234567891, Assert.Single(takeoff.Quantities).Amount.Value);
    }

    /// <summary>
    /// An opening whose area cannot be measured is never given one. It stays
    /// out of the openings — so Revit's deduction of it stands and it is never
    /// added back — and is reported, by identity and reason, instead.
    /// </summary>
    [Fact]
    public void AnUnmeasuredOpeningIsReportedNeverMeasured()
    {
        HostReading reading = Reading(unmeasured:
        [
            new UnmeasuredOpening("shadow-window", "cuts this wall but is hosted by another"),
        ]);

        ElementTakeoff takeoff = HostTakeoff.From(reading, Doubled);
        ValidationWarning warning = Assert.Single(HostTakeoff.Warnings(takeoff, reading));

        Assert.DoesNotContain(takeoff.Openings, opening => opening.UniqueId == "shadow-window");
        Assert.Equal("wall-unique-id", warning.UniqueId);
        Assert.Equal("Walls", warning.CategoryName);
        Assert.Contains("shadow-window", warning.Condition);
        Assert.Contains("cuts this wall but is hosted by another", warning.Condition);
    }

    [Fact]
    public void AWallWithEveryOpeningMeasuredHasNoWarnings()
    {
        HostReading reading = Reading();

        Assert.Empty(HostTakeoff.Warnings(HostTakeoff.From(reading, Doubled), reading));
    }

    private static HostReading Reading(
        double? computedArea = 100.0,
        string? assemblyCode = "B2010",
        IReadOnlyList<OpeningReading>? openings = null,
        IReadOnlyList<UnmeasuredOpening>? unmeasured = null) =>
        new(
            UniqueId: "wall-unique-id",
            CategoryKey: "Walls",
            FamilyName: "Basic Wall",
            TypeName: "Generic - 200mm",
            TypeUniqueId: "type-unique-id",
            AssemblyCode: assemblyCode,
            ComputedAreaSquareFeet: computedArea,
            Openings: openings ?? [new OpeningReading("door-1", 21.0)],
            Unmeasured: unmeasured ?? []);
}

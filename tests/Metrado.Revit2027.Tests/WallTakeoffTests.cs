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
public sealed class WallTakeoffTests
{
    /// <summary>Doubles, so a value converted twice or not at all is visible.</summary>
    private static double Doubled(double squareFeet) => squareFeet * 2;

    [Fact]
    public void TheCategoryIsTheCriteriaKeyNotTheLocalizedCategoryName()
    {
        ElementTakeoff takeoff = WallTakeoff.From(Reading(), Doubled);

        Assert.Equal("Walls", takeoff.CategoryName);
        Assert.True(
            CriteriaSet.Default.ByCategory.ContainsKey(takeoff.CategoryName),
            "The category key does not name a built-in criterion, so no wall would ever be measured.");
    }

    [Fact]
    public void TheComputedAreaIsTheOnlyQuantityConvertedOnceInSquareMetres()
    {
        ElementTakeoff takeoff = WallTakeoff.From(Reading(computedArea: 107.639), Doubled);

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
        ElementTakeoff takeoff = WallTakeoff.From(Reading(), Doubled);

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
        WallReading reading = Reading(openings:
        [
            new OpeningReading("door-1", 21.0),
            new OpeningReading("window-1", 12.5),
            new OpeningReading("window-2", 12.5),
        ]);

        ElementTakeoff takeoff = WallTakeoff.From(reading, Doubled);

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
        ElementTakeoff takeoff = WallTakeoff.From(Reading(openings: []), Doubled);

        Assert.NotNull(takeoff.Openings);
        Assert.Empty(takeoff.Openings);
    }

    [Fact]
    public void IdentityAndNamesPassThroughUnchanged()
    {
        ElementTakeoff takeoff = WallTakeoff.From(Reading(), Doubled);

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
        ElementTakeoff takeoff = WallTakeoff.From(Reading(assemblyCode: assemblyCode), Doubled);

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
        ElementTakeoff takeoff = WallTakeoff.From(Reading(computedArea: null), Doubled);

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
        WallReading reading = Reading(computedArea: 1.0, openings: [new OpeningReading("window-1", 1.0)]);

        ElementTakeoff takeoff = WallTakeoff.From(reading, _ => 0.9999999999999999);

        Assert.Equal(1.0, Assert.Single(takeoff.Quantities).Amount.Value);
        Assert.Equal(1.0, Assert.Single(takeoff.Openings).Amount.Value);
    }

    /// <summary>The rounding removes noise only: a real ninth decimal survives it.</summary>
    [Fact]
    public void RoundingKeepsEveryRealDecimal()
    {
        ElementTakeoff takeoff = WallTakeoff.From(Reading(), _ => 1.234567891);

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
        WallReading reading = Reading(unmeasured:
        [
            new UnmeasuredOpening("shadow-window", "cuts this wall but is hosted by another"),
        ]);

        ElementTakeoff takeoff = WallTakeoff.From(reading, Doubled);
        ValidationWarning warning = Assert.Single(WallTakeoff.Warnings(takeoff, reading));

        Assert.DoesNotContain(takeoff.Openings, opening => opening.UniqueId == "shadow-window");
        Assert.Equal("wall-unique-id", warning.UniqueId);
        Assert.Equal("Walls", warning.CategoryName);
        Assert.Contains("shadow-window", warning.Condition);
        Assert.Contains("cuts this wall but is hosted by another", warning.Condition);
    }

    [Fact]
    public void AWallWithEveryOpeningMeasuredHasNoWarnings()
    {
        WallReading reading = Reading();

        Assert.Empty(WallTakeoff.Warnings(WallTakeoff.From(reading, Doubled), reading));
    }

    private static WallReading Reading(
        double? computedArea = 100.0,
        string? assemblyCode = "B2010",
        IReadOnlyList<OpeningReading>? openings = null,
        IReadOnlyList<UnmeasuredOpening>? unmeasured = null) =>
        new(
            UniqueId: "wall-unique-id",
            FamilyName: "Basic Wall",
            TypeName: "Generic - 200mm",
            TypeUniqueId: "type-unique-id",
            AssemblyCode: assemblyCode,
            ComputedAreaSquareFeet: computedArea,
            Openings: openings ?? [new OpeningReading("door-1", 21.0)],
            Unmeasured: unmeasured ?? []);
}

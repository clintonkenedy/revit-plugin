using Metrado.Domain;

namespace Metrado.Configuration.Tests;

/// <summary>
/// Pins how a criteria file states a category's material layers (task 3.2,
/// the hook for 3.6): <c>true</c>, an object of layer functions and units,
/// or <c>false</c>; left out, the category inherits. Nothing in it is
/// ignored, and each refusal names its line and position.
/// </summary>
public sealed class LayersFieldTests
{
    [Fact]
    public void TrueTakesTheCategoryOffByLayerInSquareMetres()
    {
        Assert.Equal("on: ", Layers("""{ "Walls": { "layers": true } }"""));
    }

    [Fact]
    public void AnEmptyObjectIsTheSameAsTrue()
    {
        Assert.Equal("on: ", Layers("""{ "Walls": { "layers": {} } }"""));
    }

    [Fact]
    public void FalseTurnsItOffAsStated()
    {
        Assert.Equal("off", Layers("""{ "Walls": { "layers": false } }"""));
    }

    [Fact]
    public void AnObjectStatesAUnitPerLayerFunction()
    {
        Assert.Equal("on: Finish1=m2, Structure=m3", Layers("""{ "Floors": { "layers": { "Structure": "m3", "Finish1": "m2" } } }"""));
    }

    /// <summary>Every function is stated as Revit names it, in m2 or m3; a membrane only in m2.</summary>
    [Theory]
    [InlineData("Structure", "m3")]
    [InlineData("Substrate", "m3")]
    [InlineData("Insulation", "m3")]
    [InlineData("Finish1", "m3")]
    [InlineData("Finish2", "m3")]
    [InlineData("StructuralDeck", "m3")]
    [InlineData("Membrane", "m2")]
    [InlineData("StructuralDeck", "m2")]
    public void EveryFunctionIsStatedInTheUnitsItAllows(string function, string unit)
    {
        Assert.Equal($"on: {function}={unit}", Layers($$"""{ "Floors": { "layers": { "{{function}}": "{{unit}}" } } }"""));
    }

    [Fact]
    public void LeftOutTheCategoryInherits()
    {
        Assert.Null(Single("""{ "Walls": { "threshold": 0.5 } }""").Override.Layers);
    }

    public static TheoryData<string, string, string> Refused() => new()
    {
        { """{ "Walls": { "layers": { "Finish": "m2" } } }""", "\"Finish\"", "Structure, Substrate, Insulation, Finish1, Finish2, Membrane, StructuralDeck" },
        { """{ "Walls": { "layers": { "Structure": "u" } } }""", "\"u\"", "m2 or m3" },
        { """{ "Walls": { "layers": { "Structure": 3 } } }""", "3", "m2 or m3" },
        { """{ "Walls": { "layers": { "Membrane": "m3" } } }""", "\"m3\"", "no thickness" },
        { """{ "Walls": { "layers": { "Structure": "m3", "Structure": "m2" } } }""", "\"Structure\": \"m2\"", "stated twice" },
        { """{ "Walls": { "layers": "yes" } }""", "\"yes\"", "true, false or an object" },
        { """{ "Walls": { "layers": 1 } }""", "1 }", "true, false or an object" },
        { """{ "Walls": { "layers": null } }""", "null", "true, false or an object" },
        { """{ "Walls": { "layers": [] } }""", "[]", "true, false or an object" },
        { """{ "Walls": { "layers": true, "layers": false } }""", "\"layers\": false", "stated twice" },
        { """{ "Walls": { "layers": { "structure": "m2" } } }""", "\"structure\"", "is not a layer function" },
        { """{ "Walls": { "layers": { "Structure": "M3" } } }""", "\"M3\"", "m2 or m3" },
    };

    /// <summary>A list or an object is quoted whole, as it was written, never by its bracket alone.</summary>
    [Theory]
    [InlineData("""{ "Walls": { "layers": [1, 2] } }""", "[1, 2]")]
    [InlineData("""{ "Walls": { "layers": { "Structure": ["m2"] } } }""", "[\"m2\"]")]
    [InlineData("""{ "Walls": { "layers": { "Structure": { "unit": "m2" } } } }""", "{ \"unit\": \"m2\" }")]
    public void AListOrObjectIsQuotedWhole(string text, string written)
    {
        Result<IReadOnlyList<LocatedOverride>, ConfigError> parsed = CriteriaFile.Parse(text);

        Assert.False(parsed.IsOk);
        Assert.Equal(written, parsed.Error.InvalidValue);
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void WhatNoLayerSettingCanBeIsRefusedWhereItIsWritten(string text, string at, string phrase)
    {
        Result<IReadOnlyList<LocatedOverride>, ConfigError> parsed = CriteriaFile.Parse(text);

        Assert.False(parsed.IsOk);
        Assert.Contains(phrase, parsed.Error.Message, StringComparison.Ordinal);
        Assert.Equal("Walls", parsed.Error.Category);
        Assert.Equal(new ConfigLocation(1, text.IndexOf(at, StringComparison.Ordinal) + 1), parsed.Error.Location);
    }

    /// <summary>A counted category cannot be layered: the domain refuses it, at the entry that asks.</summary>
    [Fact]
    public void LayersOnACountedCategoryAreRefusedAtItsEntry()
    {
        string text = """
            {
              "Walls": { "layers": true },
              "Doors": { "layers": true }
            }
            """;

        Result<EffectiveCriteria, ConfigError> resolved = CriteriaResolver.Resolve(CriteriaFileLookup.Found(text), "C:/model/metrado.criteria.json");

        Assert.False(resolved.IsOk);
        Assert.Equal(
            "The criteria file 'C:/model/metrado.criteria.json', line 3, position 3: 'Doors' cannot be taken off by material layer: only walls, floors and roofs have layers.",
            resolved.Error.Message);
    }

    /// <summary>A railing stated in m2 has no layers either, and is refused at its entry, where it was once let through.</summary>
    [Fact]
    public void LayersOnARailingInSquareMetresAreRefusedAtItsEntry()
    {
        Result<EffectiveCriteria, ConfigError> resolved = CriteriaResolver.Resolve(
            CriteriaFileLookup.Found("""{ "Railings": { "unit": "m2", "layers": {} } }"""), "C:/model/metrado.criteria.json");

        Assert.False(resolved.IsOk);
        Assert.Equal(
            "The criteria file 'C:/model/metrado.criteria.json', line 1, position 3: 'Railings' cannot be taken off by material layer: only walls, floors and roofs have layers.",
            resolved.Error.Message);
    }

    private static LocatedOverride Single(string text)
    {
        Result<IReadOnlyList<LocatedOverride>, ConfigError> parsed = CriteriaFile.Parse(text);
        Assert.True(parsed.IsOk, parsed.IsOk ? string.Empty : parsed.Error.Message);
        return Assert.Single(parsed.Value);
    }

    private static string? Layers(string text) =>
        Single(text).Override.Layers?.Match(
            off: () => "off",
            on: units => "on: " + string.Join(", ", units.OrderBy(unit => unit.Key.ToString(), StringComparer.Ordinal).Select(unit => $"{unit.Key}={unit.Value.Symbol()}")));
}

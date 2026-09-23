using Metrado.Domain;

namespace Metrado.Configuration.Tests;

/// <summary>
/// <c>takeoff-configuration</c>, "Saved, Reusable Configurations" (task 3.6):
/// a complete configuration, its criteria, thresholds, material layers and
/// codification setting, is written under a name and read back as it was.
/// The file states every field of every category, so what it reproduces
/// never depends on the built-in criteria it is read over.
/// </summary>
public sealed class SavedConfigurationTests
{
    private static readonly Guid Shared = Guid.Parse("4f46423f-5c26-11d4-9217-0000863f27ad");

    [Fact]
    public void AConfigurationReadsBackAsItWasWritten()
    {
        SavedConfiguration saved = new("Obra \"Los Olivos\" — Piura", Customised(), Shared);

        SavedConfiguration read = Read(SavedConfigurations.Write(saved));

        Assert.Equal((saved.Name, saved.SharedParameter), (read.Name, read.SharedParameter));
        Assert.Equal(Describe(saved.Criteria), Describe(read.Criteria));
    }

    [Fact]
    public void NoSharedParameterIsWrittenAsNullAndReadBackAsNone()
    {
        string text = SavedConfigurations.Write(new SavedConfiguration("A", CriteriaSet.Default, SharedParameter: null));

        Assert.Contains("\"sharedParameter\": null", text, StringComparison.Ordinal);
        Assert.Null(Read(text).SharedParameter);
    }

    /// <summary>Every category states every field, layers too: all seven functions when layered, false otherwise.</summary>
    [Fact]
    public void EveryFieldOfEveryCategoryIsWritten()
    {
        CriteriaFileContent content = CriteriaFile.Read(SavedConfigurations.Write(new SavedConfiguration("A", Customised(), Shared))).Value;

        Assert.Equal(CriteriaSet.Default.ByCategory.Keys.Order(StringComparer.Ordinal), content.Entries.Select(entry => entry.Override.Category));
        Assert.All(content.Entries, entry => Assert.True(
            entry.Override is { Unit: not null, Sources: not null, Threshold: not null, Mode: not null, Layers: not null },
            $"{entry.Override.Category} leaves a field to the built-in criteria."));
        Assert.Equal(
            "Finish1=m2, Finish2=m2, Insulation=m2, Membrane=m2, StructuralDeck=m2, Structure=m3, Substrate=m2",
            content.Entries.Single(entry => entry.Override.Category == "Walls").Override.Layers!.Match(
                off: () => "off",
                on: units => string.Join(", ", units.OrderBy(unit => unit.Key.ToString(), StringComparer.Ordinal).Select(unit => $"{unit.Key}={unit.Value.Symbol()}"))));
        Assert.Equal("off", content.Entries.Single(entry => entry.Override.Category == "Doors").Override.Layers!.Match(off: () => "off", on: _ => "on"));
    }

    [Fact]
    public void AFileThatNamesNoConfigurationIsNotOne()
    {
        Result<SavedConfiguration, ConfigError> read = SavedConfigurations.Read("""{ "Walls": { "threshold": 0.5 } }""");

        Assert.False(read.IsOk);
        Assert.Contains("names no configuration", read.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>A configuration the product would refuse is refused on reading, at the entry that asks.</summary>
    [Fact]
    public void ARefusalIsLocatedAtItsEntry()
    {
        const string text = """{ "$configuration": { "name": "A" }, "Walls": { "unit": "m3", "sources": ["HOST_VOLUME_COMPUTED"], "layers": true } }""";

        Result<SavedConfiguration, ConfigError> read = SavedConfigurations.Read(text);

        Assert.False(read.IsOk);
        Assert.Equal(new ConfigLocation(1, text.IndexOf("\"Walls\"", StringComparison.Ordinal) + 1), read.Error.Location);
    }

    /// <summary>Walls layered with Structure in m3 at an inclusive 0.5 m2; floors layered in m2 at 2 m2; the rest built in.</summary>
    private static CriteriaSet Customised() =>
        CriteriaSet.Merge(
            CriteriaSet.Default,
            [
                new CategoryOverride("Walls", threshold: 0.5, mode: BoundaryMode.Inclusive, layers: LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit> { [LayerFunction.Structure] = QuantityUnit.CubicMetre })),
                new CategoryOverride("Floors", threshold: 2.0, layers: LayerOverride.On(new Dictionary<LayerFunction, QuantityUnit>())),
            ]).Value;

    private static SavedConfiguration Read(string text)
    {
        Result<SavedConfiguration, ConfigError> read = SavedConfigurations.Read(text);
        Assert.True(read.IsOk, read.IsOk ? string.Empty : read.Error.Message);
        return read.Value;
    }

    private static IEnumerable<string> Describe(CriteriaSet criteria) =>
        criteria.ByCategory.Values.OrderBy(criterion => criterion.Category, StringComparer.Ordinal).Select(criterion =>
            $"{criterion.Category} {criterion.Unit} [{string.Join(",", criterion.Sources)}] {criterion.Threshold} {criterion.Layers?.ToString() ?? "whole"}");
}

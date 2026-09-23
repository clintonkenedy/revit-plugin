using Metrado.Domain;

namespace Metrado.Configuration.Tests;

/// <summary>
/// Pins how a criteria file is read: JSON with comments and trailing commas,
/// one entry per category, every field optional and inherited when left out,
/// and every mistake refused with the place in the file where it is.
/// <c>takeoff-configuration</c>: "Invalid Configuration Fails Loudly", because
/// a silently ignored entry prices a budget under criteria nobody chose.
/// </summary>
public sealed class CriteriaFileTests
{
    [Fact]
    public void AnEntryStatesOnlyTheFieldsItSets()
    {
        CategoryOverride entry = Assert.Single(Parsed("""{ "Walls": { "threshold": 0.5 } }""")).Override;

        Assert.Equal("Walls", entry.Category);
        Assert.Equal(0.5, entry.Threshold);
        Assert.Null(entry.Unit);
        Assert.Null(entry.Sources);
        Assert.Null(entry.Mode);
    }

    [Fact]
    public void EveryFieldIsRead()
    {
        CategoryOverride entry = Assert.Single(Parsed(
            """{ "Walls": { "unit": "m2", "sources": ["HOST_AREA_COMPUTED", "Area"], "threshold": 1.5, "mode": "inclusive" } }""")).Override;

        Assert.Equal(QuantityUnit.SquareMetre, entry.Unit);
        Assert.Equal(["HOST_AREA_COMPUTED", "Area"], entry.Sources);
        Assert.Equal(1.5, entry.Threshold);
        Assert.Equal(BoundaryMode.Inclusive, entry.Mode);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreAccepted()
    {
        const string Text = """
            // Criteria for the Office project.
            {
              /* walls: small openings stay in */
              "Walls": { "threshold": 0.5, "mode": "exclusive", },
            }
            """;

        Assert.Equal(0.5, Assert.Single(Parsed(Text)).Override.Threshold);
    }

    /// <summary>An empty list declares a category measured by no source (counted in I2); it is never read as "left out".</summary>
    [Fact]
    public void AnEmptySourcesListIsKeptAsAChoice()
    {
        IReadOnlyList<string>? sources = Assert.Single(Parsed("""{ "Walls": { "sources": [] } }""")).Override.Sources;

        Assert.NotNull(sources);
        Assert.Empty(sources);
    }

    /// <summary>Lines and positions count from 1, as an editor shows them.</summary>
    [Fact]
    public void EachEntryRemembersWhereItIsWritten()
    {
        LocatedOverride entry = Assert.Single(Parsed("{\n  \"Walls\": { \"threshold\": 0.5 }\n}"));

        Assert.Equal(new ConfigLocation(2, 3), entry.Location);
    }

    [Fact]
    public void MalformedSyntaxStopsWithItsLineAndPosition()
    {
        ConfigError error = Refused("{\n  \"Walls\": { \"threshold\": }\n}");

        Assert.Equal(new ConfigLocation(2, 27), error.Location);
        Assert.Contains("not valid JSON", error.Message);
        Assert.DoesNotContain("LineNumber", error.Message);
    }

    /// <summary>
    /// Positions count characters, as an editor shows them: an accented letter
    /// earlier on the line is one position, not the two bytes UTF-8 gives it.
    /// </summary>
    [Fact]
    public void PositionsCountCharactersNotBytes()
    {
        Assert.Equal(new ConfigLocation(1, 35), Refused("""{ /* vanos pequeños */ "Walls": { "treshold": 1 } }""").Location);
        Assert.Equal(new ConfigLocation(1, 35), Refused("""{ /* ñ */ "Walls": { "threshold": } }""").Location);
    }

    /// <summary>
    /// A \u escape for half a surrogate pair passes the reader and fails only
    /// when the text is decoded; it must still stop the run with a location,
    /// never escape as an exception Revit shows without the file's name.
    /// </summary>
    [Theory]
    [InlineData("""{ "\uD800": {} }""")]
    [InlineData("""{ "Walls": { "\uDC00": 1 } }""")]
    [InlineData("""{ "Walls": { "mode": "\uD800" } }""")]
    [InlineData("""{ "Walls": { "unit": "\uD800" } }""")]
    [InlineData("""{ "Walls": { "threshold": "\uD800" } }""")]
    [InlineData("""{ "Walls": { "sources": ["\uD800"] } }""")]
    public void AnEscapedHalfSurrogateIsRefusedWithItsPlace(string text)
    {
        ConfigError error = Refused(text);

        Assert.Equal(1, error.Location?.Line);
        Assert.Contains("\\u", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("\"Walls\"")]
    public void ATextThatIsNotAnObjectOfCategoriesIsRefused(string text)
    {
        Assert.NotNull(Refused(text).Location);
    }

    /// <summary>A second object pasted after the first is not silently dropped.</summary>
    [Fact]
    public void TextAfterTheClosingBraceIsRefused()
    {
        Assert.Contains("not valid JSON", Refused("""{ "Walls": {} } { "Walls": { "threshold": 2 } }""").Message);
    }

    [Fact]
    public void AnEntryThatIsNotAnObjectIsRefusedNamingItsCategory()
    {
        ConfigError error = Refused("""{ "Walls": 0.5 }""");

        Assert.Equal("Walls", error.Category);
        Assert.Equal(new ConfigLocation(1, 12), error.Location);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AnEntryWithNoCategoryNameIsRefused(string name)
    {
        Assert.Contains("no category name", Refused($$"""{ "{{name}}": { "threshold": 0.5 } }""").Message);
    }

    [Fact]
    public void AnUnsupportedUnitIsRefusedListingTheAcceptedOnes()
    {
        ConfigError error = Refused("""{ "Walls": { "unit": "ft2" } }""");

        Assert.Equal("Walls", error.Category);
        Assert.Equal("ft2", error.InvalidValue);
        Assert.Contains("Accepted units: m2, m3, u, m.", error.Message);
        Assert.Equal(new ConfigLocation(1, 22), error.Location);
    }

    /// <summary>The domain is closed and spelled exactly: "Inclusive" is not "inclusive".</summary>
    [Theory]
    [InlineData("less-than")]
    [InlineData("Inclusive")]
    public void AnInvalidModeIsRefusedListingBothAcceptedValues(string mode)
    {
        ConfigError error = Refused($$"""{ "Walls": { "mode": "{{mode}}" } }""");

        Assert.Equal("Walls", error.Category);
        Assert.Equal(mode, error.InvalidValue);
        Assert.Contains("exclusive", error.Message);
        Assert.Contains("inclusive", error.Message);
    }

    /// <summary>A misspelt field ignored would leave its category on the default without a word.</summary>
    [Fact]
    public void AnUnknownFieldIsRefusedRatherThanIgnored()
    {
        ConfigError error = Refused("""{ "Walls": { "treshold": 0.5 } }""");

        Assert.Equal("treshold", error.InvalidValue);
        Assert.Contains("unit, sources, threshold, mode", error.Message);
        Assert.Equal(new ConfigLocation(1, 14), error.Location);
    }

    [Fact]
    public void AFieldStatedTwiceIsRefused()
    {
        ConfigError error = Refused("""{ "Walls": { "threshold": 0.5, "threshold": 2 } }""");

        Assert.Equal("threshold", error.InvalidValue);
        Assert.Equal(new ConfigLocation(1, 32), error.Location);
    }

    [Theory]
    [InlineData("threshold", "\"1\"")]
    [InlineData("threshold", "1e400")]
    [InlineData("unit", "2")]
    [InlineData("mode", "true")]
    [InlineData("sources", "\"Area\"")]
    [InlineData("sources", "[1]")]
    [InlineData("sources", "[\"  \"]")]
    [InlineData("threshold", "null")]
    [InlineData("unit", "null")]
    [InlineData("mode", "null")]
    [InlineData("sources", "null")]
    [InlineData("sources", "[null]")]
    public void AFieldOfTheWrongKindIsRefusedNamingIt(string field, string value)
    {
        ConfigError error = Refused($$"""{ "Walls": { "{{field}}": {{value}} } }""");

        Assert.Equal("Walls", error.Category);
        Assert.Contains($"'{field}'", error.Message);
    }

    /// <summary>What only the product's own criteria can judge is left to them, and passed on as written.</summary>
    [Fact]
    public void ValuesTheDomainJudgesArePassedOnAsWritten()
    {
        IReadOnlyList<LocatedOverride> entries = Parsed("""{ "Floors": { "threshold": -1 }, "Floors": {} }""");

        Assert.Equal(["Floors", "Floors"], entries.Select(entry => entry.Override.Category));
        Assert.Equal(-1, entries[0].Override.Threshold);
    }

    private static IReadOnlyList<LocatedOverride> Parsed(string text)
    {
        Result<IReadOnlyList<LocatedOverride>, ConfigError> parsed = CriteriaFile.Parse(text);
        Assert.True(parsed.IsOk, parsed.IsOk ? null : parsed.Error.Message);
        return parsed.Value;
    }

    private static ConfigError Refused(string text)
    {
        Result<IReadOnlyList<LocatedOverride>, ConfigError> parsed = CriteriaFile.Parse(text);
        Assert.False(parsed.IsOk, "The text was accepted.");
        return parsed.Error;
    }
}

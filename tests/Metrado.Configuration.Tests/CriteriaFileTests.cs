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

        Assert.Equal(2, error.Location?.Line);
        Assert.Contains("not valid JSON", error.Message);
        Assert.DoesNotContain("LineNumber", error.Message);
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

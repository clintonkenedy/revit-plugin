using Metrado.Domain;

namespace Metrado.Configuration.Tests;

/// <summary>
/// Pins the header by which a saved configuration names itself and sets its
/// codification (task 3.6): a reserved <c>"$configuration"</c> entry, which no
/// category can be named, with a name and the nominated shared parameter's
/// GUID. Nothing in it is ignored, each refusal names its place, and a
/// criteria file beside the model refuses it until a configuration can be
/// picked (3.7).
/// </summary>
public sealed class ConfigurationHeaderTests
{
    private const string Guid = "4f46423f-5c26-11d4-9217-0000863f27ad";

    [Fact]
    public void AConfigurationNamesItselfAndNominatesASharedParameter()
    {
        CriteriaFileContent content = Read($$"""{ "$configuration": { "name": "Obra Los Olivos", "sharedParameter": "{{Guid}}" }, "Walls": { "threshold": 0.5 } }""");

        Assert.Equal(new ConfigurationHeader("Obra Los Olivos", System.Guid.Parse(Guid)), content.Configuration);
        Assert.Equal("Walls", Assert.Single(content.Entries).Override.Category);
    }

    [Fact]
    public void ItsSharedParameterMayBeNoneOrLeftOut()
    {
        Assert.Null(Read("""{ "$configuration": { "name": "A", "sharedParameter": null } }""").Configuration!.SharedParameter);
        Assert.Null(Read("""{ "$configuration": { "name": "A" } }""").Configuration!.SharedParameter);
    }

    [Fact]
    public void AFileWithoutOneHasNone()
    {
        CriteriaFileContent content = Read("""{ "Walls": { "threshold": 0.5 } }""");

        Assert.Null(content.Configuration);
        Assert.Single(content.Entries);
    }

    public static TheoryData<string, string, string> Refused() => new()
    {
        { """{ "$configuration": "Obra" }""", "\"Obra\"", "must be an object" },
        { """{ "$configuration": { "sharedParameter": null } }""", "\"$configuration\"", "has no \"name\"" },
        { """{ "$configuration": { "name": "  " } }""", "\"  \"", "must be a text naming" },
        { """{ "$configuration": { "name": 3 } }""", "3 }", "must be a text naming" },
        { """{ "$configuration": { "name": "A", "sharedParameter": "shared" } }""", "\"shared\"", "the shared parameter's GUID" },
        { """{ "$configuration": { "name": "A", "sharedParameter": 7 } }""", "7 }", "the shared parameter's GUID" },
        { """{ "$configuration": { "name": "A", "owner": "B" } }""", "\"owner\"", "is not a field of '$configuration'" },
        { """{ "$configuration": { "name": "A", "name": "B" } }""", "\"name\": \"B\"", "stated twice" },
        { """{ "$configuration": { "name": "A" }, "$configuration": { "name": "B" } }""", "\"$configuration\": { \"name\": \"B\"", "stated twice" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void AHeaderThatCannotBeHonouredIsRefusedWhereItIsWritten(string text, string at, string phrase)
    {
        Result<CriteriaFileContent, ConfigError> read = CriteriaFile.Read(text);

        Assert.False(read.IsOk);
        Assert.Contains(phrase, read.Error.Message, StringComparison.Ordinal);
        Assert.Equal(new ConfigLocation(1, text.IndexOf(at, StringComparison.Ordinal) + 1), read.Error.Location);
    }

    /// <summary>Beside the model, a header would be read and then ignored: it is refused, at its key, until a configuration can be picked.</summary>
    [Fact]
    public void ACriteriaFileBesideTheModelRefusesAHeader()
    {
        const string text = """{ "Walls": { "threshold": 0.5 }, "$configuration": { "name": "A" } }""";

        Result<IReadOnlyList<LocatedOverride>, ConfigError> parsed = CriteriaFile.Parse(text);

        Assert.False(parsed.IsOk);
        Assert.Contains("names a saved configuration", parsed.Error.Message, StringComparison.Ordinal);
        Assert.Equal(new ConfigLocation(1, text.IndexOf("\"$configuration\"", StringComparison.Ordinal) + 1), parsed.Error.Location);
    }

    private static CriteriaFileContent Read(string text)
    {
        Result<CriteriaFileContent, ConfigError> read = CriteriaFile.Read(text);
        Assert.True(read.IsOk, read.IsOk ? string.Empty : read.Error.Message);
        return read.Value;
    }
}

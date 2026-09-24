using Metrado.Domain;

namespace Metrado.Configuration.Tests;

/// <summary>
/// Pins the header by which a saved configuration names itself and sets its
/// codification (task 3.6): a reserved <c>"$configuration"</c> entry, which no
/// category can be named, with a name and the nominated shared parameter's
/// GUID. Nothing in it is ignored and each refusal names its place. A
/// configuration picked in Revit is copied beside the model (3.7), so the
/// criteria file there carries the header into the criteria in force.
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

    public static TheoryData<string, string, string, string?> Refused() => new()
    {
        { """{ "$configuration": "Obra" }""", "\"Obra\"", "must be an object", "Obra" },
        { """{ "$configuration": { "sharedParameter": null } }""", "\"$configuration\"", "has no \"name\"", null },
        { """{ "$configuration": { "name": "  " } }""", "\"  \"", "must be a text naming", "  " },
        { """{ "$configuration": { "name": 3 } }""", "3 }", "must be a text naming", "3" },
        { """{ "$configuration": { "name": "A", "sharedParameter": "shared" } }""", "\"shared\"", "the shared parameter's GUID", "shared" },
        { """{ "$configuration": { "name": "A", "sharedParameter": 7 } }""", "7 }", "the shared parameter's GUID", "7" },
        { """{ "$configuration": { "name": "A", "owner": "B" } }""", "\"owner\"", "is not a field of '$configuration'", "owner" },
        { """{ "$configuration": { "name": "A", "name": "B" } }""", "\"name\": \"B\"", "stated twice", "name" },
        { """{ "$configuration": { "name": "A" }, "$configuration": { "name": "B" } }""", "\"$configuration\": { \"name\": \"B\"", "stated twice", null },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void AHeaderThatCannotBeHonouredIsRefusedWhereItIsWritten(string text, string at, string phrase, string? written)
    {
        Result<CriteriaFileContent, ConfigError> read = CriteriaFile.Read(text);

        Assert.False(read.IsOk);
        Assert.Contains(phrase, read.Error.Message, StringComparison.Ordinal);
        Assert.Equal(new ConfigLocation(1, text.IndexOf(at, StringComparison.Ordinal) + 1), read.Error.Location);
        Assert.Equal(written, read.Error.InvalidValue);
    }

    /// <summary>Beside the model the header is in force: the run knows the configuration's name and reads its shared parameter.</summary>
    [Fact]
    public void ACriteriaFileBesideTheModelCarriesItsConfiguration()
    {
        EffectiveCriteria criteria = CriteriaResolver.Resolve(
            CriteriaFileLookup.Found($$"""{ "$configuration": { "name": "Obra Los Olivos", "sharedParameter": "{{Guid}}" }, "Walls": { "threshold": 0.5 } }"""),
            "C:/model/metrado.criteria.json").Value;

        Assert.Equal(("Obra Los Olivos", System.Guid.Parse(Guid)), (criteria.ConfigurationName, criteria.SharedParameter));
        Assert.Equal(0.5, criteria.Criteria.ByCategory["Walls"].Threshold.Value);
    }

    [Fact]
    public void ACriteriaFileWithoutAHeaderNamesNoConfiguration()
    {
        EffectiveCriteria criteria = CriteriaResolver.Resolve(CriteriaFileLookup.Found("""{ "Walls": { "threshold": 0.5 } }"""), "C:/model/metrado.criteria.json").Value;

        Assert.Equal(((string?)null, (Guid?)null), (criteria.ConfigurationName, criteria.SharedParameter));
    }

    private static CriteriaFileContent Read(string text)
    {
        Result<CriteriaFileContent, ConfigError> read = CriteriaFile.Read(text);
        Assert.True(read.IsOk, read.IsOk ? string.Empty : read.Error.Message);
        return read.Value;
    }
}

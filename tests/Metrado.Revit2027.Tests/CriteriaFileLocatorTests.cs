using Metrado.Domain;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Pins where the criteria file is looked for — beside the model, as
/// <c>metrado.criteria.json</c> — and the three answers the locator gives.
///
/// The three states exist so that a file which is there but cannot be read
/// never collapses into "no file": absence falls back to the defaults, while a
/// supplied file that cannot be honoured must stop the run (residual finding N3).
/// </summary>
public sealed class CriteriaFileLocatorTests : IDisposable
{
    private readonly DirectoryInfo _project = Directory.CreateTempSubdirectory("metrado-project-");

    public void Dispose() => _project.Delete(recursive: true);

    private string ModelPath => Path.Combine(_project.FullName, "Office Building.rvt");

    private string CriteriaPath => Path.Combine(_project.FullName, "metrado.criteria.json");

    [Fact]
    public void TheFileIsLookedForBesideTheModel()
    {
        (_, string? probed) = CriteriaFileLocator.Locate(ModelPath);

        Assert.Equal(CriteriaPath, probed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnsavedModelHasNowhereToLookAndUsesTheDefaults(string? modelPath)
    {
        (CriteriaFileLookup lookup, string? probed) = CriteriaFileLocator.Locate(modelPath);

        Assert.Equal("absent", State(lookup));
        Assert.Null(probed);
    }

    /// <summary>A cloud model's path names no folder on this machine to look in.</summary>
    [Theory]
    [InlineData("BIM 360://Project/Office Building.rvt")]
    [InlineData("Office Building.rvt")]
    public void AModelPathThatNamesNoLocalFolderUsesTheDefaults(string modelPath)
    {
        (CriteriaFileLookup lookup, string? probed) = CriteriaFileLocator.Locate(modelPath);

        Assert.Equal("absent", State(lookup));
        Assert.Null(probed);
    }

    [Fact]
    public void NoFileBesideTheModelIsAbsent()
    {
        (CriteriaFileLookup lookup, _) = CriteriaFileLocator.Locate(ModelPath);

        Assert.Equal("absent", State(lookup));
    }

    [Theory]
    [InlineData("{ \"Walls\": { \"threshold\": 0.5 } }")]
    [InlineData("")]
    [InlineData("  \n{}\n  ")]
    public void AFileBesideTheModelIsFoundWithItsTextAsWritten(string text)
    {
        File.WriteAllText(CriteriaPath, text);

        (CriteriaFileLookup lookup, _) = CriteriaFileLocator.Locate(ModelPath);

        Assert.Equal("found:" + text, State(lookup));
    }

    /// <summary>
    /// A file held open by another program is there and was supplied: reading
    /// it failed, so the run must stop naming it, not fall back to defaults.
    /// </summary>
    [Fact]
    public void ALockedFileIsUnreadableAndNamesTheFile()
    {
        File.WriteAllText(CriteriaPath, "{}");
        using FileStream held = new(CriteriaPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        (CriteriaFileLookup lookup, _) = CriteriaFileLocator.Locate(ModelPath);

        ConfigError error = Unreadable(lookup);
        Assert.Equal(CriteriaPath, error.FilePath);
        Assert.Contains(CriteriaPath, error.Message);
    }

    /// <summary>A folder with the file's name is something there, not nothing: it is never "absent".</summary>
    [Fact]
    public void AFolderWithTheFilesNameIsUnreadableNotAbsent()
    {
        Directory.CreateDirectory(CriteriaPath);

        (CriteriaFileLookup lookup, _) = CriteriaFileLocator.Locate(ModelPath);

        ConfigError error = Unreadable(lookup);
        Assert.Equal(CriteriaPath, error.FilePath);
        Assert.Contains("folder", error.Message);
    }

    private static string State(CriteriaFileLookup lookup) =>
        lookup.Match(found: text => "found:" + text, absent: () => "absent", unreadable: error => "unreadable:" + error.Message);

    private static ConfigError Unreadable(CriteriaFileLookup lookup) =>
        lookup.Match<ConfigError?>(found: _ => null, absent: () => null, unreadable: error => error)
            ?? throw new Xunit.Sdk.XunitException($"Expected unreadable, got {State(lookup)}.");
}

using Metrado.Configuration;
using Metrado.Domain;

namespace Metrado.Revit2027.Tests;

/// <summary>
/// Task 3.7's files, as the user decided: a configuration picked in Revit
/// is copied beside the model as its criteria file, never over one without
/// consent, and never unless it reads as a configuration; the criteria in
/// force are saved where the estimator chooses, named after the file.
/// </summary>
public sealed class ConfigurationFilesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("metrado-config-").FullName;

    private string Model => Path.Combine(_folder, "Office.rvt");

    private string CriteriaFile => Path.Combine(_folder, CriteriaFileLocator.FileName);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void ALoadedConfigurationBecomesTheModelsCriteriaFile()
    {
        string saved = Saved("Obra Los Olivos", threshold: 0.5);

        LoadOutcome outcome = ConfigurationFiles.Load(saved, Model, replace: false);

        Assert.Equal(LoadStatus.Loaded, outcome.Status);
        Assert.Contains("'Obra Los Olivos' is now the criteria file beside the model", outcome.Message, StringComparison.Ordinal);
        EffectiveCriteria criteria = CriteriaResolver.Resolve(CriteriaFileLookup.Found(File.ReadAllText(CriteriaFile)), CriteriaFile).Value;
        Assert.Equal(("Obra Los Olivos", 0.5), (criteria.ConfigurationName, criteria.Criteria.ByCategory["Walls"].Threshold.Value));
    }

    /// <summary>The model's own criteria file is replaced only with consent; asked, nothing is written.</summary>
    [Fact]
    public void AnExistingCriteriaFileIsReplacedOnlyWithConsent()
    {
        File.WriteAllText(CriteriaFile, """{ "Walls": { "threshold": 2.0 } }""");
        string saved = Saved("Obra", threshold: 0.5);

        LoadOutcome asked = ConfigurationFiles.Load(saved, Model, replace: false);

        Assert.Equal(LoadStatus.NeedsConsent, asked.Status);
        Assert.Contains(CriteriaFile, asked.Message, StringComparison.Ordinal);
        Assert.Equal("""{ "Walls": { "threshold": 2.0 } }""", File.ReadAllText(CriteriaFile));

        Assert.Equal(LoadStatus.Loaded, ConfigurationFiles.Load(saved, Model, replace: true).Status);
        Assert.Equal(File.ReadAllText(saved), File.ReadAllText(CriteriaFile));
    }

    /// <summary>A file that is no configuration is refused, located, and nothing beside the model changes.</summary>
    [Fact]
    public void AFileThatIsNoConfigurationIsRefusedAndNothingIsWritten()
    {
        string plain = Path.Combine(_folder, "plain.json");
        File.WriteAllText(plain, """{ "Walls": { "threshold": 0.5 } }""");

        LoadOutcome outcome = ConfigurationFiles.Load(plain, Model, replace: true);

        Assert.Equal(LoadStatus.Refused, outcome.Status);
        Assert.Contains("is not a saved configuration Metrado can load", outcome.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(CriteriaFile));
    }

    [Fact]
    public void ARefusalNamesThePlaceInTheFile()
    {
        string bad = Path.Combine(_folder, "bad.json");
        File.WriteAllText(bad, """{ "$configuration": { "name": "A" }, "Walls": { "threshold": -1 } }""");

        Assert.Contains(", line 1, position ", ConfigurationFiles.Load(bad, Model, replace: true).Message, StringComparison.Ordinal);
    }

    /// <summary>The criteria in force are saved under the file's name, and read back as they were.</summary>
    [Fact]
    public void TheCriteriaInForceAreSavedUnderTheFilesName()
    {
        EffectiveCriteria inForce = new(
            CriteriaSet.Merge(CriteriaSet.Default, [new CategoryOverride("Walls", threshold: 0.5)]).Value,
            ConfigSource.File,
            CriteriaFile) { SharedParameter = Guid.Parse("4f46423f-5c26-11d4-9217-0000863f27ad") };
        string target = Path.Combine(_folder, "Obra Los Olivos.json");

        string name = ConfigurationFiles.Save(inForce, target);

        SavedConfiguration read = SavedConfigurations.Read(File.ReadAllText(target)).Value;
        Assert.Equal(("Obra Los Olivos", "Obra Los Olivos", inForce.SharedParameter), (name, read.Name, read.SharedParameter));
        Assert.Equal(0.5, read.Criteria.ByCategory["Walls"].Threshold.Value);
        Assert.False(File.Exists(target + ".partial"));
    }

    private string Saved(string name, double threshold)
    {
        string path = Path.Combine(_folder, $"{name}.json");
        File.WriteAllText(path, SavedConfigurations.Write(new SavedConfiguration(
            name, CriteriaSet.Merge(CriteriaSet.Default, [new CategoryOverride("Walls", threshold: threshold)]).Value, SharedParameter: null)));
        return path;
    }
}

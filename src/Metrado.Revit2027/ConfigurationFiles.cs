using Metrado.Configuration;
using Metrado.Domain;

namespace Metrado.Revit2027;

/// <summary>What loading a configuration came to, and what to tell the estimator.</summary>
public enum LoadStatus
{
    /// <summary>It is now the model's criteria file.</summary>
    Loaded = 1,

    /// <summary>The model already has a criteria file, which is replaced only with the estimator's consent.</summary>
    NeedsConsent = 2,

    /// <summary>The file is no configuration Metrado can load; nothing was written.</summary>
    Refused = 3,
}

/// <param name="Status">What happened.</param>
/// <param name="Message">What to tell the estimator.</param>
public sealed record LoadOutcome(LoadStatus Status, string Message);

/// <summary>
/// Loads a saved configuration beside the model and saves the criteria in
/// force as one (task 3.7), with no Revit type: the commands only open
/// Revit's file dialogs and ask for consent.
/// </summary>
/// <remarks>
/// Decided by the user: a picked configuration is copied beside the model as
/// its criteria file, so the choice stays with the model and every export of
/// it uses it; a configuration is saved where the estimator chooses, its name
/// the file's.
/// </remarks>
public static class ConfigurationFiles
{
    /// <summary>
    /// Copies the configuration at <paramref name="configurationPath"/> beside
    /// the model as its criteria file, after reading it as a configuration;
    /// an existing criteria file is replaced only when <paramref name="replace"/>.
    /// </summary>
    public static LoadOutcome Load(string configurationPath, string modelPath, bool replace)
    {
        ArgumentNullException.ThrowIfNull(configurationPath);
        ArgumentNullException.ThrowIfNull(modelPath);

        string text;
        try
        {
            text = File.ReadAllText(configurationPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new LoadOutcome(LoadStatus.Refused, $"'{configurationPath}' could not be read: {ex.Message}");
        }

        Result<SavedConfiguration, ConfigError> read = SavedConfigurations.Read(text);
        if (!read.IsOk)
        {
            string place = read.Error.Location is ConfigLocation at ? $", line {at.Line}, position {at.Position}" : string.Empty;
            return new LoadOutcome(LoadStatus.Refused, $"'{configurationPath}' is not a saved configuration Metrado can load{place}: {read.Error.Message}");
        }

        string criteriaPath = Path.Combine(Path.GetDirectoryName(modelPath)!, CriteriaFileLocator.FileName);
        string name = read.Value.Name;
        if (File.Exists(criteriaPath) && !replace)
        {
            return new LoadOutcome(LoadStatus.NeedsConsent, $"The model already has a criteria file, '{criteriaPath}'. Replace it with the configuration '{name}'?");
        }

        Replace(criteriaPath, text);
        return new LoadOutcome(LoadStatus.Loaded, $"The configuration '{name}' is now the criteria file beside the model, '{criteriaPath}': every export of this model uses it.");
    }

    /// <summary>
    /// Saves the criteria in force as a configuration named after the file
    /// chosen, every field of every category stated.
    /// </summary>
    /// <returns>The configuration's name.</returns>
    public static string Save(EffectiveCriteria criteria, string targetPath)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(targetPath);

        string name = Path.GetFileNameWithoutExtension(targetPath);
        Replace(targetPath, SavedConfigurations.Write(new SavedConfiguration(name, criteria.Criteria, criteria.SharedParameter)));
        return name;
    }

    /// <summary>Writes beside the target first and renames over it, so a failure never leaves half a file.</summary>
    private static void Replace(string path, string text)
    {
        string partial = path + ".partial";
        File.WriteAllText(partial, text);
        File.Move(partial, path, overwrite: true);
    }
}

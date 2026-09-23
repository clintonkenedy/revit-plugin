using Metrado.Domain;

namespace Metrado.Revit2027;

/// <summary>
/// Finds the criteria file beside the model: <c>metrado.criteria.json</c> in
/// the model's folder, so the criteria travel and are versioned with the
/// project they price.
///
/// It answers in three states, never two. A file that is there but cannot be
/// read — held open, refused by permissions, a folder under the file's name —
/// is <see cref="CriteriaFileLookup.Unreadable"/>, naming the file, and stops
/// the run; only nothing-there is <see cref="CriteriaFileLookup.Absent"/>, which
/// falls back to the defaults (residual finding N3). Uses no Revit type.
/// </summary>
public static class CriteriaFileLocator
{
    public const string FileName = "metrado.criteria.json";

    /// <param name="modelPath">The model's path; empty for a model never saved.</param>
    /// <returns>
    /// The lookup, and the path probed — null when the model names no local
    /// folder to look in (never saved, or a cloud model).
    /// </returns>
    public static (CriteriaFileLookup Lookup, string? Path) Locate(string? modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath)
            || !Path.IsPathFullyQualified(modelPath)
            || Path.GetDirectoryName(modelPath) is not { Length: > 0 } folder)
        {
            return (CriteriaFileLookup.Absent, null);
        }

        string path = Path.Combine(folder, FileName);

        if (Directory.Exists(path))
        {
            return (Unreadable(path, "it is a folder, not a file"), path);
        }

        if (!File.Exists(path))
        {
            return (CriteriaFileLookup.Absent, path);
        }

        try
        {
            return (CriteriaFileLookup.Found(File.ReadAllText(path)), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (Unreadable(path, ex.Message), path);
        }
    }

    private static CriteriaFileLookup Unreadable(string path, string reason) =>
        CriteriaFileLookup.Unreadable(new ConfigError($"The criteria file '{path}' could not be read: {reason}")
        {
            FilePath = path,
        });
}

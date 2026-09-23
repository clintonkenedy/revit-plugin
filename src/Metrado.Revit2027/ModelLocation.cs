namespace Metrado.Revit2027;

/// <summary>
/// Which model the export works beside: the workbook is written, and the
/// criteria file looked for, in that model's folder. For a workshared local
/// copy it is the central model, the file the team shares; the local copy is
/// per user, and a budget priced beside one estimator's copy would differ
/// from everyone else's. When there is no folder on disk to work in, it says
/// why and what to do instead. Uses no Revit type.
/// </summary>
/// <param name="Path">The model worked beside, in a folder on disk; null when there is none.</param>
/// <param name="Refusal">Why nothing can be written, for the estimator; null when <paramref name="Path"/> is set.</param>
public sealed record ModelLocation(string? Path, string? Refusal)
{
    private const string CopyToAFolder =
        "so there is no folder beside it for the workbook or the criteria file. Save a copy of the model to a local or network folder and export from that copy.";

    /// <param name="pathName">The document's own path; empty for a model never saved.</param>
    /// <param name="centralPath">The central model's path for a workshared local copy; otherwise null.</param>
    /// <param name="isInCloud">Whether the document is stored on Autodesk cloud services.</param>
    public static ModelLocation Of(string? pathName, string? centralPath, bool isInCloud)
    {
        if (isInCloud)
        {
            return new ModelLocation(null, $"This model is stored in the cloud, {CopyToAFolder}");
        }

        bool shared = !string.IsNullOrWhiteSpace(centralPath);
        string? path = shared ? centralPath : pathName;
        if (string.IsNullOrWhiteSpace(path))
        {
            return new ModelLocation(null, "Save the model first. The workbook is written beside it, and the criteria file is looked for there.");
        }

        if (!System.IO.Path.IsPathFullyQualified(path) || System.IO.Path.GetDirectoryName(path) is not { Length: > 0 })
        {
            return new ModelLocation(null, $"{(shared ? "The central model" : "The model")} is at {path}, not in a folder on disk, {CopyToAFolder}");
        }

        return new ModelLocation(path, null);
    }
}

namespace Metrado.Revit2027;

/// <summary>
/// Which model the export works beside: the workbook is written, and the
/// criteria file looked for, in that model's folder. It is the file the
/// estimator opened, a workshared local copy included. The central's path is
/// recorded inside the file and travels with every copy, so it can name a
/// folder the estimator never chose (a model received from another firm),
/// one that cannot be reached (a local copy opened offline), or one every
/// copy's export would race for in the same minute. When there is no folder
/// on disk to work in, it says why and what to do instead. Uses no Revit type.
/// </summary>
/// <param name="Path">The model worked beside, in a folder on disk; null when there is none.</param>
/// <param name="Refusal">Why nothing can be written, for the estimator; null when <paramref name="Path"/> is set.</param>
public sealed record ModelLocation(string? Path, string? Refusal)
{
    private const string CopyToAFolder =
        "so there is no folder beside it for the workbook or the criteria file. Save a copy of the model to a local or network folder (a workshared model is opened detached from its central first) and export from that copy.";

    /// <param name="pathName">The document's own path; empty for a model never saved.</param>
    /// <param name="isInCloud">Whether the document is stored on Autodesk cloud services.</param>
    public static ModelLocation Of(string? pathName, bool isInCloud)
    {
        if (isInCloud)
        {
            return new ModelLocation(null, $"This model is stored in the cloud, {CopyToAFolder}");
        }

        if (string.IsNullOrWhiteSpace(pathName))
        {
            return new ModelLocation(null, "Save the model first. The workbook is written beside it, and the criteria file is looked for there.");
        }

        if (!System.IO.Path.IsPathFullyQualified(pathName) || System.IO.Path.GetDirectoryName(pathName) is not { Length: > 0 })
        {
            return new ModelLocation(null, $"The model is at {pathName}, not in a folder on disk, {CopyToAFolder}");
        }

        return new ModelLocation(pathName, null);
    }
}

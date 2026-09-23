using System.Globalization;

namespace Metrado.Revit2027;

/// <summary>
/// Where an export's workbook goes: beside the model, named for it and for the
/// export's date and time, and never onto a file that already exists.
/// Estimators fill unit prices into an exported workbook; replacing one would
/// destroy their work. Uses no Revit type.
/// </summary>
public static class WorkbookPath
{
    /// <param name="modelPath">The model's path; empty for a model never saved.</param>
    /// <param name="now">The export's local time.</param>
    /// <param name="exists">Whether a file is already at a path.</param>
    /// <returns>Null when the model names no local folder to write beside.</returns>
    public static string? For(string? modelPath, DateTime now, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);

        if (string.IsNullOrWhiteSpace(modelPath)
            || !Path.IsPathFullyQualified(modelPath)
            || Path.GetDirectoryName(modelPath) is not { Length: > 0 } folder)
        {
            return null;
        }

        // Invariant: the Gregorian date and Latin digits whatever the locale.
        string stem = string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileNameWithoutExtension(modelPath)} - metrado {now:yyyy-MM-dd HHmm}");

        // A name is free only if its warnings list is free too: the pair is
        // never split across two exports.
        string candidate = Path.Combine(folder, stem + ".xlsx");
        for (int copy = 2; exists(candidate) || exists(WarningsFor(candidate)); copy++)
        {
            candidate = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{stem} ({copy}).xlsx"));
        }

        return candidate;
    }

    /// <summary>Where a workbook's warnings list goes: beside it, under its name.</summary>
    public static string WarningsFor(string workbookPath) =>
        Path.Combine(
            Path.GetDirectoryName(workbookPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(workbookPath) + " - warnings.txt");
}

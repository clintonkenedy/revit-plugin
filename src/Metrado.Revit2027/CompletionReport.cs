using System.Globalization;
using System.Text;
using Metrado.Domain;

namespace Metrado.Revit2027;

/// <summary>
/// What the completion dialog tells the estimator. The spec requires the run
/// to be understood without opening the workbook: the exported total, the
/// unclassified count, the criteria in force with thresholds and modes,
/// whether a criteria file was found, and the warnings. Uses no Revit type.
/// </summary>
public static class CompletionReport
{
    /// <param name="Summary">The dialog's main text.</param>
    /// <param name="Details">Every warning, for the dialog's expandable part.</param>
    /// <param name="WarningsList">
    /// The summary and every warning, for the file kept beside the workbook;
    /// null when the run raised no warning.
    /// </param>
    public sealed record Text(string Summary, string Details, string? WarningsList);

    public static Text For(RunReport report, EffectiveCriteria criteria, string workbookPath)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(criteria);

        StringBuilder summary = new();
        summary.AppendLine(CultureInfo.InvariantCulture, $"Workbook written: {workbookPath}");
        summary.AppendLine();

        // The budget sheet's first row, "Measurement lines exported", counts
        // the coded lines. The same phrase here means the same number, and
        // the unclassified elements are named apart, so one label never
        // carries two values.
        int coded = report.ExportedLines - report.UnclassifiedCount;
        summary.AppendLine(report.NoMeasurableElements
            ? "No measurable elements were found. The workbook states zero measurement lines."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{coded} measurement {(coded == 1 ? "line" : "lines")} exported to the budget sheet; "
                + $"{report.UnclassifiedCount} {(report.UnclassifiedCount == 1 ? "element" : "elements")} listed as unclassified."));
        summary.AppendLine();

        summary.AppendLine(criteria.Source == ConfigSource.File
            ? $"Criteria in force, from {criteria.Path} over the built-in criteria:"
            : $"No criteria file was found beside the model ({CriteriaFileLocator.FileName}), so the built-in criteria are in force:");
        foreach (CategoryCriterion criterion in criteria.Criteria.ByCategory.Values.OrderBy(c => c.Category, StringComparer.Ordinal))
        {
            summary.AppendLine(Describe(criterion));
        }

        summary.AppendLine();
        // A long list runs past the dialog's reach and closes with it, so it
        // is also kept beside the workbook.
        summary.Append(report.WarningCount == 0
            ? "No warnings."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{report.WarningCount} warnings: see the details below, and the full list kept in {WorkbookPath.WarningsFor(workbookPath)}"));

        string details = string.Join(
            Environment.NewLine,
            report.Warnings.Select(warning => $"- {warning.CategoryName} {warning.UniqueId} ({warning.TypeName}): {warning.Condition}"));

        return new Text(
            summary.ToString(),
            details,
            report.WarningCount == 0 ? null : summary + Environment.NewLine + Environment.NewLine + details + Environment.NewLine);
    }

    private static string Describe(CategoryCriterion criterion)
    {
        string unit = criterion.Unit.Symbol();
        string threshold = criterion.Threshold.Value.ToString(CultureInfo.InvariantCulture);
        string rule = criterion.Threshold switch
        {
            { Value: 0, Mode: BoundaryMode.Exclusive } => "every opening is deducted",
            { Mode: BoundaryMode.Exclusive } => $"openings smaller than {threshold} {unit} are not deducted (exclusive)",
            { Mode: BoundaryMode.Inclusive } => $"openings up to {threshold} {unit} are not deducted (inclusive)",
            _ => throw new ArgumentOutOfRangeException(nameof(criterion), criterion.Threshold.Mode, "Not a declared boundary mode."),
        };

        return $"  {criterion.Category}: measured in {unit} from {string.Join(", ", criterion.Sources)}; {rule}.";
    }
}

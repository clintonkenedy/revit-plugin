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
    public sealed record Text(string Summary, string Details);

    public static Text For(RunReport report, EffectiveCriteria criteria, string workbookPath)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(criteria);

        StringBuilder summary = new();
        summary.AppendLine(CultureInfo.InvariantCulture, $"Workbook written: {workbookPath}");
        summary.AppendLine();

        // The run counts every line; the budget sheet counts the coded ones.
        // Naming both keeps "4" here and "3" there from looking contradictory.
        summary.AppendLine(report.NoMeasurableElements
            ? "No measurable elements were found. The workbook states zero measurement lines."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Exported {report.ExportedLines} measurement lines: {report.ExportedLines - report.UnclassifiedCount} coded into partidas, {report.UnclassifiedCount} unclassified."));
        summary.AppendLine();

        summary.AppendLine(criteria.Source == ConfigSource.File
            ? $"Criteria read from {criteria.Path}:"
            : $"No criteria file was found beside the model ({CriteriaFileLocator.FileName}), so the built-in criteria applied:");
        foreach (CategoryCriterion criterion in criteria.Criteria.ByCategory.Values.OrderBy(c => c.Category, StringComparer.Ordinal))
        {
            summary.AppendLine(Describe(criterion));
        }

        summary.AppendLine();
        summary.Append(report.WarningCount == 0
            ? "No warnings."
            : string.Create(CultureInfo.InvariantCulture, $"{report.WarningCount} warnings: see the details below."));

        string details = string.Join(
            Environment.NewLine,
            report.Warnings.Select(warning => $"- {warning.CategoryName} {warning.UniqueId} ({warning.TypeName}): {warning.Condition}"));

        return new Text(summary.ToString(), details);
    }

    private static string Describe(CategoryCriterion criterion)
    {
        string unit = criterion.Unit.Symbol();
        string threshold = criterion.Threshold.Value.ToString(CultureInfo.InvariantCulture);
        string rule = criterion.Threshold.Mode switch
        {
            BoundaryMode.Exclusive => $"openings smaller than {threshold} {unit} are not deducted (exclusive)",
            BoundaryMode.Inclusive => $"openings up to {threshold} {unit} are not deducted (inclusive)",
            _ => throw new ArgumentOutOfRangeException(nameof(criterion), criterion.Threshold.Mode, "Not a declared boundary mode."),
        };

        return $"  {criterion.Category}: measured in {unit} from {string.Join(", ", criterion.Sources)}; {rule}.";
    }
}

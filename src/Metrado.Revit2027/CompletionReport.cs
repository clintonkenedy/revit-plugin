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
        // the unclassified ones are named apart, so one label never carries
        // two values.
        int coded = report.ExportedLines - report.UnclassifiedCount;
        summary.AppendLine(report.NoMeasurableElements
            ? "No measurable elements were found. The workbook states zero measurement lines."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{coded} measurement {(coded == 1 ? "line" : "lines")} exported to the budget sheet; {Unclassified(report)} listed as unclassified."));
        summary.AppendLine();

        summary.AppendLine(criteria.Source != ConfigSource.File
            ? $"No criteria file was found beside the model ({CriteriaFileLocator.FileName}), so the built-in criteria are in force:"
            : criteria.ConfigurationName is string name
                ? $"Criteria in force, from the configuration '{name}' ({criteria.Path}) over the built-in criteria:"
                : $"Criteria in force, from {criteria.Path} over the built-in criteria:");
        foreach (CategoryCriterion criterion in criteria.Criteria.ByCategory.Values.OrderBy(c => c.Category, StringComparer.Ordinal))
        {
            summary.AppendLine(Describe(criterion));
        }

        if (criteria.SharedParameter is Guid shared)
        {
            summary.AppendLine(CultureInfo.InvariantCulture, $"Codes are read from the Assembly Code, then the Keynote, then the shared parameter {shared:D}.");
        }

        summary.AppendLine();
        // A long list runs past the dialog's reach and closes with it, so it
        // is also kept beside the workbook.
        summary.Append(report.WarningCount == 0
            ? "No warnings."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{report.WarningCount} {(report.WarningCount == 1 ? "warning" : "warnings")}: see the details below, and the full list kept in {WorkbookPath.WarningsFor(workbookPath)}"));

        string details = string.Join(
            Environment.NewLine,
            report.Warnings.Select(warning => $"- {warning.CategoryName} {warning.UniqueId} ({warning.FamilyName}: {warning.TypeName}): {warning.Condition}"));

        return new Text(
            summary.ToString(),
            details,
            report.WarningCount == 0 ? null : summary + Environment.NewLine + Environment.NewLine + details + Environment.NewLine);
    }

    /// <summary>
    /// The unclassified lines, and the elements they belong to where those are
    /// fewer: a layered element lists a line per uncoded material.
    /// </summary>
    private static string Unclassified(RunReport report)
    {
        int lines = report.UnclassifiedCount;
        int elements = report.UnclassifiedElements;
        string ofElements = string.Create(CultureInfo.InvariantCulture, $"{elements} {(elements == 1 ? "element" : "elements")}");
        return lines == elements
            ? ofElements
            : string.Create(CultureInfo.InvariantCulture, $"{lines} lines of {ofElements}");
    }

    private static string Describe(CategoryCriterion criterion)
    {
        string unit = criterion.Unit.Symbol();

        // N1: no source is a counted category, measured before any source is
        // read, so no openings rule applies to it.
        if (criterion.Sources.Count == 0)
        {
            return $"  {criterion.Category}: counted in {unit}, one per instance.";
        }

        string threshold = criterion.Threshold.Value.ToString(CultureInfo.InvariantCulture);
        string rule = criterion.Threshold switch
        {
            { Value: 0, Mode: BoundaryMode.Exclusive } => "every opening is deducted",
            { Mode: BoundaryMode.Exclusive } => $"openings smaller than {threshold} {unit} are not deducted (exclusive)",
            { Mode: BoundaryMode.Inclusive } => $"openings up to {threshold} {unit} are not deducted (inclusive)",
            _ => throw new ArgumentOutOfRangeException(nameof(criterion), criterion.Threshold.Mode, "Not a declared boundary mode."),
        };

        string whole = $"measured in {unit} from {string.Join(", ", criterion.Sources)}";
        if (criterion.Layers is not LayerCriterion layers)
        {
            return $"  {criterion.Category}: {whole}; {rule}.";
        }

        List<LayerFunction> cubic = [.. Enum.GetValues<LayerFunction>().Where(function => layers.UnitOf(function) == QuantityUnit.CubicMetre)];
        string units = cubic.Count == 0 ? "every function in m2" : $"every function in m2 except {Joined(cubic)} in m3";

        // With every opening deducted there is nothing to give back.
        string share = criterion.Threshold is { Value: 0, Mode: BoundaryMode.Exclusive }
            ? string.Empty
            : ", each m2 line getting each one's area back once per layer it covers unless a warning says otherwise"
                + (cubic.Count == 0 ? string.Empty : ", and each m3 line keeping Revit's deduction");
        return $"  {criterion.Category}: by material layer, one line per material, {units}; {rule}{share}; "
            + $"an element whose layers do not account for it is measured whole, in {unit} from {string.Join(", ", criterion.Sources)}.";
    }

    private static string Joined(List<LayerFunction> functions) =>
        functions.Count == 1
            ? functions[0].ToString()
            : $"{string.Join(", ", functions.Take(functions.Count - 1))} and {functions[^1]}";
}

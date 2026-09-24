using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Metrado.Configuration;
using Metrado.Domain;
using Metrado.Excel;

namespace Metrado.Revit2027;

/// <summary>
/// The ribbon button's command: extract → resolve → measure → codify →
/// group → write, then report. <c>ReadOnly</c> is the read-only guarantee
/// where Revit itself enforces it: the command reads the model and writes
/// only the workbook and its warnings list, beside the model, never the
/// document.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class ExportTakeoffCommand : IExternalCommand
{
    private const string Title = "Metrado";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document? document = commandData.Application.ActiveUIDocument?.Document;
        if (document is null)
        {
            TaskDialog.Show(Title, "Open a model first: there is no active document to read.");
            return Result.Cancelled;
        }

        ModelLocation location = ModelLocation.Of(document.PathName, document.IsModelInCloud);
        if (location.Refusal is not null)
        {
            TaskDialog.Show(Title, location.Refusal);
            return Result.Cancelled;
        }

        // The location is a model in a folder on disk, which always names a workbook.
        string workbook = WorkbookPath.For(location.Path, DateTime.Now, File.Exists)!;

        (CriteriaFileLookup lookup, string? criteriaPath) = CriteriaFileLocator.Locate(location.Path);
        Result<EffectiveCriteria, ConfigError> criteria = CriteriaResolver.Resolve(lookup, criteriaPath);

        // A supplied file that cannot be honoured stops the run: a budget
        // silently priced under the defaults would look right and be wrong.
        // So does one naming a source extraction never reads, which every
        // element would lack.
        ConfigError? refusal = criteria.IsOk ? ReadableSources.Check(criteria.Value.Criteria) : criteria.Error;
        if (refusal is not null)
        {
            string file = criteria.IsOk && criteria.Value.Path is string path ? $" The criteria file is '{path}'." : string.Empty;
            TaskDialog.Show(Title, $"No workbook was written. {refusal.Message}{file}");
            return Result.Cancelled;
        }

        CompletionReport.Text report;
        try
        {
            // Reserved before the model is read: a folder that refuses writes
            // stops the export before the long read, not after it.
            using ExportFiles files = ExportFiles.Reserve(workbook);

            // Layers are read only for the categories the criteria take off by layer.
            Stopwatch clock = Stopwatch.StartNew();
            ExtractionService.Extraction extraction = ExtractionService.Extract(document, criteria.Value.SharedParameter, ReadableSources.Layered(criteria.Value.Criteria));
            TakeoffExport.Outcome outcome = TakeoffExport.Run(criteria.Value, extraction.Elements, extraction.Warnings);
            TakeoffWorkbook.Write(outcome.Result, files.Workbook);

            report = CompletionReport.For(outcome.Report, criteria.Value, workbook);
            files.Commit(report.WarningsList);
            Journal(commandData, report, extraction, clock.Elapsed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string failure = ExportFiles.Explain(workbook, ex);
            commandData.Application.Application.WriteJournalComment($"Metrado: {failure}", true);
            TaskDialog.Show(Title, failure);
            return Result.Cancelled;
        }

        TaskDialog dialog = new(Title)
        {
            MainInstruction = "Export complete",
            MainContent = report.Summary,
            ExpandedContent = report.Details,
        };
        dialog.Show();
        return Result.Succeeded;
    }

    /// <summary>The journal keeps what the dialog showed, how long the export took, and why each opening was reported.</summary>
    private static void Journal(ExternalCommandData commandData, CompletionReport.Text report, ExtractionService.Extraction extraction, TimeSpan took)
    {
        Autodesk.Revit.ApplicationServices.Application application = commandData.Application.Application;
        application.WriteJournalComment($"Metrado: {report.Summary.Replace(Environment.NewLine, " | ", StringComparison.Ordinal)}", true);
        application.WriteJournalComment(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Metrado: read, measured and written in {took.TotalSeconds:0.0} s"), false);
        foreach (IGrouping<string, string> reason in extraction.UnmeasuredReasons
            .GroupBy(reason => reason)
            .OrderByDescending(group => group.Count()))
        {
            application.WriteJournalComment($"Metrado: reported x{reason.Count()}: {reason.Key}", false);
        }
    }
}

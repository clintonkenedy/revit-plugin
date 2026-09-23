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
/// only the workbook, a file beside the model, never the document.
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

        // The workbook and the criteria file both live beside the model; a
        // model never saved (or a cloud model) has no folder for either.
        string? workbook = WorkbookPath.For(document.PathName, DateTime.Now, File.Exists);
        if (workbook is null)
        {
            TaskDialog.Show(Title, "Save the model first. The workbook is written beside it, and the criteria file is looked for there.");
            return Result.Cancelled;
        }

        (CriteriaFileLookup lookup, string? criteriaPath) = CriteriaFileLocator.Locate(document.PathName);
        Result<EffectiveCriteria, ConfigError> criteria = CriteriaResolver.Resolve(lookup, criteriaPath);
        if (!criteria.IsOk)
        {
            // A supplied file that cannot be honoured stops the run: a budget
            // silently priced under the defaults would look right and be wrong.
            TaskDialog.Show(Title, $"No workbook was written. {criteria.Error.Message}");
            return Result.Cancelled;
        }

        ExtractionService.Extraction extraction = ExtractionService.Extract(document);
        TakeoffExport.Outcome outcome = TakeoffExport.Run(criteria.Value, extraction.Elements, extraction.Warnings);

        // CreateNew, never Create: the name was chosen free, and if anything
        // took it since, failing beats replacing an estimator's priced copy.
        using (FileStream stream = new(workbook, FileMode.CreateNew, FileAccess.Write))
        {
            TakeoffWorkbook.Write(outcome.Result, stream);
        }

        CompletionReport.Text report = CompletionReport.For(outcome.Report, criteria.Value, workbook);
        Journal(commandData, report, extraction);

        TaskDialog dialog = new(Title)
        {
            MainInstruction = "Export complete",
            MainContent = report.Summary,
            ExpandedContent = report.Details,
        };
        dialog.Show();
        return Result.Succeeded;
    }

    /// <summary>The journal keeps what the dialog showed, and why each opening was reported.</summary>
    private static void Journal(ExternalCommandData commandData, CompletionReport.Text report, ExtractionService.Extraction extraction)
    {
        Autodesk.Revit.ApplicationServices.Application application = commandData.Application.Application;
        application.WriteJournalComment($"Metrado: {report.Summary.Replace(Environment.NewLine, " | ", StringComparison.Ordinal)}", true);
        foreach (IGrouping<string, string> reason in extraction.UnmeasuredReasons
            .GroupBy(reason => reason)
            .OrderByDescending(group => group.Count()))
        {
            application.WriteJournalComment($"Metrado: reported x{reason.Count()}: {reason.Key}", false);
        }
    }
}

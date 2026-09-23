using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Metrado.Revit2027;

/// <summary>
/// The ribbon button's command. <c>ReadOnly</c> is the read-only guarantee
/// where Revit itself enforces it: this command cannot open a write
/// transaction, whatever a later change puts inside it.
///
/// It extracts the walls and reports what it read. Measuring, codifying and
/// writing the workbook are wired in task 1.24; until then the command says
/// so instead of producing a workbook that looks like a result.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class ExportTakeoffCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document? document = commandData.Application.ActiveUIDocument?.Document;
        if (document is null)
        {
            TaskDialog.Show("Metrado", "Open a model first: there is no active document to read.");
            return Result.Cancelled;
        }

        ExtractionService.Extraction extraction = ExtractionService.Extract(document);
        string summary =
            $"Read {extraction.Elements.Count} walls: {extraction.OpeningsMeasured} openings measured individually, "
            + $"{extraction.Warnings.Count} reported without a measure. "
            + "The export is not wired in this build yet, so no workbook was written.";

        // The journal keeps what the dialog showed, and why each opening was
        // reported, so a run leaves evidence even when nobody reads the dialog.
        commandData.Application.Application.WriteJournalComment($"Metrado: {summary}", true);
        foreach (IGrouping<string, string> reason in extraction.UnmeasuredReasons
            .GroupBy(reason => reason)
            .OrderByDescending(group => group.Count()))
        {
            commandData.Application.Application.WriteJournalComment($"Metrado: reported x{reason.Count()}: {reason.Key}", false);
        }
        TaskDialog.Show("Metrado", summary);
        return Result.Succeeded;
    }
}

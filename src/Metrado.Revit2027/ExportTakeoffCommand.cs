using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Metrado.Revit2027;

/// <summary>
/// The ribbon button's command. <c>ReadOnly</c> is the read-only guarantee
/// where Revit itself enforces it: this command cannot open a write
/// transaction, whatever a later change puts inside it.
///
/// The export pipeline is wired in task 1.24. Until then the command says so
/// instead of producing a workbook that looks like a result.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class ExportTakeoffCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        TaskDialog.Show("Metrado", "The add-in is loaded. The export is not wired in this build yet, so no workbook was written.");

        return Result.Succeeded;
    }
}

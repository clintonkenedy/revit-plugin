using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Metrado.Revit2027;

/// <summary>
/// Enables the export only with a project open. A <c>ReadOnly</c> command
/// "should not be associated to a command visible when there is no active
/// document" (TransactionMode.ReadOnly), and a family document has no walls
/// to price.
/// </summary>
public sealed class CommandAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) =>
        applicationData.ActiveUIDocument?.Document is { IsFamilyDocument: false };
}

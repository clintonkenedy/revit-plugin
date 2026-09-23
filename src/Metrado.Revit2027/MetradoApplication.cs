using Autodesk.Revit.UI;

namespace Metrado.Revit2027;

/// <summary>
/// The class <c>Metrado.addin</c> names. Revit constructs it at startup and
/// this is the add-in's only entry point: it adds one ribbon button and does
/// nothing else, so a failure here is a registration failure and nothing more.
/// </summary>
public sealed class MetradoApplication : IExternalApplication
{
    private const string PanelName = "Metrado";

    public Result OnStartup(UIControlledApplication application)
    {
        RibbonPanel panel = application.CreateRibbonPanel(PanelName);

        // The class name comes from the type, never a string literal, so a
        // rename cannot leave the button pointing at a class that is gone.
        panel.AddItem(new PushButtonData(
            nameof(ExportTakeoffCommand),
            "Export\nmetrado",
            typeof(ExportTakeoffCommand).Assembly.Location,
            typeof(ExportTakeoffCommand).FullName)
        {
            ToolTip = "Export the model's partidas and metrado to an Excel workbook.",
        });

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}

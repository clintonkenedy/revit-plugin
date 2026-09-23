using Autodesk.Revit.UI;

namespace Metrado.Revit2027;

/// <summary>
/// The class <c>Metrado.addin</c> names. Revit constructs it at startup and
/// this is the add-in's only entry point: it checks the deployment is complete,
/// adds the export button (and, in a development session, the smoke run's)
/// and does nothing else, so a failure here is a deployment or registration
/// failure and nothing more.
/// </summary>
public sealed class MetradoApplication : IExternalApplication
{
    private const string PanelName = "Metrado";

    /// <summary>Set to "1" in Revit's environment, it adds the host smoke run's button.</summary>
    public const string SmokeSwitch = "METRADO_SMOKE";

    public Result OnStartup(UIControlledApplication application)
    {
        // First, and allowed to throw: Revit reports a startup exception with
        // its message, which names each missing assembly. An add-in whose
        // closure is incomplete must not put a button on the ribbon.
        DependencyClosure.Verify(Path.GetDirectoryName(typeof(MetradoApplication).Assembly.Location)!);

        RibbonPanel panel = application.CreateRibbonPanel(PanelName);

        // The class name comes from the type, never a string literal, so a
        // rename cannot leave the button pointing at a class that is gone.
        panel.AddItem(new PushButtonData(
            nameof(ExportTakeoffCommand),
            "Export\nmetrado",
            typeof(ExportTakeoffCommand).Assembly.Location,
            typeof(ExportTakeoffCommand).FullName)
        {
            ToolTip = "Export the model's partidas and metrado to an Excel workbook beside the model.",
            AvailabilityClassName = typeof(CommandAvailability).FullName,
        });

        // Development only: the host smoke run starts Revit with this set.
        // Estimators never see the button.
        if (Environment.GetEnvironmentVariable(SmokeSwitch) == "1")
        {
            panel.AddItem(new PushButtonData(
                nameof(SmokeCommand),
                "Smoke\ntest",
                typeof(SmokeCommand).Assembly.Location,
                typeof(SmokeCommand).FullName)
            {
                AvailabilityClassName = typeof(CommandAvailability).FullName,
            });
        }

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}

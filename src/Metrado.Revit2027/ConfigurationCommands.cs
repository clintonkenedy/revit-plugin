using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Metrado.Configuration;
using Metrado.Domain;

namespace Metrado.Revit2027;

/// <summary>
/// The ribbon's "Load configuration" (task 3.7): the estimator picks a saved
/// configuration, which is copied beside the model as its criteria file, so
/// every export of the model uses it. <c>ReadOnly</c>: it writes a file
/// beside the model, never the document.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class LoadConfigurationCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (ConfigurationCommand.Model(commandData) is not string model)
        {
            return Result.Cancelled;
        }

        using FileOpenDialog dialog = new(ConfigurationCommand.Filter) { Title = "Load a Metrado configuration beside the model" };
        if (dialog.Show() != ItemSelectionDialogResult.Confirmed)
        {
            return Result.Cancelled;
        }

        string chosen = ModelPathUtils.ConvertModelPathToUserVisiblePath(dialog.GetSelectedModelPath());
        try
        {
            LoadOutcome outcome = ConfigurationFiles.Load(chosen, model, replace: false);
            if (outcome.Status == LoadStatus.NeedsConsent)
            {
                if (TaskDialog.Show(ConfigurationCommand.Title, outcome.Message, TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes)
                {
                    return Result.Cancelled;
                }

                outcome = ConfigurationFiles.Load(chosen, model, replace: true);
            }

            TaskDialog.Show(ConfigurationCommand.Title, outcome.Message);
            return outcome.Status == LoadStatus.Loaded ? Result.Succeeded : Result.Cancelled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TaskDialog.Show(ConfigurationCommand.Title, $"The configuration could not be written beside the model: {ex.Message}");
            return Result.Cancelled;
        }
    }
}

/// <summary>
/// The ribbon's "Save configuration" (task 3.7): the criteria in force for
/// the model are saved where the estimator chooses, named after the file.
/// <c>ReadOnly</c>: it writes the chosen file, never the document.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public sealed class SaveConfigurationCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (ConfigurationCommand.Model(commandData) is not string model)
        {
            return Result.Cancelled;
        }

        // The criteria an export of this model would run under, refused the same way.
        (CriteriaFileLookup lookup, string? criteriaPath) = CriteriaFileLocator.Locate(model);
        Result<EffectiveCriteria, ConfigError> criteria = CriteriaResolver.Resolve(lookup, criteriaPath);
        ConfigError? refusal = criteria.IsOk ? ReadableSources.Check(criteria.Value.Criteria) : criteria.Error;
        if (refusal is not null)
        {
            TaskDialog.Show(ConfigurationCommand.Title, $"No configuration was saved. {refusal.Message}");
            return Result.Cancelled;
        }

        using FileSaveDialog dialog = new(ConfigurationCommand.Filter)
        {
            Title = "Save the criteria in force as a Metrado configuration",
            InitialFileName = ConfigurationFiles.ProposedFileName(criteria.Value.ConfigurationName, model),
        };
        if (dialog.Show() != ItemSelectionDialogResult.Confirmed)
        {
            return Result.Cancelled;
        }

        string target = ModelPathUtils.ConvertModelPathToUserVisiblePath(dialog.GetSelectedModelPath());
        try
        {
            string name = ConfigurationFiles.Save(criteria.Value, target);
            TaskDialog.Show(ConfigurationCommand.Title, $"The criteria in force are saved as the configuration '{name}', '{target}'. Load it beside any model to measure it the same way.");
            return Result.Succeeded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TaskDialog.Show(ConfigurationCommand.Title, $"The configuration could not be saved to '{target}': {ex.Message}");
            return Result.Cancelled;
        }
        catch (ArgumentException)
        {
            TaskDialog.Show(ConfigurationCommand.Title, $"No configuration was saved: a configuration is named after its file, and '{Path.GetFileName(target)}' gives it no name.");
            return Result.Cancelled;
        }
    }
}

/// <summary>What the two configuration commands share: the model they work beside.</summary>
internal static class ConfigurationCommand
{
    public const string Title = "Metrado";

    public const string Filter = "Metrado configuration (*.json)|*.json";

    /// <summary>The model's path on disk, or null once the estimator has been told why there is none.</summary>
    public static string? Model(ExternalCommandData commandData)
    {
        Document? document = commandData.Application.ActiveUIDocument?.Document;
        if (document is null)
        {
            TaskDialog.Show(Title, "Open a model first: a configuration is kept beside the model it measures.");
            return null;
        }

        ModelLocation location = ModelLocation.Of(document.PathName, document.IsModelInCloud);
        if (location.Refusal is not null)
        {
            TaskDialog.Show(Title, location.Refusal);
            return null;
        }

        return location.Path;
    }
}

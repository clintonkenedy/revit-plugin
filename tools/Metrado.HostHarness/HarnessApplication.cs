using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace Metrado.HostHarness;

/// <summary>
/// What one unattended run is asked to do. Read from the file METRADO_HARNESS names.
/// Mode "command" posts <see cref="CommandId"/>; mode "probe-walls" runs <see cref="WallProbe"/> instead,
/// and mode "probe-area-settings" runs <see cref="AreaSettingsProbe"/>.
/// </summary>
public sealed record HarnessRequest(
    string CommandId, string ReportPath, int SettleIdlings = 5, string Mode = "command", int MaxWalls = 30);

/// <summary>What one run observed. Rewritten after every step, so a run that hangs still leaves its trail.</summary>
public sealed class HarnessReport
{
    public HarnessRequest? Request { get; set; }
    public string Stage { get; set; } = "started";
    public string? DocumentTitle { get; set; }
    public string? DocumentPath { get; set; }
    public bool? ModifiedBeforeCommand { get; set; }
    public bool? ModifiedAfterCommand { get; set; }
    public bool CommandPosted { get; set; }
    public List<HarnessDialog> Dialogs { get; } = [];
    public WallProbe.Result? Probe { get; set; }
    public AreaSettingsProbe.Result? AreaProbe { get; set; }
    public HostProbe.Result? HostProbe { get; set; }
    public string? Error { get; set; }
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedUtc { get; set; }
}

public sealed record HarnessDialog(string Kind, string DialogId, string? Message, int ResultGiven, DateTime AtUtc);

/// <summary>
/// Development only, never deployed with Metrado. Inert unless the
/// METRADO_HARNESS environment variable names a request file.
///
/// Drives one session unattended: waits for the document Revit was started
/// with, posts the requested command, answers every dialog while recording
/// its text, then writes a report and exits Revit. The command runs exactly
/// as a ribbon click would run it, in its own transaction mode. Nothing is
/// ever saved: an unsaved-changes prompt is answered No and recorded, because
/// its appearance is itself evidence that the document was modified.
/// </summary>
public sealed class HarnessApplication : IExternalApplication
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private readonly HarnessReport _report = new();
    private HarnessRequest? _request;
    private int _idlingsSincePost;

    public Result OnStartup(UIControlledApplication application)
    {
        string? requestPath = Environment.GetEnvironmentVariable("METRADO_HARNESS");
        if (string.IsNullOrWhiteSpace(requestPath))
        {
            return Result.Succeeded;
        }

        _request = JsonSerializer.Deserialize<HarnessRequest>(File.ReadAllText(requestPath))
            ?? throw new InvalidOperationException($"'{requestPath}' holds no harness request.");
        _report.Request = _request;
        Write("waiting-for-document");

        application.DialogBoxShowing += OnDialog;
        application.Idling += OnIdling;
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

    private void OnIdling(object? sender, IdlingEventArgs e)
    {
        if (sender is not UIApplication app || _request is null || _report.FinishedUtc is not null)
        {
            return;
        }

        e.SetRaiseWithoutDelay();
        Document? document = app.ActiveUIDocument?.Document;

        try
        {
            if (!_report.CommandPosted)
            {
                if (document is null)
                {
                    return;
                }

                _report.DocumentTitle = document.Title;
                _report.DocumentPath = document.PathName;
                _report.ModifiedBeforeCommand = document.IsModified;

                if (string.Equals(_request.Mode, "probe-walls", StringComparison.OrdinalIgnoreCase))
                {
                    Write("probing");
                    _report.Probe = WallProbe.Run(document, _request.MaxWalls);
                    _report.ModifiedAfterCommand = document.IsModified;
                    Finish(app, "done");
                    return;
                }

                if (string.Equals(_request.Mode, "probe-hosts", StringComparison.OrdinalIgnoreCase))
                {
                    Write("probing");
                    _report.HostProbe = HostProbe.Run(document, _request.MaxWalls);
                    _report.ModifiedAfterCommand = document.IsModified;
                    Finish(app, "done");
                    return;
                }

                if (string.Equals(_request.Mode, "probe-area-settings", StringComparison.OrdinalIgnoreCase))
                {
                    Write("probing");
                    _report.AreaProbe = AreaSettingsProbe.Run(document);
                    _report.ModifiedAfterCommand = document.IsModified;
                    Finish(app, "done");
                    return;
                }

                if (!string.Equals(_request.Mode, "command", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Unknown harness mode '{_request.Mode}'.");
                }

                RevitCommandId command = RevitCommandId.LookupCommandId(_request.CommandId)
                    ?? throw new InvalidOperationException($"No command is registered as '{_request.CommandId}'.");
                app.PostCommand(command);
                _report.CommandPosted = true;
                Write("command-posted");
                return;
            }

            // A posted command runs when control returns to Revit; the idlings
            // counted here come after it, so the document is read once it is done.
            if (++_idlingsSincePost < _request.SettleIdlings)
            {
                return;
            }

            _report.ModifiedAfterCommand = document?.IsModified;
            Finish(app, "done");
        }
        catch (Exception ex)
        {
            _report.Error = ex.ToString();
            Finish(app, "failed");
        }
    }

    private void OnDialog(object? sender, DialogBoxShowingEventArgs e)
    {
        (string kind, string? message, int result) = e switch
        {
            TaskDialogShowingEventArgs task => ("TaskDialog", task.Message,
                IsSavePrompt(task.DialogId, task.Message) ? (int)TaskDialogResult.No : (int)TaskDialogResult.Close),
            MessageBoxShowingEventArgs box => ("MessageBox", box.Message, 1 /* IDOK */),
            _ => ("DialogBox", null, 2 /* IDCANCEL */),
        };

        _report.Dialogs.Add(new HarnessDialog(kind, e.DialogId, message, result, DateTime.UtcNow));
        Write(_report.Stage);
        e.OverrideResult(result);
    }

    private static bool IsSavePrompt(string dialogId, string? message) =>
        dialogId.Contains("Save", StringComparison.OrdinalIgnoreCase)
        || (message?.Contains("save", StringComparison.OrdinalIgnoreCase) ?? false);

    private void Finish(UIApplication app, string stage)
    {
        _report.FinishedUtc = DateTime.UtcNow;
        Write(stage);
        app.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit));
    }

    private void Write(string stage)
    {
        _report.Stage = stage;
        if (_request is not null)
        {
            File.WriteAllText(_request.ReportPath, JsonSerializer.Serialize(_report, Json));
        }
    }
}

<#
.SYNOPSIS
    Runs one Metrado command in Revit 2027 unattended and prints what happened.

.DESCRIPTION
    Development only. Deploys tools/Metrado.HostHarness beside Metrado for
    the length of one run, starts Revit on the given model with
    METRADO_HARNESS pointing at a request file, and waits until the harness
    has written its report and Revit has exited. The harness is removed again
    in every case, so the next ordinary Revit session never loads it.

    Metrado itself must already be deployed (deploy/Deploy-Metrado.ps1).
    The model is opened, never saved.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Model,
    [string] $CommandId = 'CustomCtrl_%CustomCtrl_%Add-Ins%Metrado%ExportTakeoffCommand',
    [string] $ReportPath = (Join-Path ([IO.Path]::GetTempPath()) "metrado-harness-$([guid]::NewGuid()).json"),
    [ValidateSet('command', 'probe-walls', 'probe-area-settings')] [string] $Mode = 'command',
    [int] $MaxWalls = 30,
    [int] $TimeoutSeconds = 600,
    [string] $RevitExe = 'D:\autodesk\producto\Revit 2027\Revit.exe',
    # Adds the smoke run's button (METRADO_SMOKE=1, for this Revit only) and posts it.
    [switch] $Smoke,
    # A Revit UI language code such as ESP; Revit's own language when empty.
    [string] $Language,
    [string] $AddinsRoot = (Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2027')
)

$ErrorActionPreference = 'Stop'
if ($Smoke -and -not $PSBoundParameters.ContainsKey('CommandId')) {
    $CommandId = 'CustomCtrl_%CustomCtrl_%Add-Ins%Metrado%SmokeCommand'
}

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace MetradoHarness -Name Dialog -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
'@

# Revit 2027's own wording for the prompt's title and name label, English and
# Spanish (es-ES UIFrameworkRes, TaskDialog_Security_Unsigned_File_Loading), so
# a run under a Spanish UI is recognised too. Its buttons are pressed by id,
# which no language changes: 1002 is Load Once ("Cargar una vez").
$UnsignedPrompt = @{
    Title    = @('Security - Unsigned Add-In', 'Seguridad - Complemento sin firma')
    Name     = '(Name|Nombre)'
    LoadOnce = 1002
}

function Confirm-OwnUnsignedAddIn {
    $A = [System.Windows.Automation.AutomationElement]
    $scope = [System.Windows.Automation.TreeScope]::Descendants
    $prompt = $UnsignedPrompt.Title | ForEach-Object {
        $A::RootElement.FindFirst($scope, [System.Windows.Automation.PropertyCondition]::new($A::NameProperty, $_))
    } | Where-Object { $_ } | Select-Object -First 1
    if (-not $prompt) { return }

    # One line per text element, as the prompt shows them: the instruction
    # comes first, so joined by spaces "Name:" would never start a line.
    $text = ($prompt.FindAll($scope, [System.Windows.Automation.PropertyCondition]::new(
        $A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)) |
        ForEach-Object { $_.Current.Name }) -join "`n"
    if ($text -notmatch "(?m)^$($UnsignedPrompt.Name):[ \t]+(Metrado|Metrado host harness \(development only\))[ \t]*\r?$") { return }

    # The prompt is a native task dialog, and its command links expose no UI
    # Automation pattern. TDM_CLICK_BUTTON presses Load Once by its id: no
    # mouse, and no need for the prompt to be in front, which a minimised or
    # disconnected remote session never lets it be.
    $handle = [IntPtr]$prompt.Current.NativeWindowHandle
    [MetradoHarness.Dialog]::PostMessage($handle, 0x0466, [IntPtr]$UnsignedPrompt.LoadOnce, [IntPtr]::Zero) | Out-Null
    Write-Host "Answered Load Once for: $($Matches[2])"
}

if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw 'Revit is already running. The harness needs a session of its own; close Revit and run again.'
}

# Revit keeps the language it last ran in ([Language] Select= in Revit.ini,
# UTF-16) and starts in it from then on. A run in another language puts the
# person's own choice back once Revit has exited.
$revitIni = Join-Path $env:APPDATA 'Autodesk\Revit\Autodesk Revit 2027\Revit.ini'
function Get-LanguageChoice {
    $lines = @(Get-Content $revitIni -Encoding Unicode)
    $at = [Array]::IndexOf($lines, '[Language]')
    if ($at -ge 0 -and $at + 1 -lt $lines.Count -and $lines[$at + 1] -like 'Select=*') { $lines[$at + 1] }
}
function Restore-LanguageChoice([string] $choice) {
    $lines = [Collections.Generic.List[string]]@(Get-Content $revitIni -Encoding Unicode)
    $at = $lines.IndexOf('[Language]')
    if ($at -lt 0 -or $at + 1 -ge $lines.Count -or $lines[$at + 1] -notlike 'Select=*') { return }
    if ($choice) { $lines[$at + 1] = $choice } else { $lines.RemoveRange($at, 2) }
    Set-Content $revitIni $lines -Encoding Unicode
}
# The choice to put back is kept on disk until it is put back, so a run that
# died before Revit exited (a closed console, a killed runner) is repaired by
# the next run, which starts only when no Revit is running.
$languageMarker = Join-Path $env:LOCALAPPDATA 'Metrado\harness-language-choice.txt'
if (Test-Path $languageMarker) {
    $left = (Get-Content $languageMarker -Raw).Trim()
    Restore-LanguageChoice $(if ($left) { $left } else { $null })
    Remove-Item $languageMarker
    Write-Host "Put back Revit's language choice ('$left') left by an interrupted run."
}
if (-not (Test-Path $Model)) { throw "Model '$Model' not found." }

$languageChoice = $null
if ($Language -and (Test-Path $revitIni)) {
    $languageChoice = Get-LanguageChoice
    New-Item -ItemType Directory (Split-Path $languageMarker) -Force | Out-Null
    Set-Content $languageMarker "$languageChoice"
}

$project = Join-Path $PSScriptRoot 'Metrado.HostHarness\Metrado.HostHarness.csproj'
$folder = Join-Path $AddinsRoot 'Metrado.HostHarness'
$manifest = Join-Path $AddinsRoot 'Metrado.HostHarness.addin'
$request = Join-Path ([IO.Path]::GetTempPath()) "metrado-harness-request-$([guid]::NewGuid()).json"

dotnet build $project --configuration Release --output $folder | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Building the harness failed with exit code $LASTEXITCODE." }

try {
    Move-Item (Join-Path $folder 'Metrado.HostHarness.addin') $manifest -Force
    # The harness carries Metrado's assemblies to judge them; Metrado's own
    # manifest comes along with them and has no business in this folder.
    Remove-Item (Join-Path $folder 'Metrado.addin') -ErrorAction SilentlyContinue
    @{ CommandId = $CommandId; ReportPath = $ReportPath; SettleIdlings = 5; Mode = $Mode; MaxWalls = $MaxWalls } | ConvertTo-Json | Set-Content $request -Encoding utf8
    Remove-Item $ReportPath -ErrorAction SilentlyContinue

    $env:METRADO_HARNESS = $request
    if ($Smoke) { $env:METRADO_SMOKE = '1' }
    $arguments = @("`"$Model`"")
    if ($Language) { $arguments += "/language $Language" }
    $revit = Start-Process $RevitExe -ArgumentList $arguments -PassThru
    Remove-Item Env:METRADO_HARNESS
    Remove-Item Env:METRADO_SMOKE -ErrorAction SilentlyContinue

    # Every rebuilt binary is unsigned and new to Revit, so it asks before
    # loading it — before any add-in can answer. Only a prompt naming Metrado
    # or this harness is answered, and only with Load Once, which trusts
    # nothing beyond this session; any other prompt is left for a person.
    # A busy Revit makes UI Automation queries time out; that means "no
    # prompt showing now", never a reason to abandon the run.
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not $revit.HasExited -and (Get-Date) -lt $deadline) {
        try { Confirm-OwnUnsignedAddIn } catch { Write-Verbose "Prompt check skipped: $($_.Exception.Message)" }
        Start-Sleep -Seconds 2
    }

    if (-not $revit.WaitForExit(1000)) {
        Write-Warning "Revit did not exit within $TimeoutSeconds s; asking it to close. Nothing is saved."
        $revit.CloseMainWindow() | Out-Null
        if (-not $revit.WaitForExit(60000)) { $revit.Kill(); Write-Warning 'Revit was killed after a further 60 s.' }
    }
}
finally {
    # The manifest goes first and unconditionally: without it the harness
    # never loads again, even if its folder is still held open.
    Remove-Item Env:METRADO_HARNESS -ErrorAction SilentlyContinue
    Remove-Item Env:METRADO_SMOKE -ErrorAction SilentlyContinue
    Remove-Item $manifest -Force -ErrorAction SilentlyContinue
    Remove-Item $request -Force -ErrorAction SilentlyContinue
    if ($revit -and -not $revit.HasExited) {
        Write-Warning "Revit is still running; '$folder' stays until it exits. Without its manifest the harness is inert."
        if ($Language) { Write-Warning "Revit.ini still selects $Language; the next run puts '$languageChoice' back, or set it once Revit exits." }
    }
    else {
        Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue
        if ($Language -and (Test-Path $revitIni)) {
            Restore-LanguageChoice $languageChoice
            Remove-Item $languageMarker -ErrorAction SilentlyContinue
        }
    }
}

if (-not (Test-Path $ReportPath)) { throw "The harness wrote no report to '$ReportPath'." }
Write-Host "Report: $ReportPath"
Get-Content $ReportPath -Raw

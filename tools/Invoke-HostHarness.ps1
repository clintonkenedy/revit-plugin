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
    [ValidateSet('command', 'probe-walls')] [string] $Mode = 'command',
    [int] $MaxWalls = 30,
    [int] $TimeoutSeconds = 600,
    [string] $RevitExe = 'D:\autodesk\producto\Revit 2027\Revit.exe',
    [string] $AddinsRoot = (Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2027')
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace MetradoHarness -Name Mouse -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
'@

function Confirm-OwnUnsignedAddIn {
    $A = [System.Windows.Automation.AutomationElement]
    $scope = [System.Windows.Automation.TreeScope]::Descendants
    $prompt = $A::RootElement.FindFirst($scope,
        [System.Windows.Automation.PropertyCondition]::new($A::NameProperty, 'Security - Unsigned Add-In'))
    if (-not $prompt) { return }

    $text = ($prompt.FindAll($scope, [System.Windows.Automation.PropertyCondition]::new(
        $A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)) |
        ForEach-Object { $_.Current.Name }) -join ' '
    if ($text -notmatch '(?m)^Name:[ \t]+(Metrado|Metrado host harness \(development only\))[ \t]*\r?$') { return }

    # The command links expose no UI Automation pattern; a click is the only way in.
    $button = $prompt.FindFirst($scope, [System.Windows.Automation.PropertyCondition]::new($A::NameProperty, 'Load Once'))
    $r = $button.Current.BoundingRectangle
    [MetradoHarness.Mouse]::SetForegroundWindow([IntPtr]$prompt.Current.NativeWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 300
    [MetradoHarness.Mouse]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) | Out-Null
    [MetradoHarness.Mouse]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero)
    [MetradoHarness.Mouse]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero)
    Write-Host "Answered Load Once for: $(($text -split 'Publisher:')[0].Trim())"
}

if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw 'Revit is already running. The harness needs a session of its own; close Revit and run again.'
}
if (-not (Test-Path $Model)) { throw "Model '$Model' not found." }

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
    $revit = Start-Process $RevitExe -ArgumentList "`"$Model`"" -PassThru
    Remove-Item Env:METRADO_HARNESS

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
    Remove-Item $manifest -Force -ErrorAction SilentlyContinue
    Remove-Item $request -Force -ErrorAction SilentlyContinue
    if ($revit -and -not $revit.HasExited) {
        Write-Warning "Revit is still running; '$folder' stays until it exits. Without its manifest the harness is inert."
    }
    else {
        Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path $ReportPath)) { throw "The harness wrote no report to '$ReportPath'." }
Write-Host "Report: $ReportPath"
Get-Content $ReportPath -Raw

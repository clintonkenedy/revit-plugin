<#
.SYNOPSIS
    Publishes Metrado.Revit2027 and deploys it for the current user's Revit 2027.

.DESCRIPTION
    Only 'dotnet publish' lands the add-in's dependency closure flat beside
    it; a plain build does not. The manifest goes to the add-ins folder and
    everything else to its Metrado\ subfolder, which is replaced whole: a
    stale assembly left behind from an earlier deployment would satisfy the
    startup closure check without being the assembly the build resolved.

    The new folder is staged beside the old one and swapped in by renames,
    never by deleting file by file: renaming the old folder aside either
    moves all of it or, when a file inside is held open, fails and changes
    nothing, so a failed run leaves the previous deployment whole. The old
    folder is deleted only after the new one is in place; if that deletion
    fails, its leftover — like any staging folder of an interrupted run —
    carries this script's own name and is cleared on the next run.

    Nothing is replaced that this script did not deploy: a Metrado\ folder
    without Metrado.Revit2027.dll, or a Metrado.addin registering another
    add-in, stops the run untouched.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $AddinsRoot = (Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2027')
)

$ErrorActionPreference = 'Stop'

$addInId = '1834CEE1-BFC3-404E-9002-5E262333E11E'
$repository = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repository 'src\Metrado.Revit2027\Metrado.Revit2027.csproj'
$target = Join-Path $AddinsRoot 'Metrado'
$manifest = Join-Path $AddinsRoot 'Metrado.addin'
$publish = Join-Path ([IO.Path]::GetTempPath()) "metrado-publish-$([guid]::NewGuid())"
$staging = Join-Path $AddinsRoot "Metrado.staging-$([guid]::NewGuid())"
$previous = Join-Path $AddinsRoot "Metrado.previous-$([guid]::NewGuid())"

if ((Test-Path $target) -and (Get-ChildItem $target -Force | Select-Object -First 1) -and
    -not (Test-Path (Join-Path $target 'Metrado.Revit2027.dll'))) {
    throw "'$target' is not empty and holds no Metrado.Revit2027.dll; refusing to replace a folder this script did not deploy."
}
if ((Test-Path $manifest) -and -not (Select-String -Path $manifest -SimpleMatch $addInId -Quiet)) {
    throw "'$manifest' does not register add-in $addInId; refusing to overwrite another add-in's manifest."
}

try {
    dotnet publish $project --configuration $Configuration --output $publish
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE; nothing was deployed."
    }

    # Checked after the publish, right before any file is touched. Only Revit
    # 2027 loads from this folder; a process whose path cannot be read is
    # assumed to be one.
    $running = Get-Process -Name Revit -ErrorAction SilentlyContinue |
        Where-Object { -not $_.Path -or (Get-Item $_.Path).VersionInfo.FileMajorPart -eq 27 }
    if ($running) {
        throw "Revit 2027 is running (PID $($running.Id -join ', ')) and loads from this folder. Close it and deploy again; nothing was deployed."
    }

    Get-ChildItem $AddinsRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'Metrado.staging-*' -or $_.Name -like 'Metrado.previous-*' } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    New-Item -ItemType Directory -Force $staging | Out-Null
    Get-ChildItem $publish -Exclude 'Metrado.addin' | Copy-Item -Destination $staging -Recurse

    if (Test-Path $target) {
        Rename-Item $target -NewName (Split-Path $previous -Leaf)
    }
    try {
        Rename-Item $staging -NewName (Split-Path $target -Leaf)
    }
    catch {
        if (Test-Path $previous) {
            Rename-Item $previous -NewName (Split-Path $target -Leaf)
        }
        throw
    }
    Copy-Item (Join-Path $publish 'Metrado.addin') $manifest -Force
}
finally {
    Remove-Item $publish, $staging -Recurse -Force -ErrorAction SilentlyContinue
}

Remove-Item $previous -Recurse -Force -ErrorAction SilentlyContinue

Get-ChildItem $manifest, $target | Format-Table -AutoSize FullName, Length

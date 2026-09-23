<#
.SYNOPSIS
    Publishes Metrado.Revit2027 and deploys it for the current user's Revit 2027.

.DESCRIPTION
    Only 'dotnet publish' lands the add-in's third-party closure flat beside
    it; a plain build does not. The manifest goes to the add-ins folder and
    everything else to its Metrado\ subfolder, which is replaced whole: a
    stale assembly left behind from an earlier deployment would satisfy the
    startup closure check without being the assembly the build resolved.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $AddinsRoot = (Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2027')
)

$ErrorActionPreference = 'Stop'

if (Get-Process -Name Revit -ErrorAction SilentlyContinue) {
    throw 'Revit is running and holds the deployed assemblies open. Close it and deploy again.'
}

$repository = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repository 'src\Metrado.Revit2027\Metrado.Revit2027.csproj'
$publish = Join-Path ([IO.Path]::GetTempPath()) "metrado-publish-$([guid]::NewGuid())"
$target = Join-Path $AddinsRoot 'Metrado'

dotnet publish $project --configuration $Configuration --output $publish
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE; nothing was deployed."
}

try {
    if (Test-Path $target) {
        Remove-Item $target -Recurse -Force
    }
    New-Item -ItemType Directory -Force $target | Out-Null

    Copy-Item (Join-Path $publish 'Metrado.addin') $AddinsRoot -Force
    Get-ChildItem $publish -Exclude 'Metrado.addin' | Copy-Item -Destination $target -Recurse
}
finally {
    Remove-Item $publish -Recurse -Force
}

Get-ChildItem (Join-Path $AddinsRoot 'Metrado.addin'), $target | Format-Table -AutoSize FullName, Length

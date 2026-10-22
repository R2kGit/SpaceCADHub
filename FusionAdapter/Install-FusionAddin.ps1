# SPDX-License-Identifier: GPL-3.0-only
param()
$ErrorActionPreference = 'Stop'
$fusionAddonSource = Join-Path $PSScriptRoot 'SpaceCADHubFusion'
$fusionAddonParent = Join-Path $env:APPDATA 'Autodesk\Autodesk Fusion 360\API\AddIns'
$fusionAddonTarget = Join-Path $fusionAddonParent 'SpaceCADHubFusion'
if (-not (Test-Path -LiteralPath (Join-Path $fusionAddonSource 'SpaceCADHubFusion.manifest'))) {
    throw 'Add-in-Paket unvollständig.'
}
if (Test-Path -LiteralPath $fusionAddonTarget) {
    throw "Ziel existiert bereits: $fusionAddonTarget. Bitte laufendes Add-in stoppen und Bestand zuerst prüfen; nichts wurde überschrieben."
}
New-Item -ItemType Directory -Path $fusionAddonParent -Force | Out-Null
Copy-Item -LiteralPath $fusionAddonSource -Destination $fusionAddonTarget -Recurse
foreach ($fusionAddonName in @('LICENSE','NOTICE.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $fusionAddonName) -Destination $fusionAddonTarget
}
Write-Host "Installiert: $fusionAddonTarget"
Write-Host 'Fusion: Dienstprogramme > Skripte und Zusatzmodule > Zusatzmodule > SpaceCADHubFusion > Ausführen.'
Write-Host 'Hub: Profil Fusion aktivieren und auswählen. Adapter-EXE leer lassen.'

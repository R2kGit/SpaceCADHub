# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 Benjamin Preuß
param([string]$Destination=(Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference='Stop'
$packageVersion='1.0.11'
& (Join-Path $PSScriptRoot 'build.ps1')
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$packageWindows=Join-Path $Destination "SpaceCADHub-$packageVersion-Windows"
$packageSource=Join-Path $Destination "SpaceCADHub-$packageVersion-GitHub-Source"
$packageWindowsZip=$packageWindows+'.zip'
$packageSourceZip=$packageSource+'.zip'
foreach($packageTarget in @($packageWindows,$packageSource,$packageWindowsZip,$packageSourceZip)){
    if(Test-Path -LiteralPath $packageTarget){throw "Ziel existiert bereits, nichts wird überschrieben: $packageTarget"}
}
New-Item -ItemType Directory -Path $packageWindows,$packageSource | Out-Null
foreach($packageExe in @('SpaceControllerCadHub.exe','SolidCraftHubAdapter.exe')){
    Copy-Item -LiteralPath (Join-Path "$PSScriptRoot\bin" $packageExe) -Destination $packageWindows
}
foreach($packageDoc in @('README.md','ANLEITUNG.md','LICENSE','NOTICE.md','CHANGELOG.md','RELEASE_CHECKLIST.md')){
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $packageDoc) -Destination $packageWindows
}
foreach($packageFolder in @('Assets','FreeCADAddon')){
    $packageFrom=Join-Path $PSScriptRoot $packageFolder
    foreach($packageFile in Get-ChildItem -LiteralPath $packageFrom -File){
        if($packageFile.Name -eq 'logo-prompt.txt'){continue}
        $packageInto=Join-Path $packageWindows $packageFolder
        New-Item -ItemType Directory -Path $packageInto -Force | Out-Null
        Copy-Item -LiteralPath $packageFile.FullName -Destination $packageInto
    }
}
$packageFusion=Join-Path $packageWindows 'FusionAdapter'
New-Item -ItemType Directory -Path $packageFusion | Out-Null
foreach($packageDoc in @('README.md','LICENSE','NOTICE.md','Install-FusionAddin.ps1')){
    Copy-Item -LiteralPath (Join-Path "$PSScriptRoot\FusionAdapter" $packageDoc) -Destination $packageFusion
}
$packageFusionAddon=Join-Path $packageFusion 'SpaceCADHubFusion'
New-Item -ItemType Directory -Path $packageFusionAddon | Out-Null
Get-ChildItem -LiteralPath "$PSScriptRoot\FusionAdapter\SpaceCADHubFusion" -File | Copy-Item -Destination $packageFusionAddon
# Source ZIP: build inputs only, no tests or generated/private files.
foreach($packageFile in Get-ChildItem -LiteralPath $PSScriptRoot -File -Recurse -Force){
    $packageRelative=$packageFile.FullName.Substring($PSScriptRoot.Length).TrimStart('\')
    if($packageRelative -match '(^|\\)(bin|dist|preview|tests?|__pycache__|\.git|\.vs)(\\|$)'){continue}
    if($packageFile.Name -match '^test'){continue}
    $packageAbsolute=[IO.Path]::GetFullPath($packageFile.FullName)
    $packageDist=[IO.Path]::GetFullPath($Destination).TrimEnd('\')+'\'
    if($packageAbsolute.StartsWith($packageDist,[StringComparison]::OrdinalIgnoreCase)){continue}
    if($packageFile.Extension -in @('.exe','.pyc','.pyo','.log','.token')){continue}
    if($packageFile.Name -match '^(profiles|settings)\.json'){continue}
    $packageInto=Join-Path $packageSource $packageRelative
    New-Item -ItemType Directory -Path (Split-Path -Parent $packageInto) -Force | Out-Null
    Copy-Item -LiteralPath $packageFile.FullName -Destination $packageInto
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($packageWindows,$packageWindowsZip)
[IO.Compression.ZipFile]::CreateFromDirectory($packageSource,$packageSourceZip)
Write-Output "Windows: $packageWindowsZip"
Write-Output "Source:  $packageSourceZip"

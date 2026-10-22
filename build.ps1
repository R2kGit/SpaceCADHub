# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$uiLibrary = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$destination = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
& $compiler /nologo /target:exe "/out:$destination\IconBuilder.exe" /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'IconBuilder.cs')
if ($LASTEXITCODE -ne 0) { throw 'Icon builder failed' }
& (Join-Path $destination 'IconBuilder.exe') (Join-Path $PSScriptRoot 'Assets\hub-logo.png') (Join-Path $PSScriptRoot 'Assets\hub.ico')
if ($LASTEXITCODE -ne 0) { throw 'Icon conversion failed' }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/win32icon:$PSScriptRoot\Assets\hub.ico" "/out:$destination\SpaceControllerCadHub.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'ControllerHub.cs') (Join-Path $PSScriptRoot 'VendorButtonLease.cs') (Join-Path $PSScriptRoot 'Profiles.cs') (Join-Path $PSScriptRoot 'MainForm.cs') (Join-Path $PSScriptRoot 'ControllerMap.cs') (Join-Path $PSScriptRoot 'LiveFeedback.cs') (Join-Path $PSScriptRoot 'MotionMonitor.cs') (Join-Path $PSScriptRoot 'Calibration.cs') (Join-Path $PSScriptRoot 'AdapterContext.cs') (Join-Path $PSScriptRoot 'HubDisplay.cs') (Join-Path $PSScriptRoot 'LcdWriter.cs') (Join-Path $PSScriptRoot 'MotionHelp.cs') (Join-Path $PSScriptRoot 'PluginCatalog.cs')
if ($LASTEXITCODE -ne 0) { throw 'Hub build failed' }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$destination\SolidCraftHubAdapter.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'SolidCraftClient.cs') (Join-Path $PSScriptRoot 'SolidCraftAdapter.cs') (Join-Path $PSScriptRoot 'AdapterContext.cs') (Join-Path $PSScriptRoot 'SolidCraftNumbers.cs') (Join-Path $PSScriptRoot 'FocusedInput.cs') "/lib:$uiLibrary" /reference:UIAutomationClient.dll /reference:UIAutomationTypes.dll /reference:WindowsBase.dll
if ($LASTEXITCODE -ne 0) { throw 'Adapter build failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FreeCADAddon') -Destination $destination -Recurse -Force
& (Join-Path $PSScriptRoot 'FusionAdapter\build.ps1')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FusionAdapter') -Destination $destination -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets') -Destination $destination -Recurse -Force
foreach ($document in @('LICENSE','NOTICE.md','README.md','ANLEITUNG.md','ADAPTER_PROTOCOL.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $document) -Destination $destination -Force
}
Write-Output "Build fertig: $destination"

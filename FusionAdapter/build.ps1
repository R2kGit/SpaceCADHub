# SPDX-License-Identifier: GPL-3.0-only
$ErrorActionPreference = 'Stop'
$fusionCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fusionWpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
& $fusionCompiler /nologo /target:exe /platform:x64 "/out:$PSScriptRoot\SpaceCADHubFusion\FusionNumbers.exe" "/lib:$fusionWpf" /reference:UIAutomationClient.dll /reference:UIAutomationTypes.dll /reference:WindowsBase.dll /reference:System.Web.Extensions.dll "$PSScriptRoot\FusionNumbers.cs"
if ($LASTEXITCODE -ne 0) { throw 'FusionNumbers build failed' }

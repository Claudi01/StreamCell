param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Instale o .NET Framework 4.8 para compilar.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'StreamCell.exe'
$vendor = Join-Path $PSScriptRoot 'vendor/webview2'
if (-not (Test-Path -LiteralPath (Join-Path $vendor 'Microsoft.Web.WebView2.Core.dll'))) { throw 'Arquivos do WebView2 ausentes em vendor/webview2.' }
$compileArgs = @('/nologo','/target:winexe','/optimize+','/platform:x64',('/out:' + $output),('/win32icon:' + (Join-Path $PSScriptRoot 'StreamCell.ico')),('/win32manifest:' + (Join-Path $PSScriptRoot 'StreamCell.manifest')), '/reference:System.Web.Extensions.dll','/reference:System.Windows.Forms.dll','/reference:System.Security.dll',('/reference:' + (Join-Path $vendor 'Microsoft.Web.WebView2.Core.dll')),('/reference:' + (Join-Path $vendor 'Microsoft.Web.WebView2.WinForms.dll')))
foreach ($asset in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'wwwroot') -File) { $compileArgs += '/resource:' + $asset.FullName + ',StreamCell.Assets.' + $asset.Name }
foreach ($asset in Get-ChildItem -LiteralPath $vendor -File) { $compileArgs += '/resource:' + $asset.FullName + ',StreamCell.Runtime.' + $asset.Name }
foreach ($source in @('Program.cs','ConfigStore.cs','DesktopActions.cs','ObsBridge.cs','DesktopHost.cs')) { $compileArgs += Join-Path $PSScriptRoot $source }
& $compiler @compileArgs
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }
Write-Output ('Compilado: ' + $output)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x x64 compiler was not found.' }
$buildDirectory = Join-Path $projectRoot 'build'
New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
$output = Join-Path $buildDirectory 'Trainer.RenderTests.exe'
$references = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Xml.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$compilerArgs = @('/nologo','/target:exe','/platform:x64','/optimize+','/utf8output','/codepage:65001','/main:ParanormalTrainer.RenderUi',('/out:' + $output),('/resource:' + (Join-Path $projectRoot 'src\features.json') + ',features.json'))
foreach ($reference in $references) { $compilerArgs += '/reference:' + (Join-Path $framework $reference) }
$compilerArgs += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$compilerArgs += Join-Path $projectRoot 'tests\RenderUi.cs'
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'UI render test compilation failed.' }
& $output $projectRoot
if ($LASTEXITCODE -ne 0) { throw 'UI render checks failed.' }

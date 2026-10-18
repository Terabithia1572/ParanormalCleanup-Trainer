param([switch]$Test, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x x64 compiler was not found.' }
$buildDirectory = Join-Path $projectRoot 'build'
New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = $projectRoot }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Xml.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output','/codepage:65001',('/win32manifest:' + (Join-Path $projectRoot 'src\app.manifest')),('/out:' + (Join-Path $OutputDirectory 'ParanormalCleanup-Trainer.exe')),('/resource:' + (Join-Path $projectRoot 'src\features.json') + ',features.json'))
foreach ($reference in $references) { $compilerArgs += '/reference:' + (Join-Path $framework $reference) }
$compilerArgs += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Trainer compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\app.config') -Destination (Join-Path $OutputDirectory 'ParanormalCleanup-Trainer.exe.config') -Force
if ($Test) {
    $testArgs = @('/nologo','/target:exe','/platform:x64','/optimize+','/utf8output','/codepage:65001','/define:TESTING','/main:ParanormalTrainer.TrainerTests',('/out:' + (Join-Path $buildDirectory 'Trainer.Tests.exe')),('/resource:' + (Join-Path $projectRoot 'src\features.json') + ',features.json'))
    foreach ($reference in $references) { $testArgs += '/reference:' + (Join-Path $framework $reference) }
    $testArgs += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
    $testArgs += Join-Path $projectRoot 'tests\TrainerTests.cs'
    & $compiler @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & (Join-Path $buildDirectory 'Trainer.Tests.exe') $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'Trainer tests failed.' }
}
Get-Item -LiteralPath (Join-Path $OutputDirectory 'ParanormalCleanup-Trainer.exe') | Select-Object FullName,Length
Get-FileHash -LiteralPath (Join-Path $OutputDirectory 'ParanormalCleanup-Trainer.exe') -Algorithm SHA256

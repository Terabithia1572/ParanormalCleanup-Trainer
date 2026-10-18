param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = $projectRoot }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $OutputDirectory
$files = @(
    (Join-Path $OutputDirectory 'ParanormalCleanup-Trainer.exe'),
    (Join-Path $OutputDirectory 'ParanormalCleanup-Trainer.exe.config'),
    (Join-Path $projectRoot 'KULLANIM.txt')
)
$archive = Join-Path $OutputDirectory 'ParanormalCleanup-Trainer.zip'
Compress-Archive -LiteralPath $files -DestinationPath $archive -Force
Get-FileHash -LiteralPath $archive -Algorithm SHA256

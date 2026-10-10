<#
.SYNOPSIS
  Runs the xUnit suite with coverlet coverage and prints a per-assembly summary.
.DESCRIPTION
  Results go to artifacts/coverage/<run>/coverage.cobertura.xml (git-ignored).
  Fails if line coverage drops below the targets: Core 90%, Runtime 90%, Avalonia 70%.
  -Uncovered lists every uncovered line. Runs headless; no window or mouse is used.
#>
param([switch]$Uncovered, [string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $results = Join-Path $PSScriptRoot 'artifacts/coverage'
    if (Test-Path $results) { Remove-Item $results -Recurse -Force }
    dotnet test tests/Pangaea.Dock.UnitTests/Pangaea.Dock.UnitTests.csproj -c $Configuration -m:1 -p:UseSharedCompilation=false `
        -p:UsedAvaloniaProducts= --nologo --collect:"XPlat Code Coverage" `
        --settings tests/Pangaea.Dock.UnitTests/coverage.runsettings --results-directory $results
    if ($LASTEXITCODE) { throw 'Tests failed.' }

    $file = Get-ChildItem $results -Recurse -Filter coverage.cobertura.xml | Select-Object -First 1
    if (-not $file) { throw 'No coverage report was produced.' }
    [xml]$report = Get-Content $file.FullName
    $targets = @{ 'Pangaea.Dock.Core' = 90; 'Pangaea.Dock.Runtime' = 90; 'Pangaea.Dock.Avalonia' = 70 }
    $failed = $false
    Write-Host ''
    Write-Host ('{0,-24} {1,8} {2,8} {3,10} {4,8}' -f 'Assembly', 'Line %', 'Branch %', 'Lines', 'Target')
    foreach ($package in $report.coverage.packages.package) {
        $lines = @($package.SelectNodes('.//class/lines/line'))
        $covered = @($lines | Where-Object { [int]$_.hits -gt 0 }).Count
        $line = [math]::Round([double]$package.'line-rate' * 100, 1)
        $branch = [math]::Round([double]$package.'branch-rate' * 100, 1)
        $target = $targets[$package.name]
        $mark = if ($target -and $line -lt $target) { $failed = $true; 'BELOW' } else { 'ok' }
        Write-Host ('{0,-24} {1,8} {2,8} {3,10} {4,8}' -f $package.name, $line, $branch, "$covered/$($lines.Count)", "$target% $mark")
        if ($Uncovered) {
            foreach ($class in $package.classes.class) {
                foreach ($l in $class.lines.line | Where-Object { $_.hits -eq '0' }) {
                    Write-Host ('    {0}:{1}' -f (Split-Path $class.filename -Leaf), $l.number)
                }
            }
        }
    }
    Write-Host "Report: $($file.FullName)"
    if ($failed) { throw 'Coverage is below target.' }
} finally { Pop-Location }

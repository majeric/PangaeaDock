param([switch]$Offline)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $feed = Join-Path $PSScriptRoot 'artifacts/packages'
    foreach ($package in @('Core', 'Runtime', 'Avalonia')) {
        dotnet pack "src/Pangaea.Dock.$package" -c Release -o $feed -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:UsedAvaloniaProducts= --nologo -v minimal
        if ($LASTEXITCODE) { throw "Could not pack $package." }
    }
    # Copy only the consumer sources into an isolated directory. No sibling source/project references.
    $consumer = Join-Path $PSScriptRoot ('artifacts/isolated-consumer/' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $consumer -Force | Out-Null
    Copy-Item -LiteralPath 'tests/Pangaea.Dock.PackageSmoke/Pangaea.Dock.PackageSmoke.csproj' -Destination $consumer
    Copy-Item -LiteralPath 'tests/Pangaea.Dock.PackageSmoke/Program.cs' -Destination $consumer
    $cache = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget/packages'
    $restoreSources = @('--source', $feed, '--source', $cache)
    if (-not $Offline) { $restoreSources += @('--source', 'https://api.nuget.org/v3/index.json') }
    $packageCache = Join-Path $consumer 'packages'
    dotnet restore $consumer --packages $packageCache @restoreSources
    if ($LASTEXITCODE) { throw 'Package-only restore failed.' }
    dotnet build $consumer -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:UsedAvaloniaProducts= --nologo -v minimal
    if ($LASTEXITCODE) { throw 'Package-only consumer build failed.' }
    dotnet (Join-Path $consumer 'bin/Release/net10.0/Pangaea.Dock.PackageSmoke.dll')
    if ($LASTEXITCODE) { throw 'Package-only render check failed.' }
} finally { Pop-Location }

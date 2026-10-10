$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build PangaeaDock.slnx -c Release -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:UsedAvaloniaProducts= --nologo -v minimal
    if ($LASTEXITCODE) { throw 'Build failed.' }
    dotnet tests/Pangaea.Dock.Tests/bin/Release/net10.0/Pangaea.Dock.Tests.dll
    if ($LASTEXITCODE) { throw 'Core/runtime verification failed.' }
    dotnet tests/Pangaea.Dock.Harness/bin/Release/net10.0/Pangaea.Dock.Harness.dll artifacts/ui
    if ($LASTEXITCODE) { throw 'Headless verification failed.' }
} finally { Pop-Location }

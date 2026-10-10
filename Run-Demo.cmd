@echo off
setlocal
pushd "%~dp0"
dotnet build samples\Pangaea.Dock.Demo\Pangaea.Dock.Demo.csproj -c Release -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:UsedAvaloniaProducts= --nologo -v minimal
if errorlevel 1 (
  echo.
  echo The demo could not be built. The .NET 10 SDK is required.
  pause
  popd
  exit /b 1
)
start "" "%~dp0samples\Pangaea.Dock.Demo\bin\Release\net10.0\Pangaea.Dock.Demo.exe"
popd

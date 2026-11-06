@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
 echo Install the .NET 10 SDK, then run this file again.
 pause
 exit /b 1
)
dotnet publish src\Trainer.Desktop\Trainer.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts\windows
if errorlevel 1 (
 echo BUILD FAILED
 pause
 exit /b 1
)
echo Ready: artifacts\windows\Trainer Studio.exe
pause

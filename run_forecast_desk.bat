@echo off
setlocal
cd /d "%~dp0"
set "EXE=%~dp0bin\Debug\net8.0-windows\ForecastDesk.exe"
if exist "%EXE%" (
  start "" "%EXE%"
) else (
  dotnet run
  if errorlevel 1 pause
)

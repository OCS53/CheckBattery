@echo off
setlocal
dotnet run --project "%~dp0src\CheckBattery\CheckBattery.csproj" -c Release -- %*

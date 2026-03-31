@echo off
setlocal
set "ROOT=%~dp0.."
set "DB_CONFIG_PATH=%ROOT%\local-databases.json"
"C:\Program Files\dotnet\dotnet.exe" exec "%ROOT%\bin\Debug\net10.0\DatabaseMcpServer.dll"

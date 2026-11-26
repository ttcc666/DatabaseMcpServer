param([ValidateSet('net10.0')][string]$Framework = 'net10.0', [string]$Output = 'artifacts/performance/latest.json')
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet run --project 'benchmarks/DatabaseMcpServer.Benchmarks/DatabaseMcpServer.Benchmarks.csproj' -c 'Release' -f $Framework -- $Output
    if ($LASTEXITCODE -ne 0) { throw 'Performance harness failed.' }
}
finally { Pop-Location }

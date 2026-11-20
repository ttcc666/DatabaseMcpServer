param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

Push-Location (Split-Path -Parent $PSScriptRoot)

try {
    dotnet build 'DatabaseMcpServer.slnx'
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }

    if (-not $SkipTests) {
        dotnet test 'DatabaseMcpServer.slnx' --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Solution tests failed.' }
    }
}
finally {
    Pop-Location
}

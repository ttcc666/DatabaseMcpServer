param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

Push-Location (Split-Path -Parent $PSScriptRoot)

try {
    dotnet build 'DatabaseMcpServer.csproj'

    if (-not $SkipTests) {
        dotnet test 'DatabaseMcpServer.Tests\DatabaseMcpServer.Tests.csproj'
    }
}
finally {
    Pop-Location
}

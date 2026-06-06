param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

Push-Location (Split-Path -Parent $PSScriptRoot)

try {
    dotnet build 'src\DatabaseMcpServer\DatabaseMcpServer.csproj'

    if (-not $SkipTests) {
        dotnet test 'tests\DatabaseMcpServer.Tests\DatabaseMcpServer.Tests.csproj'
    }
}
finally {
    Pop-Location
}

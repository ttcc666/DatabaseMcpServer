# Requires PowerShell 7, Docker with Linux containers, .NET 9/10 SDKs and Node.js.
[CmdletBinding()]
param(
    [ValidateSet('net9.0', 'net10.0')]
    [ValidateNotNullOrEmpty()]
    [string[]] $Framework = @('net9.0', 'net10.0'),
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$runId = 'dbmcp-it-' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$containers = [System.Collections.Generic.List[string]]::new()
$savedEnvironment = @{}
$repository = Split-Path -Parent $PSScriptRoot

function Set-TestEnvironment([string] $Name, [string] $Value) {
    if (-not $savedEnvironment.ContainsKey($Name)) {
        $savedEnvironment[$Name] = [Environment]::GetEnvironmentVariable($Name, 'Process')
    }
    [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
}

function Invoke-Docker([string[]] $Arguments) {
    $output = & docker @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Docker operation failed. Inspect Docker Desktop (credentials are not printed).' }
    return $output
}

function Start-TestContainer([string] $Provider, [string] $Image, [int] $Port,
    [string[]] $EnvironmentNames, [string] $HealthCommand, [string] $Memory) {
    $name = $runId + '-' + $Provider
    # Register before run so finally also handles a partially started container.
    $containers.Add($name)
    $arguments = @('run', '--detach', '--name', $name, '--label', ('dbmcp.integration=' + $runId),
        '--memory', $Memory, '--publish', ('127.0.0.1::' + $Port),
        '--health-cmd', $HealthCommand, '--health-interval', '2s', '--health-timeout', '5s', '--health-retries', '90')
    foreach ($environmentName in $EnvironmentNames) { $arguments += @('--env', $environmentName) }
    $null = Invoke-Docker ($arguments + $Image)
    return $name
}

function Wait-TestContainer([string] $Name) {
    $deadline = [DateTime]::UtcNow.AddMinutes(4)
    do {
        $state = (Invoke-Docker @('inspect', '--format', '{{json .State}}', $Name)) | ConvertFrom-Json
        if (-not $state.Running) { throw ('Test container exited: ' + $Name) }
        if ($state.Health.Status -eq 'healthy') { return }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)
    throw ('Readiness deadline exceeded: ' + $Name)
}

function Get-TestPort([string] $Name, [int] $Port) {
    $binding = [string](Invoke-Docker @('port', $Name, ($Port.ToString() + '/tcp')))
    if ($binding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Expected a loopback-only test port.' }
    return $Matches[1]
}

Push-Location $repository
try {
    $os = [string](Invoke-Docker @('info', '--format', '{{.OSType}}'))
    if ($os -ne 'linux') { throw 'The integration suite requires Docker Linux containers.' }
    $password = 'Dbmcp!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    Set-TestEnvironment 'MYSQL_ROOT_PASSWORD' $password
    Set-TestEnvironment 'MYSQL_ROOT_HOST' '%'
    Set-TestEnvironment 'MYSQL_DATABASE' 'dbmcp_test'
    Set-TestEnvironment 'POSTGRES_PASSWORD' $password
    Set-TestEnvironment 'POSTGRES_DB' 'dbmcp_test'
    Set-TestEnvironment 'MSSQL_SA_PASSWORD' $password
    Set-TestEnvironment 'ACCEPT_EULA' 'Y'
    Set-TestEnvironment 'MSSQL_PID' 'Developer'
    Set-TestEnvironment 'MSSQL_MEMORY_LIMIT_MB' '2048'

    Write-Host 'Starting isolated MySQL 8.4, PostgreSQL 17 and SQL Server 2022 containers...'
    $mysql = Start-TestContainer 'mysql' 'mysql:8.4' 3306 @('MYSQL_ROOT_PASSWORD', 'MYSQL_ROOT_HOST', 'MYSQL_DATABASE') 'mysqladmin ping -h 127.0.0.1 --silent' '1g'
    $postgres = Start-TestContainer 'postgres' 'postgres:17-alpine' 5432 @('POSTGRES_PASSWORD', 'POSTGRES_DB') 'pg_isready -U postgres -d dbmcp_test' '512m'
    $sqlserver = Start-TestContainer 'sqlserver' 'mcr.microsoft.com/mssql/server:2022-latest' 1433 @('MSSQL_SA_PASSWORD', 'ACCEPT_EULA', 'MSSQL_PID', 'MSSQL_MEMORY_LIMIT_MB') '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -Q "SELECT 1" -b -o /dev/null' '3g'
    foreach ($imageName in @('mysql:8.4', 'postgres:17-alpine', 'mcr.microsoft.com/mssql/server:2022-latest')) {
        Write-Host ([string](Invoke-Docker @('image', 'inspect', '--format', '{{json .RepoDigests}}', $imageName)))
    }
    foreach ($name in $containers) { Wait-TestContainer $name }
    $null = Invoke-Docker @('exec', $sqlserver, 'sh', '-c', '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -Q "CREATE DATABASE dbmcp_test" -b -o /dev/null')

    $mysqlPort = Get-TestPort $mysql 3306
    $postgresPort = Get-TestPort $postgres 5432
    $sqlserverPort = Get-TestPort $sqlserver 1433
    Set-TestEnvironment 'DBMCP_TEST_MYSQL' ('Server=127.0.0.1;Port=' + $mysqlPort + ';Database=dbmcp_test;User ID=root;Password=' + $password + ';SslMode=None;Connection Timeout=5;')
    Set-TestEnvironment 'DBMCP_TEST_POSTGRESQL' ('Host=127.0.0.1;Port=' + $postgresPort + ';Database=dbmcp_test;Username=postgres;Password=' + $password + ';Timeout=5;')
    Set-TestEnvironment 'DBMCP_TEST_SQLSERVER' ('Server=127.0.0.1,' + $sqlserverPort + ';Database=dbmcp_test;User ID=sa;Password=' + $password + ';Encrypt=False;TrustServerCertificate=True;Connection Timeout=5;')
    Set-TestEnvironment 'DBMCP_RUN_INTEGRATION' '1'
    foreach ($target in $Framework) {
        Write-Host ('Running provider integration tests: ' + $target)
        $resultsDirectory = 'TestResults/integration/' + $target
        $reportName = $runId + '.trx'
        & dotnet test 'tests/DatabaseMcpServer.Tests/DatabaseMcpServer.Tests.csproj' -c $Configuration -f $target --filter 'Category=Integration' --logger ('trx;LogFileName=' + $reportName) --results-directory $resultsDirectory --blame-hang --blame-hang-timeout '3min'
        if ($LASTEXITCODE -ne 0) { throw ('Provider integration tests failed: ' + $target) }
        [xml] $report = Get-Content -LiteralPath (Join-Path $resultsDirectory $reportName) -Raw
        $counts = $report.TestRun.ResultSummary.Counters
        if ([int]$counts.total -lt 1 -or [int]$counts.passed -ne [int]$counts.total) {
            throw ('Expected executed, passing integration tests without skips: ' + $target)
        }
    }
}
finally {
    $cleanupFailed = $false
    foreach ($name in $containers) {
        $owner = & docker inspect '--format' '{{index .Config.Labels "dbmcp.integration"}}' $name 2>$null
        if ($LASTEXITCODE -eq 0 -and $owner -eq $runId) {
            $null = & docker rm '--force' '--volumes' $name
            if ($LASTEXITCODE -ne 0) { Write-Warning ('Could not remove test container: ' + $name); $cleanupFailed = $true }
        }
    }
    foreach ($entry in $savedEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
    Pop-Location
    if ($cleanupFailed) { throw 'Integration container cleanup was incomplete.' }
    Write-Host 'Removed this run''s test containers and restored process environment.'
}

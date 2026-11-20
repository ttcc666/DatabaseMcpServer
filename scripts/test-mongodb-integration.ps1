# Requires PowerShell 7, Docker Linux containers, .NET 9/10 SDKs and Node.js.
[CmdletBinding()]
param(
    [ValidateSet('net9.0', 'net10.0')][string[]] $Framework = @('net9.0', 'net10.0'),
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$runId = 'dbmcp-mongo-' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$saved = @{}
function Set-TestEnvironment([string] $Name, [string] $Value) {
    if (-not $saved.ContainsKey($Name)) { $saved[$Name] = [Environment]::GetEnvironmentVariable($Name, 'Process') }
    [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
}
function Invoke-Docker([string[]] $Arguments) {
    $output = & docker @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Mongo test Docker operation failed; credentials are not printed.' }
    return $output
}
Push-Location $repository
try {
    if ([string](Invoke-Docker @('info', '--format', '{{.OSType}}')) -ne 'linux') { throw 'Docker Linux containers required.' }
    $password = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    Set-TestEnvironment 'MONGO_INITDB_ROOT_USERNAME' 'dbmcp_test'
    Set-TestEnvironment 'MONGO_INITDB_ROOT_PASSWORD' $password
    $null = Invoke-Docker @('run', '--detach', '--name', $runId, '--label', ('dbmcp.mongodb=' + $runId),
        '--memory', '1g', '--publish', '127.0.0.1::27017', '--env', 'MONGO_INITDB_ROOT_USERNAME', '--env', 'MONGO_INITDB_ROOT_PASSWORD',
        'mongo:8.0', '--setParameter', 'enableTestCommands=1')
    Write-Host ([string](Invoke-Docker @('image', 'inspect', '--format', '{{json .RepoDigests}}', 'mongo:8.0')))
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    do {
        # Use container environment so neither password nor full URI appears in process arguments or logs.
        $ready = & docker exec $runId mongosh '--quiet' '--eval' 'const c = new Mongo("mongodb://" + process.env.MONGO_INITDB_ROOT_USERNAME + ":" + process.env.MONGO_INITDB_ROOT_PASSWORD + "@127.0.0.1/admin"); quit(c.getDB("admin").runCommand({ping:1}).ok === 1 ? 0 : 1)' 2>$null
        if ($LASTEXITCODE -eq 0) { break }
        if ([DateTime]::UtcNow -ge $deadline) { throw 'Mongo readiness deadline exceeded.' }
        Start-Sleep -Seconds 2
    } while ($true)
    $binding = [string](Invoke-Docker @('port', $runId, '27017/tcp'))
    if ($binding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Expected loopback-only Mongo port.' }
    Set-TestEnvironment 'DBMCP_TEST_MONGODB' ('mongodb://dbmcp_test:' + $password + '@127.0.0.1:' + $Matches[1] + '/dbmcp_test?authSource=admin&serverSelectionTimeoutMS=5000')
    Set-TestEnvironment 'DBMCP_RUN_MONGODB_INTEGRATION' '1'
    foreach ($target in $Framework) {
        $directory = 'TestResults/mongodb/' + $target
        $reportName = $runId + '.trx'
        & dotnet test 'tests/DatabaseMcpServer.Tests/DatabaseMcpServer.Tests.csproj' -c $Configuration -f $target --filter 'Category=MongoIntegration' --logger ('trx;LogFileName=' + $reportName) --results-directory $directory --blame-hang --blame-hang-timeout '2min'
        if ($LASTEXITCODE -ne 0) { throw ('Mongo integration tests failed: ' + $target) }
        [xml] $report = Get-Content -LiteralPath (Join-Path $directory $reportName) -Raw
        $counts = $report.TestRun.ResultSummary.Counters
        if ([int]$counts.total -lt 1 -or [int]$counts.passed -ne [int]$counts.total) { throw 'Expected executed passing Mongo tests without skips.' }
    }
}
finally {
    $owner = & docker inspect '--format' '{{index .Config.Labels "dbmcp.mongodb"}}' $runId 2>$null
    $cleanupFailed = $false
    if ($LASTEXITCODE -eq 0 -and $owner -eq $runId) {
        $null = & docker rm '--force' '--volumes' $runId
        $cleanupFailed = $LASTEXITCODE -ne 0
    }
    foreach ($item in $saved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($item.Key, $item.Value, 'Process') }
    Pop-Location
    if ($cleanupFailed) { throw 'Mongo test container cleanup incomplete.' }
    Write-Host 'Removed this run''s Mongo container and restored process environment.'
}

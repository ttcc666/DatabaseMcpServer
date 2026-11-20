# Shared by local package smoke tests and the publishing workflow.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackagePath)

$ErrorActionPreference = 'Stop'
$packageFile = Get-Item -LiteralPath $PackagePath
if ($packageFile.Length -gt 250000000) { throw 'Package exceeds the NuGet.org size limit.' }
Write-Host ('Package size: {0:N1} MiB' -f ($packageFile.Length / 1MB))
$archive = [IO.Compression.ZipFile]::OpenRead($packageFile.FullName)
try {
    $specReader = [IO.StreamReader]::new($archive.GetEntry('DatabaseMcpServer.nuspec').Open())
    try { [xml]$spec = $specReader.ReadToEnd() }
    finally { $specReader.Dispose() }
    if ($spec.SelectNodes('//*[local-name()="dependency"]').Count -ne 0) {
        throw 'The tool package must carry its runtime assemblies without NuGet dependency declarations.'
    }
    $frameworks = @($spec.SelectNodes('//*[local-name()="group"]/@targetFramework') | ForEach-Object { $_.Value })
    if ($frameworks.Count -ne 1 -or $frameworks[0] -ne 'net10.0') {
        throw 'Package metadata must reference only the net10 framework.'
    }

    $manifestReader = [IO.StreamReader]::new($archive.GetEntry('.mcp/server.json').Open())
    try { $manifest = $manifestReader.ReadToEnd() | ConvertFrom-Json }
    finally { $manifestReader.Dispose() }
    if ($manifest.tools.Count -ne 62 -or @($manifest.tools.name | Select-Object -Unique).Count -ne 62) {
        throw 'Packaged MCP catalog must contain 62 unique current tools.'
    }
    if (-not $archive.GetEntry('Doc/mongodb-collections.md')) { throw 'Mongo collection contract is missing from package.' }
    foreach ($assembly in @('DatabaseMcpServer.Core.dll', 'DatabaseMcpServer.Infrastructure.dll')) {
        if (-not $archive.GetEntry('tools/net10.0/any/' + $assembly)) {
            throw ('Architecture assembly missing from package: ' + $assembly)
        }
    }
    $runtimeFiles = @($archive.Entries.FullName | Where-Object { $_ -match '^tools/[^/]+/any/DatabaseMcpServer\.dll$' })
    if ($runtimeFiles.Count -ne 1 -or $runtimeFiles[0] -ne 'tools/net10.0/any/DatabaseMcpServer.dll') {
        throw 'Release package must contain only the net10 tool runtime.'
    }
    foreach ($rid in @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx')) {
        if (-not ($archive.Entries.FullName -like ('tools/net10.0/any/runtimes/' + $rid + '/native/*duckdb*'))) {
            throw ('DuckDB native runtime missing from package: ' + $rid)
        }
    }
}
finally { $archive.Dispose() }

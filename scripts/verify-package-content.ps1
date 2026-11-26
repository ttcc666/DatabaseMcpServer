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
    if ([string]::IsNullOrWhiteSpace($manifest.description) -or $manifest.description.Length -gt 100) {
        throw 'MCP server description must contain 1-100 characters, as required by the server schema.'
    }
    $sourceManifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '../.mcp/server.json') | ConvertFrom-Json
    $toolNames = @($manifest.tools.name)
    if ($toolNames.Count -eq 0 -or @($toolNames | Sort-Object -CaseSensitive -Unique).Count -ne $toolNames.Count -or
        (Compare-Object -ReferenceObject $sourceManifest.tools.name -DifferenceObject $toolNames -CaseSensitive)) {
        throw 'Packaged MCP catalog must match the source catalog and contain unique tool names.'
    }
    $historicalFiles = @($archive.Entries.FullName | Where-Object { $_ -match '^Doc/.+(?:-validation\.md|\.json)$' })
    if ($historicalFiles.Count) { throw ('Historical reports must stay out of the package: ' + ($historicalFiles -join ', ')) }
    if (-not $archive.GetEntry('Doc/tool-evolution.md')) { throw 'Tool migration guide is missing from package.' }
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

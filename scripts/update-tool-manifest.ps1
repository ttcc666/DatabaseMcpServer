param(
    [string] $AssemblyPath = 'src/DatabaseMcpServer/bin/Release/net10.0/DatabaseMcpServer.dll',
    [switch] $Check
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $catalog = & dotnet $AssemblyPath 'tool' 'list' '--json' '--all'
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read tool catalog from the selected build.' }
    $discovery = $catalog | ConvertFrom-Json
    $tools = @($discovery.tools | Select-Object 'name', 'description', 'provider', 'requiredAccess', 'analysis', 'supportedProviders', 'unsupportedProviders')
    $path = Join-Path $root '.mcp/server.json'
    $manifest = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($Check) {
        if (($manifest.tools | ConvertTo-Json -Depth 100 -Compress) -cne ($tools | ConvertTo-Json -Depth 100 -Compress)) {
            throw 'Tool manifest differs from the current SDK catalog. Run update-tool-manifest.ps1 after building.'
        }
        Write-Host ('Manifest matches {0} tools.' -f $tools.Count)
    } else {
        $manifest.tools = $tools
        [IO.File]::WriteAllText($path, ($manifest | ConvertTo-Json -Depth 100) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    }
    $capabilities = $discovery.capabilityCatalog
    if ($null -eq $capabilities) { throw 'The selected build does not export capabilityCatalog.' }
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# Generated capability reference')
    $lines.Add('')
    $lines.Add('Generated from tool list --json --all by scripts/update-tool-manifest.ps1. Do not edit by hand.')
    $lines.Add('')
    $lines.Add(('Schema sections: {0}. Defaults: {1}.' -f ($capabilities.schemaSections -join ', '), ($capabilities.defaultSchemaSections -join ', ')))
    $lines.Add('')
    $lines.Add('An allowlist limits a capability to those products. An exclusion list marks known unsupported products; other products remain provider-dependent, not verified. Runtime configuration and permissions still apply.')
    $lines.Add('')
    $lines.Add('| Capability | Allowed products | Excluded products |')
    $lines.Add('| --- | --- | --- |')
    foreach ($rule in $capabilities.capabilities) {
        $lines.Add(('| {0} | {1} | {2} |' -f $rule.name, ($rule.supportedProviders -join ', '), ($rule.unsupportedProviders -join ', ')))
    }
    foreach ($provider in $capabilities.providers) {
        $lines.Add('')
        $lines.Add(('## {0}' -f $provider.name))
        $lines.Add('')
        $lines.Add('| Group | Feature | Support |')
        $lines.Add('| --- | --- | --- |')
        foreach ($group in $provider.details.PSObject.Properties) {
            foreach ($feature in $group.Value.PSObject.Properties) {
                $lines.Add(('| {0} | {1} | {2} |' -f $group.Name, $feature.Name, $feature.Value))
            }
        }
    }
    $referencePath = Join-Path $root 'Doc/capabilities.generated.md'
    $reference = ($lines -join "`n") + "`n"
    if ($Check) {
        if (!(Test-Path -LiteralPath $referencePath) -or (Get-Content -LiteralPath $referencePath -Raw).Replace("`r`n", "`n") -cne $reference) {
            throw 'Capability reference differs from the current declarations. Run update-tool-manifest.ps1 after building.'
        }
        Write-Host 'Capability reference matches current declarations.'
    } else {
        [IO.File]::WriteAllText($referencePath, $reference, [Text.UTF8Encoding]::new($false))
    }
} finally { Pop-Location }

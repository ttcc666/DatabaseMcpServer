# Requires PowerShell 7, Python 3, .NET 10 SDK and Node.js. No publishing or global installation.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$scratch = Join-Path $temporaryRoot ('dbmcp-package-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $scratch
$version = '5.0.0-ci.' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
Push-Location $repository
try {
    $packages = Join-Path $scratch 'packages'
    $install = Join-Path $scratch 'tool'
    # Isolate package restore assets from regular builds and IDE background restores.
    & dotnet pack 'src/DatabaseMcpServer/DatabaseMcpServer.csproj' -c 'Release' '-p:BuildWebsite=true' --artifacts-path (Join-Path $scratch 'build') -o $packages ('-p:PackageVersion=' + $version)
    if ($LASTEXITCODE -ne 0) { throw 'Local package build failed.' }
    $packageFile = Get-Item -LiteralPath (Join-Path $packages ('DatabaseMcpServer.' + $version + '.nupkg'))
    & (Join-Path $PSScriptRoot 'verify-package-content.ps1') -PackagePath $packageFile.FullName
    & dotnet tool install 'DatabaseMcpServer' --tool-path $install --version $version --add-source $packages --framework 'net10.0' --no-cache
    if ($LASTEXITCODE -ne 0) { throw 'Local package installation failed.' }
    $entry = Join-Path $install $(if ($IsWindows) { 'DatabaseMcpServer.exe' } else { 'DatabaseMcpServer' })
    & python (Join-Path $PSScriptRoot 'test-package.py') $entry
    if ($LASTEXITCODE -ne 0) { throw 'Installed CLI/stdio smoke test failed.' }
    # Copy only the selected runtime; never remove a DLL from the installed tool or build output.
    $runtimeDll = Get-ChildItem -LiteralPath $install -Force -Recurse -Filter 'DatabaseMcpServer.dll' |
        Where-Object { $_.FullName -match '[\\/]net10\.0[\\/]' } | Select-Object -First 1
    if (-not $runtimeDll) { throw 'Installed net10 runtime was not found.' }
    $incomplete = Join-Path $scratch 'missing-extension'
    $null = New-Item -ItemType Directory -Path $incomplete
    Get-ChildItem -LiteralPath $runtimeDll.DirectoryName | Where-Object { $_.Name -ne 'SqlSugar.HGCore.dll' } |
        Copy-Item -Destination $incomplete -Recurse
    & python (Join-Path $PSScriptRoot 'test-package.py') (Join-Path $incomplete 'DatabaseMcpServer.dll') '--missing-extension'
    if ($LASTEXITCODE -ne 0) { throw 'Missing extension isolation smoke test failed.' }
}
finally {
    Pop-Location
    # This exact directory was created above; never delete the tool or repository root.
    $resolved = [IO.Path]::GetFullPath($scratch)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd([IO.Path]::DirectorySeparatorChar) -eq $temporaryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) -and
        [IO.Path]::GetFileName($resolved).StartsWith('dbmcp-package-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

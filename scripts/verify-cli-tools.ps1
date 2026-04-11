param(
    [string]$Framework = 'net9.0',
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

Push-Location (Split-Path -Parent $PSScriptRoot)

try {
    $root = Get-Location
    $buildOutput = Join-Path $root ('bin\{0}\{1}' -f $Configuration, $Framework)
    $exePath = Join-Path $buildOutput 'DatabaseMcpServer.exe'

    dotnet build 'DatabaseMcpServer.csproj' --framework $Framework --configuration $Configuration | Out-Host

    $reportRoot = Join-Path $root 'artifacts\cli-tool-verify'
    $sessionName = Get-Date -Format 'yyyyMMdd-HHmmss'
    $sessionRoot = Join-Path $reportRoot $sessionName
    $dataRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('dbmcp-cli-verify-' + $sessionName)
    $templateRoot = Join-Path $dataRoot 'templates'
    $runRoot = Join-Path $dataRoot 'runs'
    $reportFile = Join-Path $sessionRoot 'cli-tool-summary.json'
    $markdownFile = Join-Path $sessionRoot 'cli-tool-summary.md'

    New-Item -ItemType Directory -Force -Path $reportRoot, $sessionRoot, $dataRoot, $templateRoot, $runRoot | Out-Null

    $env:PATH = (Join-Path $buildOutput 'runtimes\win-x64\native') + ';' + $env:PATH
    Add-Type -Path (Join-Path $buildOutput 'SQLitePCLRaw.core.dll')
    Add-Type -Path (Join-Path $buildOutput 'SQLitePCLRaw.batteries_v2.dll')
    Add-Type -Path (Join-Path $buildOutput 'SQLitePCLRaw.provider.e_sqlite3.dll')
    Add-Type -Path (Join-Path $buildOutput 'Microsoft.Data.Sqlite.dll')
    [SQLitePCL.Batteries_V2]::Init()

    function New-ConfigFile {
        param(
            [array]$Databases,
            [string]$ConfigPath
        )

        @{ databases = $Databases } |
            ConvertTo-Json -Depth 6 |
            Set-Content -LiteralPath $ConfigPath -Encoding UTF8
    }

    function Invoke-DatabaseTool {
        param(
            [string]$ConfigPath,
            [string[]]$ToolArgs,
            [hashtable]$Environment = @{}
        )

        $allArgs = @('tool') + $ToolArgs
        if (($ToolArgs -notcontains '--config') -and $ConfigPath) {
            $allArgs += @('--config', $ConfigPath)
        }

        $startInfo = [System.Diagnostics.ProcessStartInfo]::new($exePath)
        $startInfo.UseShellExecute = $false
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.CreateNoWindow = $true

        foreach ($arg in $allArgs) {
            [void]$startInfo.ArgumentList.Add($arg)
        }

        foreach ($entry in $Environment.GetEnumerator()) {
            $startInfo.Environment[$entry.Key] = [string]$entry.Value
        }

        $process = [System.Diagnostics.Process]::Start($startInfo)
        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $process.StandardError.ReadToEnd()
        $process.WaitForExit()

        $jsonSuccess = $null
        $parsedJson = $null
        if (-not [string]::IsNullOrWhiteSpace($stdout)) {
            try {
                $parsedJson = $stdout | ConvertFrom-Json -Depth 20
                if ($null -ne $parsedJson.success) {
                    $jsonSuccess = [bool]$parsedJson.success
                }
            }
            catch {
            }
        }

        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Stdout = $stdout.Trim()
            Stderr = $stderr.Trim()
            JsonSuccess = $jsonSuccess
            ParsedJson = $parsedJson
            CommandLine = ($allArgs -join ' ')
        }
    }

    function Assert-InvocationSucceeded {
        param(
            [pscustomobject]$Invocation,
            [string]$Context
        )

        if ($Invocation.ExitCode -ne 0) {
            throw "{0} failed with exit code {1}. stderr: {2}`nstdout: {3}" -f $Context, $Invocation.ExitCode, $Invocation.Stderr, $Invocation.Stdout
        }

        if ($Invocation.JsonSuccess -eq $false) {
            throw "{0} returned success=false. stdout: {1}" -f $Context, $Invocation.Stdout
        }
    }

    function Invoke-SqliteNonQuery {
        param(
            [Microsoft.Data.Sqlite.SqliteConnection]$Connection,
            [string]$Sql
        )

        $command = $Connection.CreateCommand()
        $command.CommandText = $Sql
        [void]$command.ExecuteNonQuery()
    }

    function Initialize-TestDatabase {
        param(
            [string]$DatabasePath,
            [string]$Label
        )

        if (Test-Path $DatabasePath) {
            Remove-Item -LiteralPath $DatabasePath -Force
        }

        $connection = [Microsoft.Data.Sqlite.SqliteConnection]::new("Data Source=$DatabasePath;Cache=Shared;Mode=ReadWriteCreate;")
        $connection.Open()

        try {
            $sqlBatches = @(
                @"
CREATE TABLE users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    email TEXT,
    age INTEGER DEFAULT 0,
    status TEXT DEFAULT 'active',
    created_at TEXT DEFAULT CURRENT_TIMESTAMP
);
"@,
                @"
INSERT INTO users (name, email, age, status) VALUES
('alice-$Label', 'alice-$Label@example.com', 30, 'active'),
('bob-$Label', 'bob-$Label@example.com', 24, 'inactive'),
('carol-$Label', 'carol-$Label@example.com', 35, 'active');
"@,
                @"
CREATE TABLE orders (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    amount REAL NOT NULL,
    created_at TEXT DEFAULT CURRENT_TIMESTAMP
);
"@,
                @"
INSERT INTO orders (user_id, amount) VALUES
(1, 19.50),
(1, 88.00),
(2, 42.25);
"@,
                @"
CREATE TABLE audit_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    entity_name TEXT NOT NULL,
    entity_id INTEGER NOT NULL,
    action TEXT NOT NULL,
    created_at TEXT DEFAULT CURRENT_TIMESTAMP
);
"@,
                'CREATE VIEW active_users AS SELECT id, name, email FROM users WHERE status = ''active'';',
                @"
CREATE TRIGGER trg_users_after_insert
AFTER INSERT ON users
BEGIN
    INSERT INTO audit_logs (entity_name, entity_id, action)
    VALUES ('users', NEW.id, 'insert');
END;
"@,
                'CREATE TABLE backup_source (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL);',
                "INSERT INTO backup_source (name) VALUES ('backup-$Label-1'), ('backup-$Label-2');",
                'CREATE TABLE rename_source (id INTEGER PRIMARY KEY AUTOINCREMENT, note TEXT NOT NULL);',
                "INSERT INTO rename_source (note) VALUES ('rename-$Label');",
                'CREATE TABLE truncate_target (id INTEGER PRIMARY KEY AUTOINCREMENT, value_text TEXT NOT NULL);',
                "INSERT INTO truncate_target (value_text) VALUES ('truncate-$Label-1'), ('truncate-$Label-2');",
                @"
CREATE TABLE column_ops (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    rename_me TEXT,
    drop_me TEXT,
    modify_me TEXT,
    default_target TEXT
);
"@,
                "INSERT INTO column_ops (rename_me, drop_me, modify_me, default_target) VALUES ('rename-$Label', 'drop-$Label', 'modify-$Label', NULL);",
                @"
CREATE TABLE index_target (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    index_col TEXT NOT NULL,
    value_col TEXT NOT NULL
);
"@,
                @"
INSERT INTO index_target (index_col, value_col) VALUES
('idx-$Label-a', 'value-a'),
('idx-$Label-b', 'value-b');
"@,
                'CREATE UNIQUE INDEX ux_drop_constraint_target ON index_target(index_col);',
                'CREATE TABLE pk_target (code TEXT NOT NULL, description TEXT);',
                'CREATE TABLE remark_target (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT NOT NULL, details TEXT);',
                "INSERT INTO remark_target (title, details) VALUES ('remark-$Label', 'details-$Label');",
                'CREATE TABLE constraint_target (id INTEGER PRIMARY KEY AUTOINCREMENT, code TEXT NOT NULL, name TEXT NOT NULL, CONSTRAINT uq_constraint_target_name UNIQUE(name));',
                "INSERT INTO constraint_target (code, name) VALUES ('code-$Label', 'name-$Label');",
                'CREATE VIEW view_to_drop AS SELECT id, index_col FROM index_target;'
            )

            foreach ($sql in $sqlBatches) {
                Invoke-SqliteNonQuery -Connection $connection -Sql $sql
            }
        }
        finally {
            $connection.Dispose()
        }
    }

    function New-TestEnvironment {
        param([string]$Name)

        $envDir = Join-Path $runRoot $Name
        if (Test-Path $envDir) {
            Remove-Item -LiteralPath $envDir -Recurse -Force
        }

        New-Item -ItemType Directory -Force -Path $envDir | Out-Null

        $primaryDbPath = Join-Path $envDir 'primary.db'
        $secondaryDbPath = Join-Path $envDir 'secondary.db'
        Copy-Item -LiteralPath (Join-Path $templateRoot 'primary-template.db') -Destination $primaryDbPath
        Copy-Item -LiteralPath (Join-Path $templateRoot 'secondary-template.db') -Destination $secondaryDbPath

        $configPath = Join-Path $envDir 'databases.json'
        $databases = @(
            @{
                name = 'primary'
                connectionString = "Data Source=$primaryDbPath;Cache=Shared;Mode=ReadWriteCreate;"
                dbType = 'Sqlite'
                description = 'CLI primary'
                isDefault = $true
                optimizationSettings = @{
                    enableDefaultValue = 'true'
                    enableDescription = 'true'
                    enableDropColumn = 'true'
                }
            },
            @{
                name = 'secondary'
                connectionString = "Data Source=$secondaryDbPath;Cache=Shared;Mode=ReadWriteCreate;"
                dbType = 'Sqlite'
                description = 'CLI secondary'
                isDefault = $false
                optimizationSettings = @{
                    enableDefaultValue = 'true'
                    enableDescription = 'true'
                    enableDropColumn = 'true'
                }
            }
        )

        New-ConfigFile -Databases $databases -ConfigPath $configPath

        [pscustomobject]@{
            Name = $Name
            Directory = $envDir
            ConfigPath = $configPath
        }
    }

    function New-PathArg {
        param(
            [string]$EnvironmentDirectory,
            [string]$LeafName
        )

        return (Join-Path $EnvironmentDirectory $LeafName)
    }

    Initialize-TestDatabase -DatabasePath (Join-Path $templateRoot 'primary-template.db') -Label 'primary'
    Initialize-TestDatabase -DatabasePath (Join-Path $templateRoot 'secondary-template.db') -Label 'secondary'

    $cases = @(
        @{ Tool = 'test_connection'; Args = @() },
        @{ Tool = 'test_connection_by_name'; Args = @('--database-name', 'secondary') },
        @{ Tool = 'get_database_config'; Args = @() },
        @{ Tool = 'validate_configuration'; Args = @() },
        @{ Tool = 'reload_database_config'; Args = @() },
        @{ Tool = 'list_databases'; Args = @() },
        @{ Tool = 'switch_database'; Args = @('--database-name', 'secondary') },
        @{ Tool = 'get_current_database'; Args = @() },
        @{ Tool = 'health_check'; Args = @() },
        @{ Tool = 'test_connection_with_retry'; Args = @('--max-retries', '1', '--initial-delay-ms', '10') },
        @{ Tool = 'get_data_base_list'; Args = @() },
        @{ Tool = 'get_view_info_list'; Args = @() },
        @{ Tool = 'get_table_info_list'; Args = @() },
        @{ Tool = 'get_column_infos_by_table_name'; Args = @('--table-name', 'users') },
        @{ Tool = 'get_is_identities'; Args = @('--table-name', 'users') },
        @{ Tool = 'get_primaries'; Args = @('--table-name', 'users') },
        @{ Tool = 'is_any_table'; Args = @('--table-name', 'users') },
        @{ Tool = 'is_any_column'; Args = @('--table-name', 'users', '--column-name', 'name') },
        @{ Tool = 'is_any_constraint'; Args = @('--constraint-name', 'uq_constraint_target_name') },
        @{ Tool = 'drop_table'; Args = @('--table-name', 'rename_source', '--yes') },
        @{ Tool = 'truncate_table'; Args = @('--table-name', 'truncate_target', '--yes') },
        @{ Tool = 'backup_table'; Args = @('--old-table-name', 'backup_source', '--new-table-name', 'backup_source_copy', '--yes') },
        @{ Tool = 'rename_table'; Args = @('--old-table-name', 'rename_source', '--new-table-name', 'rename_source_done', '--yes') },
        @{ Tool = 'add_column'; Args = @('--table-name', 'column_ops', '--column-info', '{"DbColumnName":"added_col","DataType":"nvarchar","Length":50,"IsNullable":true}', '--yes') },
        @{ Tool = 'update_column'; Args = @('--table-name', 'column_ops', '--column-info', '{"DbColumnName":"modify_me","DataType":"nvarchar","Length":120,"IsNullable":true}', '--yes') },
        @{ Tool = 'drop_column'; Args = @('--table-name', 'column_ops', '--column-name', 'drop_me', '--yes') },
        @{ Tool = 'rename_column'; Args = @('--table-name', 'column_ops', '--old-column-name', 'rename_me', '--new-column-name', 'renamed_col', '--yes') },
        @{ Tool = 'add_primary_key'; Args = @('--table-name', 'pk_target', '--column-name', 'code', '--yes') },
        @{ Tool = 'drop_constraint'; Args = @('--table-name', 'constraint_target', '--constraint-name', 'uq_constraint_target_name', '--yes') },
        @{ Tool = 'create_index'; Args = @('--table-name', 'index_target', '--index-name', 'ix_index_target_value', '--column-name', 'value_col', '--yes') },
        @{ Tool = 'get_index_list'; Args = @('--table-name', 'index_target') },
        @{ Tool = 'add_default_value'; Args = @('--table-name', 'column_ops', '--column-name', 'default_target', '--default-value', '''N/A''', '--yes') },
        @{ Tool = 'add_table_remark'; Args = @('--table-name', 'remark_target', '--description', 'remark text', '--yes') },
        @{ Tool = 'is_any_table_remark'; Args = @('--table-name', 'remark_target') },
        @{ Tool = 'delete_table_remark'; Args = @('--table-name', 'remark_target', '--yes') },
        @{ Tool = 'add_column_remark'; Args = @('--table-name', 'remark_target', '--column-name', 'title', '--description', 'title remark', '--yes') },
        @{ Tool = 'delete_column_remark'; Args = @('--table-name', 'remark_target', '--column-name', 'title', '--yes') },
        @{ Tool = 'get_proc_list'; Args = @() },
        @{ Tool = 'get_func_list'; Args = @() },
        @{ Tool = 'drop_view'; Args = @('--view-name', 'view_to_drop', '--yes') },
        @{ Tool = 'drop_func'; Args = @('--function-name', 'fn_demo', '--yes') },
        @{ Tool = 'drop_proc'; Args = @('--procedure-name', 'sp_demo', '--yes') },
        @{ Tool = 'get_trigger_names'; Args = @('--table-name', 'users') },
        @{ Tool = 'get_table_schema'; Args = @('--table-name', 'users') },
        @{ Tool = 'sql_query'; Args = @('--sql', 'select * from "users"') },
        @{ Tool = 'sql_query_single'; Args = @('--sql', 'select * from "users" order by "id" limit 1') },
        @{ Tool = 'get_data_set_all'; Args = @('--sql', 'select * from "users"; select * from "orders"') },
        @{ Tool = 'get_scalar'; Args = @('--sql', 'select count(*) from "users"') },
        @{ Tool = 'sql_query_with_in_parameter'; Args = @('--sql', 'select * from "users" where "id" in (@ids)', '--in-parameter-name', 'ids', '--in-values', '[1,2]') },
        @{ Tool = 'execute_command'; Args = @('--sql', 'insert into "users" ("name","email","age","status") values (''cli-user'',''cli@example.com'',28,''active'')', '--yes') },
        @{ Tool = 'call_stored_procedure'; Args = @('--procedure-name', 'sp_demo', '--yes') },
        @{ Tool = 'call_stored_procedure_with_output'; Args = @('--procedure-name', 'sp_demo', '--output-parameters', '["out_value"]', '--yes') },
        @{ Tool = 'execute_command_with_go'; Args = @('--sql', "UPDATE ""users"" SET ""age"" = 31 WHERE ""id"" = 1`nGO", '--yes') },
        @{ Tool = 'batch_execute_commands'; Args = @('--commands', '["update \"users\" set \"age\" = 29 where \"id\" = 1","update \"users\" set \"status\" = \"active\" where \"id\" = 2"]', '--yes') },
        @{ Tool = 'export_query_to_excel'; ArgsFactory = { param($env) @('--sql', 'select * from "users"', '--file-path', (New-PathArg -EnvironmentDirectory $env.Directory -LeafName 'query.xlsx'), '--return-format', 'path') } },
        @{ Tool = 'export_table_to_excel'; ArgsFactory = { param($env) @('--table-name', 'users', '--file-path', (New-PathArg -EnvironmentDirectory $env.Directory -LeafName 'table.xlsx'), '--return-format', 'path') } },
        @{ Tool = 'export_multiple_queries_to_excel'; ArgsFactory = { param($env) @('--queries-json', '{"users":"select * from \"users\"","orders":"select * from \"orders\""}', '--file-path', (New-PathArg -EnvironmentDirectory $env.Directory -LeafName 'multi.xlsx'), '--return-format', 'path') } },
        @{ Tool = 'generate_database_documentation'; ArgsFactory = { param($env) @('--format', 'markdown', '--return-mode', 'path', '--file-path', (New-PathArg -EnvironmentDirectory $env.Directory -LeafName 'database-doc.md')) } }
    )

    $results = foreach ($case in $cases) {
        $name = $case.Tool
        $environment = New-TestEnvironment -Name $name
        $caseArgs = if ($case.ContainsKey('ArgsFactory')) { & $case.ArgsFactory $environment } else { $case.Args }
        $invocation = Invoke-DatabaseTool -ConfigPath $environment.ConfigPath -ToolArgs (@($name) + $caseArgs)

        [pscustomobject]@{
            Tool = $name
            ExitCode = $invocation.ExitCode
            JsonSuccess = $invocation.JsonSuccess
            StdoutLength = $invocation.Stdout.Length
            StderrLength = $invocation.Stderr.Length
            Stdout = $invocation.Stdout
            Stderr = $invocation.Stderr
            CommandLine = $invocation.CommandLine
        }
    }

    $summary = [pscustomobject]@{
        GeneratedAt = (Get-Date).ToString('s')
        Framework = $Framework
        Configuration = $Configuration
        Executable = $exePath
        TotalTools = $results.Count
        ExitCodeZero = @($results | Where-Object ExitCode -eq 0).Count
        ExitCodeNonZero = @($results | Where-Object ExitCode -ne 0).Count
        JsonSuccessTrue = @($results | Where-Object JsonSuccess -eq $true).Count
        JsonSuccessFalse = @($results | Where-Object JsonSuccess -eq $false).Count
        JsonSuccessNull = @($results | Where-Object { $null -eq $_.JsonSuccess }).Count
        Results = $results
    }

    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportFile -Encoding UTF8

    $markdown = New-Object System.Text.StringBuilder
    [void]$markdown.AppendLine('# CLI Tool Verification Summary')
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine(('* GeneratedAt: {0}' -f $summary.GeneratedAt))
    [void]$markdown.AppendLine(('* TotalTools: {0}' -f $summary.TotalTools))
    [void]$markdown.AppendLine(('* ExitCodeZero: {0}' -f $summary.ExitCodeZero))
    [void]$markdown.AppendLine(('* ExitCodeNonZero: {0}' -f $summary.ExitCodeNonZero))
    [void]$markdown.AppendLine(('* JsonSuccessTrue: {0}' -f $summary.JsonSuccessTrue))
    [void]$markdown.AppendLine(('* JsonSuccessFalse: {0}' -f $summary.JsonSuccessFalse))
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('| Tool | ExitCode | JsonSuccess | StdoutLength | StderrLength |')
    [void]$markdown.AppendLine('| --- | --- | --- | --- | --- |')
    foreach ($result in $results) {
        [void]$markdown.AppendLine(('| {0} | {1} | {2} | {3} | {4} |' -f $result.Tool, $result.ExitCode, $result.JsonSuccess, $result.StdoutLength, $result.StderrLength))
    }
    $markdown.ToString() | Set-Content -LiteralPath $markdownFile -Encoding UTF8

    Write-Host ('Summary JSON: {0}' -f $reportFile)
    Write-Host ('Summary Markdown: {0}' -f $markdownFile)
    Write-Host ($summary | ConvertTo-Json -Depth 4)
}
finally {
    Pop-Location
}

using System.Text.Json;
using DatabaseMcpServer.Cli;

namespace DatabaseMcpServer.Tests;

public class CliRunnerTests
{
    [Fact]
    public async Task RunAsync_ShouldListTools_WithoutConfig()
    {
        var runner = new CliRunner();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await runner.RunAsync(["tool", "list"], stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Contains("list_databases", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ShouldRejectProtectedTool_WithoutYesBeforeConfigResolution()
    {
        var runner = new CliRunner();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await runner.RunAsync(["tool", "drop_table", "--table-name", "users"], stdout, stderr);

        Assert.Equal(2, exitCode);
        Assert.Contains("--yes", stderr.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public async Task RunAsync_ShouldExecuteListDatabases_WithExplicitConfig()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Server=localhost;Database=main;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "description": "primary",
                  "isDefault": true
                },
                {
                  "name": "analytics",
                  "connectionString": "Server=localhost;Database=analytics;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "description": "analytics"
                }
              ]
            }
            """);

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(["tool", "list_databases", "--config", configPath], stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(2, document.RootElement.GetProperty("totalDatabases").GetInt32());
            Assert.Equal("primary", document.RootElement.GetProperty("currentDatabase").GetString());
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldExecuteGetDatabaseConfig_WithExplicitConfig()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Server=localhost;Database=main;User Id=sa;Password=secret;",
                  "dbType": "SqlServer",
                  "description": "primary",
                  "isDefault": true
                }
              ]
            }
            """);

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(["tool", "get_database_config", "--config", configPath], stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.True(document.RootElement.GetProperty("configured").GetBoolean());
            Assert.Equal("primary", document.RootElement.GetProperty("currentDatabase").GetString());
            Assert.Contains("Password=****", document.RootElement.GetProperty("connectionString").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldListBuiltInPresets()
    {
        var runner = new CliRunner();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await runner.RunAsync(["config", "presets"], stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr.ToString());

        using var document = JsonDocument.Parse(stdout.ToString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.True(document.RootElement.GetProperty("totalPresets").GetInt32() > 0);
    }

    [Fact]
    public async Task RunAsync_ShouldShowBuiltInPreset()
    {
        var runner = new CliRunner();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await runner.RunAsync(["config", "preset", "--db-type", "Sqlite"], stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stderr.ToString());

        using var document = JsonDocument.Parse(stdout.ToString());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Sqlite", document.RootElement.GetProperty("preset").GetProperty("dbType").GetString());
    }

    [Fact]
    public async Task RunAsync_ShouldCreateConfigEntryFromPreset()
    {
        var configPath = WriteConfigFile("""{ "databases": [] }""");

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(
                ["config", "create", "--config", configPath, "--from-preset", "Sqlite", "--name", "sqlite-dev", "--connection-string", "Data Source=custom.db;", "--description", "custom sqlite", "--set-default"],
                stdout,
                stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("sqlite-dev", document.RootElement.GetProperty("databaseName").GetString());
            Assert.Equal("Sqlite", document.RootElement.GetProperty("dbType").GetString());

            using var configDocument = JsonDocument.Parse(File.ReadAllText(configPath));
            var database = configDocument.RootElement.GetProperty("databases")[0];
            Assert.Equal("sqlite-dev", database.GetProperty("name").GetString());
            Assert.Equal("Sqlite", database.GetProperty("dbType").GetString());
            Assert.Equal("Data Source=custom.db;", database.GetProperty("connectionString").GetString());
            Assert.Equal("custom sqlite", database.GetProperty("description").GetString());
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldPreviewConfigEntryFromPreset_WhenPrintOnlyIsEnabled()
    {
        var configPath = WriteConfigFile("""{ "databases": [] }""");

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(
                ["config", "create", "--config", configPath, "--from-preset", "Sqlite", "--name", "sqlite-preview", "--print-only"],
                stdout,
                stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.True(document.RootElement.GetProperty("printOnly").GetBoolean());

            using var configDocument = JsonDocument.Parse(File.ReadAllText(configPath));
            Assert.Equal(0, configDocument.RootElement.GetProperty("databases").GetArrayLength());
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldCreateConfigFile_WhenInitIsInvoked()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"dbmcp-cli-init-{Guid.NewGuid():N}.json");
        DeleteFileIfExists(configPath);

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(["init", "--config", configPath], stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());
            Assert.True(File.Exists(configPath));

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.True(document.RootElement.GetProperty("created").GetBoolean());
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldManageConfigEntries()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Data Source=primary.db;",
                  "dbType": "Sqlite",
                  "description": "primary",
                  "isDefault": true
                }
              ]
            }
            """);

        try
        {
            var runner = new CliRunner();

            var addStdout = new StringWriter();
            var addStderr = new StringWriter();
            var addExitCode = await runner.RunAsync(
                ["config", "add", "--config", configPath, "--name", "sqlite-local", "--db-type", "Sqlite", "--connection-string", "Data Source=test.db;", "--set-default"],
                addStdout,
                addStderr);

            Assert.Equal(0, addExitCode);
            Assert.Equal(string.Empty, addStderr.ToString());

            var showStdout = new StringWriter();
            var showStderr = new StringWriter();
            var showExitCode = await runner.RunAsync(
                ["config", "show", "--config", configPath, "--name", "sqlite-local"],
                showStdout,
                showStderr);

            Assert.Equal(0, showExitCode);
            Assert.Equal(string.Empty, showStderr.ToString());
            using (var showDocument = JsonDocument.Parse(showStdout.ToString()))
            {
                Assert.Equal("sqlite-local", showDocument.RootElement.GetProperty("database").GetProperty("name").GetString());
                Assert.Equal("Data Source=test.db;", showDocument.RootElement.GetProperty("database").GetProperty("connectionString").GetString());
            }

            var setDefaultStdout = new StringWriter();
            var setDefaultStderr = new StringWriter();
            var setDefaultExitCode = await runner.RunAsync(
                ["config", "use", "--config", configPath, "--name", "primary"],
                setDefaultStdout,
                setDefaultStderr);

            Assert.Equal(0, setDefaultExitCode);
            Assert.Equal(string.Empty, setDefaultStderr.ToString());
            using (var setDefaultDocument = JsonDocument.Parse(setDefaultStdout.ToString()))
            {
                Assert.Equal("primary", setDefaultDocument.RootElement.GetProperty("currentDefaultDatabase").GetString());
            }

            var removeStdout = new StringWriter();
            var removeStderr = new StringWriter();
            var removeExitCode = await runner.RunAsync(
                ["config", "remove", "--config", configPath, "--name", "sqlite-local", "--yes"],
                removeStdout,
                removeStderr);

            Assert.Equal(0, removeExitCode);
            Assert.Equal(string.Empty, removeStderr.ToString());

            var listStdout = new StringWriter();
            var listStderr = new StringWriter();
            var listExitCode = await runner.RunAsync(
                ["config", "list", "--config", configPath],
                listStdout,
                listStderr);

            Assert.Equal(0, listExitCode);
            Assert.Equal(string.Empty, listStderr.ToString());
            using var listDocument = JsonDocument.Parse(listStdout.ToString());
            Assert.Equal(1, listDocument.RootElement.GetProperty("totalDatabases").GetInt32());
            Assert.Equal("primary", listDocument.RootElement.GetProperty("currentDefaultDatabase").GetString());
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRenameAndUpdateConfigEntry()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "sqlite-local",
                  "connectionString": "Data Source=local.db;",
                  "dbType": "Sqlite",
                  "description": "local",
                  "isDefault": true
                }
              ]
            }
            """);

        try
        {
            var runner = new CliRunner();

            var renameStdout = new StringWriter();
            var renameStderr = new StringWriter();
            var renameExitCode = await runner.RunAsync(
                ["config", "rename", "--config", configPath, "--name", "sqlite-local", "--new-name", "sqlite-dev"],
                renameStdout,
                renameStderr);

            Assert.Equal(0, renameExitCode);
            Assert.Equal(string.Empty, renameStderr.ToString());

            var updateStdout = new StringWriter();
            var updateStderr = new StringWriter();
            var updateExitCode = await runner.RunAsync(
                ["config", "update", "--config", configPath, "--name", "sqlite-dev", "--db-type", "SqlServer", "--connection-string", "Server=.;Database=test;User Id=sa;Password=secret;", "--description", "updated"],
                updateStdout,
                updateStderr);

            Assert.Equal(0, updateExitCode);
            Assert.Equal(string.Empty, updateStderr.ToString());

            using var document = JsonDocument.Parse(updateStdout.ToString());
            var database = document.RootElement.GetProperty("database");
            Assert.Equal("sqlite-dev", database.GetProperty("name").GetString());
            Assert.Equal("SqlServer", database.GetProperty("dbType").GetString());
            Assert.Equal("updated", database.GetProperty("description").GetString());
            Assert.Contains("Password=****", database.GetProperty("connectionString").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldClearDescription_WhenRequested()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "sqlite-local",
                  "connectionString": "Data Source=local.db;",
                  "dbType": "Sqlite",
                  "description": "local",
                  "isDefault": true
                }
              ]
            }
            """);

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(
                ["config", "update", "--config", configPath, "--name", "sqlite-local", "--clear-description"],
                stdout,
                stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("database").GetProperty("description").ValueKind);
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldValidateConfigFile()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Data Source=primary.db;",
                  "dbType": "Sqlite",
                  "isDefault": true
                },
                {
                  "name": "secondary",
                  "connectionString": "Data Source=secondary.db;",
                  "dbType": "Sqlite"
                }
              ]
            }
            """);

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(["config", "validate", "--config", configPath], stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(2, document.RootElement.GetProperty("totalDatabases").GetInt32());
            Assert.Empty(document.RootElement.GetProperty("errors").EnumerateArray());
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldFailValidation_WhenDuplicateNamesExist()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Data Source=primary.db;",
                  "dbType": "Sqlite",
                  "isDefault": true
                },
                {
                  "name": "primary",
                  "connectionString": "Data Source=secondary.db;",
                  "dbType": "Sqlite"
                }
              ]
            }
            """);

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(["config", "validate", "--config", configPath], stdout, stderr);

            Assert.Equal(1, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.NotEmpty(document.RootElement.GetProperty("errors").EnumerateArray());
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldCloneConfigEntry()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "sqlite-dev",
                  "connectionString": "Data Source=dev.db;",
                  "dbType": "Sqlite",
                  "description": "dev",
                  "isDefault": true,
                  "optimizationSettings": {
                    "lowercaseTables": "true"
                  }
                }
              ]
            }
            """);

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(
                ["config", "clone", "--config", configPath, "--name", "sqlite-dev", "--new-name", "sqlite-ci"],
                stdout,
                stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("sqlite-ci", document.RootElement.GetProperty("clonedDatabaseName").GetString());

            using var configDocument = JsonDocument.Parse(File.ReadAllText(configPath));
            Assert.Equal(2, configDocument.RootElement.GetProperty("databases").GetArrayLength());
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldDoctorConfigWithoutTestingConnections_WhenDisabled()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Data Source=primary.db;",
                  "dbType": "Sqlite",
                  "isDefault": true
                }
              ]
            }
            """);

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(
                ["config", "doctor", "--config", configPath, "--test-connections", "false"],
                stdout,
                stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.True(document.RootElement.GetProperty("skippedConnectionTests").GetBoolean());
            Assert.Equal(0, document.RootElement.GetProperty("testedConnections").GetInt32());
            Assert.NotEmpty(document.RootElement.GetProperty("fixSuggestions").EnumerateArray());
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldDoctorConfigAndTestConnections()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"dbmcp-cli-doctor-{Guid.NewGuid():N}.db");
        var escapedDatabasePath = databasePath.Replace("\\", "\\\\", StringComparison.Ordinal);
        var configPath = WriteConfigFile($$"""
            {
              "databases": [
                {
                  "name": "sqlite-local",
                  "connectionString": "Data Source={{escapedDatabasePath}};Cache=Shared;Mode=ReadWriteCreate;",
                  "dbType": "Sqlite",
                  "isDefault": true
                }
              ]
            }
            """);

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(
                ["config", "doctor", "--config", configPath],
                stdout,
                stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(1, document.RootElement.GetProperty("testedConnections").GetInt32());
            Assert.Equal(0, document.RootElement.GetProperty("failedConnections").GetInt32());
            Assert.False(document.RootElement.GetProperty("skippedConnectionTests").GetBoolean());
            Assert.NotEmpty(document.RootElement.GetProperty("fixSuggestions").EnumerateArray());
        }
        finally
        {
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(databasePath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldDoctorSingleNamedConnection()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Data Source=primary.db;",
                  "dbType": "Sqlite",
                  "isDefault": true
                },
                {
                  "name": "secondary",
                  "connectionString": "Data Source=secondary.db;",
                  "dbType": "Sqlite"
                }
              ]
            }
            """);

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(
                ["config", "doctor", "--config", configPath, "--name", "secondary", "--test-connections", "false"],
                stdout,
                stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("secondary", document.RootElement.GetProperty("databaseName").GetString());
            Assert.Equal(1, document.RootElement.GetProperty("totalDatabases").GetInt32());
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReturnDoctorSummaryOnly()
    {
        var configPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "primary",
                  "connectionString": "Data Source=primary.db;",
                  "dbType": "Sqlite",
                  "isDefault": true
                }
              ]
            }
            """);

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(
                ["config", "doctor", "--config", configPath, "--test-connections", "false", "--summary-only"],
                stdout,
                stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("configErrors").ValueKind);
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("fixSuggestions").ValueKind);
            Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("connectionResults").ValueKind);
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReturnToolFailureExitCode_WhenInitWithoutForceTargetsExistingFile()
    {
        var configPath = WriteConfigFile("""{ "databases": [] }""");

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(["init", "--config", configPath], stdout, stderr);

            Assert.Equal(1, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        }
        finally
        {
            DeleteFileIfExists(configPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldTestNamedConfigConnection()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"dbmcp-cli-sqlite-{Guid.NewGuid():N}.db");
        var escapedDatabasePath = databasePath.Replace("\\", "\\\\", StringComparison.Ordinal);
        var configPath = WriteConfigFile($$"""
            {
              "databases": [
                {
                  "name": "sqlite-local",
                  "connectionString": "Data Source={{escapedDatabasePath}};Cache=Shared;Mode=ReadWriteCreate;",
                  "dbType": "Sqlite",
                  "description": "local",
                  "isDefault": true
                }
              ]
            }
            """);

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(["config", "test", "--config", configPath, "--name", "sqlite-local"], stdout, stderr);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.True(document.RootElement.GetProperty("connected").GetBoolean());
            Assert.Equal("sqlite-local", document.RootElement.GetProperty("databaseName").GetString());
        }
        finally
        {
            DeleteFileIfExists(configPath);
            DeleteFileIfExists(databasePath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldExportAndImportConfigFiles()
    {
        var sourceConfigPath = WriteConfigFile("""
            {
              "databases": [
                {
                  "name": "sqlite-local",
                  "connectionString": "Data Source=test.db;",
                  "dbType": "Sqlite",
                  "description": "local",
                  "isDefault": true
                }
              ]
            }
            """);
        var exportPath = Path.Combine(Path.GetTempPath(), $"dbmcp-cli-export-{Guid.NewGuid():N}.json");
        var importTargetPath = Path.Combine(Path.GetTempPath(), $"dbmcp-cli-import-target-{Guid.NewGuid():N}.json");

        DeleteFileIfExists(exportPath);
        DeleteFileIfExists(importTargetPath);

        try
        {
            var runner = new CliRunner();

            var exportStdout = new StringWriter();
            var exportStderr = new StringWriter();
            var exportExitCode = await runner.RunAsync(
                ["config", "export", "--config", sourceConfigPath, "--output", exportPath],
                exportStdout,
                exportStderr);

            Assert.Equal(0, exportExitCode);
            Assert.Equal(string.Empty, exportStderr.ToString());
            Assert.True(File.Exists(exportPath));

            var importStdout = new StringWriter();
            var importStderr = new StringWriter();
            var importExitCode = await runner.RunAsync(
                ["config", "import", "--config", importTargetPath, "--input", exportPath],
                importStdout,
                importStderr);

            Assert.Equal(0, importExitCode);
            Assert.Equal(string.Empty, importStderr.ToString());
            Assert.True(File.Exists(importTargetPath));

            using var importedDocument = JsonDocument.Parse(File.ReadAllText(importTargetPath));
            Assert.Equal("sqlite-local", importedDocument.RootElement.GetProperty("databases")[0].GetProperty("name").GetString());
        }
        finally
        {
            DeleteFileIfExists(sourceConfigPath);
            DeleteFileIfExists(exportPath);
            DeleteFileIfExists(importTargetPath);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRequireForce_WhenImportTargetAlreadyExists()
    {
        var sourceConfigPath = WriteConfigFile("""{ "databases": [] }""");
        var importTargetPath = WriteConfigFile("""{ "databases": [] }""");

        try
        {
            var runner = new CliRunner();
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = await runner.RunAsync(
                ["config", "import", "--config", importTargetPath, "--input", sourceConfigPath],
                stdout,
                stderr);

            Assert.Equal(1, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var document = JsonDocument.Parse(stdout.ToString());
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        }
        finally
        {
            DeleteFileIfExists(sourceConfigPath);
            DeleteFileIfExists(importTargetPath);
        }
    }

    private static string WriteConfigFile(string json)
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"dbmcp-cli-{Guid.NewGuid():N}.json");
        File.WriteAllText(configPath, json);
        return configPath;
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }
}

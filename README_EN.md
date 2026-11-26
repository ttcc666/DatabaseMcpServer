# DatabaseMCP Database Operation Server

Current source defaults batch and multiple-result queries to shared budgets of 1000 rows and 1 MiB. Set `DB_QUERY_BOUNDED_DEFAULTS=false` explicitly to retain their previous omitted-budget behavior. Admission enforces both per-connection and application-wide limits, with `DB_MAX_TOTAL_CONCURRENT=32` and `DB_MAX_TOTAL_QUEUED=128`. Review truncation flags and capacity settings when upgrading; see the [execution policy](Doc/database-mcp-workflow.md).

The current source catalog contains **54 tools** in [5.0.0](Doc/release-5.0.0.md); check `tool list/help --json` for your installed version. MCP, CLI and Web filter availability by configured providers and access. See table metadata, paging, Mongo aggregation, streaming CLI export and migration from 4.0.0 in the [current tool contract](Doc/tool-evolution.md).

[![NuGet](https://img.shields.io/nuget/v/DatabaseMcpServer.svg)](https://www.nuget.org/packages/DatabaseMcpServer)
[![.NET Tool](https://img.shields.io/badge/.NET%20Tool-5.0.0-blue.svg)](https://www.nuget.org/packages/DatabaseMcpServer)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

[🇺🇸 English](README_EN.md) | [🇨🇳 中文](README.md) | [🌐 Website](https://databasemcp.ttcc.online/)

A powerful database operation MCP (Model Context Protocol) server focused on **19 widely used databases** (mainstream, scenario-specific, and domestic/compliance) with **single-instance multi-database dynamic switching**, enabling AI assistants to safely and conveniently execute database operations.

## ✨ Core Features

- 🗄️ **Database providers** - Product verification is separate from driver availability. MySQL, PostgreSQL, SQL Server and SQLite have regression coverage; other products need deployment-specific validation. See [ORM configuration](Doc/orm-configuration.md). HG/GBase/TDengine/DuckDB extensions and managed drivers are bundled; GBase and TDengine additionally require native clients on the deployment host. See [driver requirements](Doc/provider-drivers.md). MongoDB provides six tools through SqlSugar.MongoDbCore ADO: find, aggregate, insert_many, update, delete and truncate. Input accepts Extended JSON v2; output uses provider-converted JSON and is not a lossless BSON round trip. SRV URIs are rejected; see the [collection contract](Doc/mongodb-collections.md).
- 🔄 **Single-Instance Multi-Database** - One MCP Server instance can configure and dynamically switch between multiple database connections
- 🔒 **Security Protection** - Dangerous operation detection + SQL injection protection + sensitive information protection
- ⚡ **Execution Isolation** - Exclusive SqlSugarClient leases + driver connection pooling + database-specific optimizations
- 🔧 **Flexible Configuration** - JSON configuration file support for easy multi-database management
- 💾 **Database tools** - Typed queries, MongoDB collections, table paging and offline capability discovery. See the [database MCP workflow](Doc/database-mcp-workflow.md).
- 🚀 **Execution Controls** - Batch operations, stored procedures, access policies, timeouts and optional transactions for supported batch writes
- 📦 **.NET Global Tool** - Source, tests and published packages target .NET 10 LTS
- 🌐 **Cross-Platform** - Full support for Windows, macOS, Linux

## 🗄️ Supported Database Types

### 🔥 Mainstream

- **MySQL** (default)
- **PostgreSQL**
- **SQL Server**
- **Oracle**
- **MongoDB**

### 📊 Scenario-Focused

- **SQLite**
- **ClickHouse**
- **TiDB**
- **OceanBase**
- ~~IBM DB2~~ (removed)
- ~~SAP HANA~~ (removed)

### 🇨🇳 Domestic / Compliance

- **DaMeng (DM)** (dm)
- **Kingbase / Kdbndp** (kdbndp/kingbase)
- **Huawei GaussDB / OpenGauss**
- **PolarDB** (polardb)
- **Vastbase** (vastbase)
- **HighGo Database** (hg)
- **Oscar Database** (oscar)
- **GoldenDB** (goldendb)

## 🚀 Quick Start

### Step 1: Install .NET Global Tool

Source builds and the published tool require the .NET 10 SDK, including the .NET and ASP.NET Core runtimes. See [driver requirements](Doc/provider-drivers.md).

```bash
# Install latest version
dotnet tool install --global DatabaseMcpServer

# Verify the executable version without loading database configuration
DatabaseMcpServer --version
dotnet tool list --global | Select-String databasemcpserver
```

### Step 2: Create Database Configuration File

Create `databases.json` configuration file:

```json
{
  "enableMonitorConfig": false,
  "databases": [
    {
      "name": "default",
      "connectionString": "Server=localhost;Database=test;Uid=root;Pwd=123456;",
      "dbType": "MySql",
      "description": "Default database",
      "isDefault": true
    }
  ]
}
```

`enableMonitorConfig` is an optional root field (default `false`). When `true`, a long-running MCP stdio / `-web` process watches this file and switches the runtime current database if the file default changes. See [Config File Monitoring](#config-file-monitoring-optional) for priority.

> 💡 If `DB_CONFIG_PATH` is not set in the MCP client configuration, stdio mode automatically reads `%USERPROFILE%/.database-mcp/databases.json`. CLI / `-web` additionally check `./databases.json` and `./local-databases.json` in the current directory. Setting it explicitly is still recommended.

### Step 3: Configure MCP Client

Create `mcp.json` configuration file (VS Code: `.vscode/mcp.json`):

```json
{
  "mcpServers": {
    "database": {
      "command": "DatabaseMcpServer",
      "env": {
        "DB_CONFIG_PATH": "D:\\config\\databases.json"
      }
    }
  }
}
```

### Step 4: Test Connection and Execute Queries

After restarting IDE, test in AI assistant:

```
"Test database connection"
```

System returns:

```json
{
  "success": true,
  "connected": true,
  "databaseType": "MySql"
}
```

## 💻 Command Line Mode (CLI)

Starting with the current version, `DatabaseMcpServer` keeps its original MCP stdio mode while also supporting the following two categories of tasks directly from the command line:

- **No arguments**: starts the stdio MCP server (compatible with existing MCP client configurations)
- **`-web`**: starts a localhost-only configuration management page and opens the browser by default
- **`tool` subcommand**: invokes an exposed MCP tool directly
- **`init` / `config` subcommands**: initializes and maintains the local `databases.json`

### Basic Usage

```bash
# Start the local Web configuration page (opens the browser by default)
DatabaseMcpServer -web
DatabaseMcpServer -web --config "D:\config\databases.json" --no-browser

# Start stdio MCP / -web with config-file monitoring forced on or off for this process
DatabaseMcpServer --enable-monitor-config
DatabaseMcpServer --enable-monitor-config true
DatabaseMcpServer --enable-monitor-config false -web --no-browser
DatabaseMcpServer -web --enable-monitor-config true --no-browser

# Initialize the default config file (defaults to %USERPROFILE%/.database-mcp/databases.json)
DatabaseMcpServer init

# Inspect / manage local connection configuration
DatabaseMcpServer config list
DatabaseMcpServer config presets
DatabaseMcpServer config preset --db-type Sqlite
DatabaseMcpServer config create --from-preset Sqlite --name sqlite-local --connection-string "Data Source=./data/local.db;Cache=Shared;Mode=ReadWriteCreate;" --description "local sqlite" --set-default
DatabaseMcpServer config create --from-preset Sqlite --name sqlite-preview --print-only
DatabaseMcpServer config add --name sqlite-local --db-type Sqlite --connection-string "Data Source=./data/local.db;Cache=Shared;Mode=ReadWriteCreate;" --set-default
DatabaseMcpServer config rename --name sqlite-local --new-name sqlite-dev
DatabaseMcpServer config update --name sqlite-dev --description "dev sqlite" --set-default
DatabaseMcpServer config validate
DatabaseMcpServer config clone --name sqlite-dev --new-name sqlite-ci
DatabaseMcpServer config doctor
DatabaseMcpServer config export --output ".\\backup-databases.json"
DatabaseMcpServer config import --input ".\\backup-databases.json" --config "D:\config\databases.json" --force

# List every invokable tool
DatabaseMcpServer tool list

# Show help for a single tool
DatabaseMcpServer tool help switch_database

# Invoke a tool directly
DatabaseMcpServer tool list_databases --config "D:\config\databases.json"
DatabaseMcpServer tool get_table_schema --table-name users --config "D:\config\databases.json"
```

### Argument Rules

- `-web` is mainly for **local visual configuration management**
  - Binds to `localhost / 127.0.0.1` by default; not exposed to the public network
  - Reuses the CLI resolution order: `--config -> ./databases.json -> ./local-databases.json -> DB_CONFIG_PATH -> %USERPROFILE%/.database-mcp/databases.json`
  - When no existing config file is found, falls back to `%USERPROFILE%/.database-mcp/databases.json` as the writable target
  - The page manages two state files together: the default connection in `databases.json`, and the current connection in `%USERPROFILE%/.database-mcp/cli-state.json`
  - `--port <number>` explicitly sets the port; otherwise a free port is assigned automatically
  - `--no-browser` disables auto-opening the browser
  - `--enable-monitor-config true|false` forces `databases.json` monitoring on or off for this process; it overrides the environment variable and the config-file field
- `init` / `config` are mainly for **local configuration management**
  - Default target is `%USERPROFILE%/.database-mcp/databases.json`
  - `--config <path>` overrides the target config file for the current invocation
  - `config use` / `config set-default` switches the default connection (written back to `databases.json`)
  - `config rename` / `config update` evolves an existing connection
  - `config validate` runs config-file-level validation (not a connectivity test)
  - `config clone` quickly duplicates a connection
  - `config presets` / `config preset` lists built-in connection templates
  - `config create --from-preset` scaffolds a connection from a template and can override the connection string / description
  - `config update --clear-description` explicitly clears the description
  - `--enable-dangerous-operations true|false` writes the dangerous-operations switch into `config create/add/update` (default `false`)
  - `config doctor` runs diagnostics — by default tests connectivity of each connection and provides fix suggestions; `--summary-only` is suitable for scripts
  - `config export` / `config import` backs up and migrates the config file
- Tool names match the MCP names and use `snake_case`
  - For example: `list_databases`, `get_table_schema`, `execute_command`
- Tool parameters are mapped to `kebab-case` options
  - For example: `databaseName -> --database-name`
  - For example: `initialDelayMs -> --initial-delay-ms`
- `tool switch_database` switches the **current connection**
  - Under CLI, it persists the current connection per resolved config path into `%USERPROFILE%/.database-mcp/cli-state.json`
  - It does not modify the default connection in `databases.json`
  - Subsequent `tool get_current_database` / `tool list_databases` / query-style commands keep using this current connection
  - It only falls back to the default connection when there is no saved current connection, or the saved one has become invalid
- CLI global options:
  - `--config <path>`: temporarily sets the config file for the current invocation
  - `--yes`: must be supplied for write / high-risk schema tools
  - `--help`: shows help

### Config File Lookup Order Under `tool` Mode

When running `DatabaseMcpServer tool ...` without an explicit `--config`, the CLI looks up the database configuration in this order:

1. Current directory `./databases.json`
2. Current directory `./local-databases.json`
3. Environment variable `DB_CONFIG_PATH`
4. User directory `%USERPROFILE%/.database-mcp/databases.json`

### High-Risk Command Confirmation

The following write / high-risk commands must include `--yes`:

```bash
DatabaseMcpServer tool drop_table --table-name users --config "D:\config\databases.json" --yes
DatabaseMcpServer tool execute_command --sql "delete from users where id = 1" --config "D:\config\databases.json" --yes
```

Under CLI mode, command result JSON is written to `stdout`, and help and logs go to `stderr`, which is convenient for script integration.
Also note: `tool switch_database` and `config use` have different semantics — the former switches and persists the "current connection", while the latter modifies the "default connection" in the config file.

See the full command reference:

- [CLI command guide](Doc/cli.md)

## 📦 Installation Methods

### Method 1: .NET Global Tool (Recommended)

**Installation**:

```bash
dotnet tool install --global DatabaseMcpServer
# Update: dotnet tool update --global DatabaseMcpServer
```

**MCP Configuration**:

```json
{
  "mcpServers": {
    "database": {
      "command": "DatabaseMcpServer",
      "env": {
        "DB_CONFIG_PATH": "D:\\config\\databases.json"
      }
    }
  }
}
```

### Method 2: dnx Command

**Installation**:

```bash
dnx DatabaseMcpServer@3.6.5 --yes
```

**MCP Configuration**:

```json
{
  "mcpServers": {
    "database": {
      "command": "dnx",
      "args": ["DatabaseMcpServer@3.6.5", "--yes"],
      "env": {
        "DB_CONFIG_PATH": "D:\\config\\databases.json"
      }
    }
  }
}
```

### Method 3: Local Source Code Run

**Run**:

```bash
git clone https://github.com/ttcc666/DatabaseMcpServer.git
cd DatabaseMcpServer
# .NET 10 LTS
dotnet run --project 'src/DatabaseMcpServer/DatabaseMcpServer.csproj'
```

**MCP Configuration**:

```json
{
  "mcpServers": {
    "database-net10": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/DatabaseMcpServer/src/DatabaseMcpServer/DatabaseMcpServer.csproj"],
      "env": {
        "DB_CONFIG_PATH": "D:\\config\\databases.json"
      }
    }
  }
}
```

## ⚙️ Configuration Guide

DatabaseMcpServer 2.0.0 uses JSON configuration file for unified database connection management.

### Configuration File (Recommended; auto-discovered when unset)

Specify the **absolute path** of the configuration file through the environment variable `DB_CONFIG_PATH`:

> **Auto-discovery priority:**
>
> - **stdio mode**: `DB_CONFIG_PATH` → `%USERPROFILE%/.database-mcp/databases.json` (does not read the current directory, to avoid picking up stray files from MCP client directories).
> - **CLI / `-web` mode**: `--config` → `./databases.json` → `./local-databases.json` → `DB_CONFIG_PATH` → `%USERPROFILE%/.database-mcp/databases.json`.
>
> Setting `DB_CONFIG_PATH` explicitly is still recommended.

**MCP Configuration Example:**

```json
{
  "mcpServers": {
    "database": {
      "command": "DatabaseMcpServer",
      "env": {
        "DB_CONFIG_PATH": "D:\\config\\databases.json"
      }
    }
  }
}
```

After `databases.json` changes, call `reload_database_config` to reload the file and rebuild cached clients without restarting the MCP process.

**Configuration File Format (databases.json):**

```json
{
  "enableMonitorConfig": false,
  "databases": [
    {
      "name": "mysql-main",
      "connectionString": "Server=localhost;Database=myapp;User=root;Password=123456;",
      "dbType": "MySql",
      "description": "MySQL Main Database",
      "isDefault": true,
      "enableDangerousOperations": false,
      "optimizationSettings": {
        "enableCache": "true",
        "batchSize": "1000"
      }
    },
    {
      "name": "postgres-analytics",
      "connectionString": "Host=localhost;Database=analytics;Username=postgres;Password=123456;",
      "dbType": "PostgreSQL",
      "description": "PostgreSQL Analytics Database",
      "optimizationSettings": {
        "autoToLower": "true",
        "enableIlike": "true"
      }
    }
  ]
}
```

The root field `enableMonitorConfig` can be stored in the config file to control whether a long-running MCP stdio / `-web` process watches that file. It is treated as `false` when omitted.

### Config File Monitoring (Optional)

A long-running process (MCP stdio / `-web`) can watch `databases.json` and switch the runtime current database when the file default changes. One-shot `tool` / `config` commands do not start the watcher.

It can be set in three places, **highest priority first**:

| Priority | Source | Notes |
| --- | --- | --- |
| 1 (highest) | Startup flag `--enable-monitor-config true\|false` | Process-local force on/off; not written back to the config file |
| 2 | Environment variable `ENABLE_MONITOR_CONFIG` | `true`/`false` (also accepts `1`/`0`/`yes`/`no`/`on`/`off`); if unset, fall through |
| 3 | Config file `enableMonitorConfig` | Root field in `databases.json`; edit the JSON directly |
| 4 (default) | Unset | Monitoring is off |

Example: even if the file has `"enableMonitorConfig": true`, this process will not watch when started with `--enable-monitor-config false` or `ENABLE_MONITOR_CONFIG=false`.

**Multi-Database Management Tools:**
- `list_databases` - List all available database connections
- `switch_database` - Switch to a specified database
- `get_current_database` - Get current active database

**Performance Optimization Tools:**
- `health_check` - Perform health checks on all database connections (response time, connection status)

---

## 🌐 Environment Configuration

### Optional Environment Variables
- `DB_CONFIG_PATH`: Database configuration file path
  - Example: `D:\config\databases.json`
  - Priority: env var `DB_CONFIG_PATH` → `%USERPROFILE%/.database-mcp/databases.json` (stdio mode); CLI / `-web` additionally check `./databases.json` and `./local-databases.json` in the current directory
- `SEQ_SERVER_URL`: Seq log server address (optional)
- `SEQ_API_KEY`: Seq API key (optional)
- `DB_DDL_WHITELIST`: DDL operation whitelist (optional, semicolon-separated regex patterns)
- `ENABLE_MONITOR_CONFIG`: Monitor `databases.json` and let a long-running MCP / `-web` process follow default-database changes (`true`/`false`, off by default). You can also set `"enableMonitorConfig": true` at the config-file root. Priority: `--enable-monitor-config` > `ENABLE_MONITOR_CONFIG` > `enableMonitorConfig`.

### Database-Specific Optimization Configuration
Starting from version 2.0.0, all database-specific optimization configurations are set in the `optimizationSettings` section of `databases.json`.

**Detailed Configuration Documentation**:
- [MySQL Configuration Guide](DatabaseSetting/MySQL.md)
- [PostgreSQL Configuration Guide](DatabaseSetting/PostgreSQL.md)
- [SQL Server Configuration Guide](DatabaseSetting/SQLServer.md)
- [Oracle Configuration Guide](DatabaseSetting/Oracle.md)
- [MongoDB Configuration Guide](DatabaseSetting/MongoDB.md)
- [SQLite Configuration Guide](DatabaseSetting/SQLite.md)
- [ClickHouse Configuration Guide](DatabaseSetting/ClickHouse.md)
- [TiDB Configuration Guide](DatabaseSetting/TiDB.md)
- [OceanBase Configuration Guide](DatabaseSetting/OceanBase.md)
- [DaMeng Database Configuration Guide](DatabaseSetting/DM.md)
- [KingbaseES Configuration Guide](DatabaseSetting/Kdbndp.md)
- [GaussDB Configuration Guide](DatabaseSetting/GaussDB.md)
- [PolarDB Configuration Guide](DatabaseSetting/PolarDB.md)
- [Vastbase Configuration Guide](DatabaseSetting/Vastbase.md)
- [HighGo Database Configuration Guide](DatabaseSetting/HighGo.md)
- [Oscar Database Configuration Guide](DatabaseSetting/Oscar.md)
- [GoldenDB Configuration Guide](DatabaseSetting/GoldenDB.md)
- [Configuration Index](DatabaseSetting/README.md)

---

## 🔄 Migration from 1.x to 2.0

### ⚠️ Breaking Changes

DatabaseMcpServer 2.0.0 has removed environment variable configuration method and unified to use JSON configuration file.

### Migration Steps

#### 1. Single Database Configuration Migration

**Old Method (1.x - Deprecated)**:
```json
{
  "mcpServers": {
    "database": {
      "command": "DatabaseMcpServer",
      "env": {
        "DB_CONNECTION_STRING": "Server=localhost;Database=test;...",
        "DB_TYPE": "MySql",
        "DB_DM_LOWERCASE_TABLES": "true"
      }
    }
  }
}
```

**New Method (2.0)**:

1. Create `databases.json` file:
```json
{
  "databases": [
    {
      "name": "default",
      "connectionString": "Server=localhost;Database=test;...",
      "dbType": "MySql",
      "description": "Default database",
      "isDefault": true,
      "enableDangerousOperations": false,
      "optimizationSettings": {
        "lowercaseTables": "true"
      }
    }
  ]
}
```

2. Update MCP configuration:
```json
{
  "mcpServers": {
    "database": {
      "command": "DatabaseMcpServer",
      "env": {
        "DB_CONFIG_PATH": "D:\\config\\databases.json"
      }
    }
  }
}
```

#### 2. Environment Variable Mapping Table

| Old Environment Variable | New JSON Configuration Path |
|-------------------------|----------------------------|
| `DB_CONNECTION_STRING` | `databases[].connectionString` |
| `DB_TYPE` | `databases[].dbType` |
| `DB_DM_LOWERCASE_TABLES` | `databases[].optimizationSettings.lowercaseTables` |
| `DB_KDBNDP_MODE` | `databases[].optimizationSettings.mode` |
| `DB_GAUSSDB_NATIVE_DRIVER` | `databases[].optimizationSettings.nativeDriver` |
| `DB_ORACLE_CAMEL_CASE` | `databases[].optimizationSettings.camelCase` |
| `DB_POSTGRES_AUTO_TO_LOWER` | `databases[].optimizationSettings.autoToLower` |
| `DB_SQLITE_ENABLE_DEFAULT_VALUE` | `databases[].optimizationSettings.enableDefaultValue` |
| `DB_DISABLE_NVARCHAR` | `databases[].optimizationSettings.disableNvarchar` |

For complete mapping table, please refer to each database configuration documentation.

#### 3. Automatic Migration Detection

If you are still using old environment variable configuration, DatabaseMcpServer 2.0.0 will automatically detect and display detailed migration prompts.

### Common Database Connection String Examples

| Database | Connection String Example | Detailed Documentation |
|----------|--------------------------|------------------------|
| **MySQL** | `Server=localhost;Port=3306;Database=mydb;User=root;Password=123456;` | [MySQL.md](DatabaseSetting/MySQL.md) |
| **SQL Server** | `Server=localhost;Database=mydb;User Id=sa;Password=123456;` | [SQLServer.md](DatabaseSetting/SQLServer.md) |
| **PostgreSQL** | `Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=123456;` | [PostgreSQL.md](DatabaseSetting/PostgreSQL.md) |
| **OceanBase (MySQL mode)** | `Server=localhost;Port=2881;Database=mydb;User=root@sys;Password=123456;` | [OceanBase.md](DatabaseSetting/OceanBase.md) |
| **OceanBase (Oracle mode)** | `Driver={OceanBase ODBC 2.0 Driver};Server=172.19.9.9;Port=2883;Database=TRD;User=USER@TENANT#CLUSTER:1650773680;Password=123456;Option=3;` | [OceanBase.md](DatabaseSetting/OceanBase.md) |
| **QuestDB** | `host=localhost;port=8812;username=admin;password=quest;database=qdb;ServerCompatibilityMode=NoTypeLoading;` | [QuestDb.md](DatabaseSetting/QuestDb.md) |
| **GBase 8s** | `Host=localhost;Service=19088;Server=gbase01;Database=testdb;Protocol=onsoctcp;Uid=gbasedbt;Pwd=GBase123;Db_locale=zh_CN.utf8;Client_locale=zh_CN.utf8` | [GBase.md](DatabaseSetting/GBase.md) |
| **DuckDB** | `DataSource=train_services.db` | [DuckDB.md](DatabaseSetting/DuckDB.md) |
| **TDengine** | `Host=localhost;Port=6030;Username=root;Password=taosdata;Database=power` | [TDengine.md](DatabaseSetting/TDengine.md) |
| **Oracle** | `Data Source=localhost/orcl;User ID=system;Password=oracle123;` | [Oracle.md](DatabaseSetting/Oracle.md) |
| **SQLite** | `Data Source=mydb.db;` | [SQLite.md](DatabaseSetting/SQLite.md) |
| **DaMeng Database** | `Server=localhost;Port=5236;Database=mydb;User=SYSDBA;Password=SYSDBA001;` | [DM.md](DatabaseSetting/DM.md) |
| **KingbaseES** | `Server=localhost;Port=54321;Database=mydb;User=SYSTEM;Password=system123;` | [Kdbndp.md](DatabaseSetting/Kdbndp.md) |
| **GaussDB** | `PORT=5432;DATABASE=mydb;HOST=localhost;PASSWORD=Gauss@123;USER ID=gaussdb;` | [GaussDB.md](DatabaseSetting/GaussDB.md) |

For more connection strings and optimization configurations, please refer to the detailed documentation in the [DatabaseSetting/](DatabaseSetting/) directory.

---

## 📋 Complete Feature List (54 tools)

Run `DatabaseMcpServer tool list` to see the tools available for your configuration and `tool help <name>` for arguments. See the [tool reference](TOOLS.md) for the complete catalog and the [capability matrix](Doc/capabilities.generated.md) for provider differences.

`query_table_page` supports columns, AND filters and ascending/descending order. Supplying any of these options uses offset pages without a cursor and checks bytes after loading the page; omitting them retains primary-key cursor paging. `insert_row` returns an integer identity as a decimal string on SQL Server, MySQL and SQLite. Index tools support multiple columns and `drop_index` removes an index. MCP also exposes connection/capability/schema resources, slow-query and migration prompts, connection-name completion and health-check progress.

## 💡 Usage Examples

### Example 1: Basic Connection and Query

**Test Database Connection**

```
Test database connection
```

**List All Tables**

```
List all tables in current database
```

**Query User Data**

```
Query all data in users table
```

### Example 2: Parameterized Queries

**Conditional Query**

```
Query active users older than 25 in users table, ordered by creation time descending
```

**IN Parameter Query**

```
Query user information where user ID is in [1,2,3,4,5]
```

**Multi-condition Query**

```
Query users with city "Beijing", age between 20-30, and status active
```

### Example 3: Inspect a table

```text
Show the columns, primary key, and indexes of the orders table.
```

## 🔒 Security Features

### Dangerous Operation Detection

System automatically detects and blocks the following dangerous operations:

- `DROP TABLE` / `DROP DATABASE` - Delete table/database
- `TRUNCATE TABLE` - Clear table data
- `ALTER TABLE` - Modify table structure
- `DELETE` / `UPDATE` without WHERE condition

To execute these operations, prefer dedicated schema operation tools (such as `create_table`, `drop_table`, `truncate_table`, etc.), which clearly prompt risks. If you must run DDL through `execute_command`, `execute_command_with_go`, or `batch_execute_commands`, explicitly set `"enableDangerousOperations": true` on the current connection, or run `config update --enable-dangerous-operations true`; the default is `false`.

The legacy configuration field `allowDangerousOperations` and CLI option `--allow-dangerous-operations` remain accepted. Saving a configuration writes only `enableDangerousOperations`. When both JSON fields are present, the new field takes precedence regardless of field order.

### SQL Injection Protection

All queries support parameterized queries, automatically preventing SQL injection:

```json
{
  "sql": "SELECT * FROM users WHERE age > @age AND city = @city",
  "parameters": [{"name": "age", "value": 18}, {"name": "city", "value": "Beijing"}]
}
```

### SQL Command Timeout

Query and data-operation tools accept optional `commandTimeoutSeconds` (CLI: `--command-timeout-seconds`):

- Async queries and writes default to a 30-second total deadline, including queueing, execution and reading; server policy can override this
- Synchronous GO/DDL commands retain the SqlSugar/provider default (typically 300 seconds), subject to the server timeout ceiling
- `0` permits infinite waiting only when `DB_ALLOW_INFINITE_TIMEOUT=true`; atomic batches still have an independent total deadline
- Valid range: `0–86400`, also limited by `DB_COMMAND_MAX_TIMEOUT_SECONDS`
- Async batch deadlines cover the whole batch; see the [execution workflow](Doc/database-mcp-workflow.md)

```json
{
  "sql": "SELECT * FROM large_report WHERE day = @day",
  "parameters": [{"name": "day", "value": "2026-08-01"}],
  "commandTimeoutSeconds": 900
}
```

```bash
DatabaseMcpServer tool execute_command \
  --sql 'update large_table set status=@status where id=@id' \
  --parameters '[{"name":"status","value":"done"},{"name":"id","value":1}]' \
  --command-timeout-seconds 900 \
  --yes
```

See [TOOLS.md](TOOLS.md) for the full parameter catalog.

### Sensitive Information Protection

- Passwords in connection strings are automatically hidden (displayed as `Password=****`)
- Complete connection strings are not output in logs
- Configuration information is automatically desensitized when returned

## 💻 Development Guide

### Local Development

The backend has Host, Core and Infrastructure assemblies. See the [architecture guide](Doc/backend-architecture.md). Backend builds use tracked web assets; pass `-p:BuildWebsite=true` when updating the UI or packaging a release.

```bash
# Clone project
git clone https://github.com/ttcc666/DatabaseMcpServer.git
cd DatabaseMcpServer

# Create databases.json configuration file, then run
DB_CONFIG_PATH="path/to/databases.json" dotnet run --project 'src/DatabaseMcpServer/DatabaseMcpServer.csproj'

# Build project
dotnet build 'DatabaseMcpServer.slnx'

# Run tests
dotnet test 'tests\DatabaseMcpServer.Tests\DatabaseMcpServer.Tests.csproj'

# Package and publish
dotnet pack 'src\DatabaseMcpServer\DatabaseMcpServer.csproj' -c Release '-p:BuildWebsite=true' --artifacts-path 'artifacts/package-build'
```

Recommended stable verification flow:

```powershell
.\scripts\verify.ps1
```

CLI command reference:

- [CLI Command Guide](Doc/cli.md)

CLI highlights:

- `DatabaseMcpServer` with no arguments still starts the stdio MCP server.
- `DatabaseMcpServer -web` starts a localhost-only configuration UI and opens the browser by default.
- `DatabaseMcpServer --enable-monitor-config true|false` starts stdio MCP (or prefix `-web`) with config-file monitoring forced on or off for this process. Priority: `--enable-monitor-config` > `ENABLE_MONITOR_CONFIG` > config-file `enableMonitorConfig` (default off).
- `DatabaseMcpServer tool ...` invokes existing MCP tools directly.
- In CLI tool mode, `switch_database` persists the current connection per resolved config path for later `tool` invocations.
- `config use` / `config set-default` updates the default connection stored in `databases.json`; it is distinct from the persisted CLI current connection.
- `DatabaseMcpServer init` creates a local config file, defaulting to `%USERPROFILE%/.database-mcp/databases.json`.
- `DatabaseMcpServer config presets|preset|create|list|show|add|rename|update|clone|remove|set-default|use|test|validate|doctor|export|import` manages connection entries, built-in templates, and config files from the CLI.
- `-web` resolves config files with the same order as CLI tool mode and falls back to `%USERPROFILE%/.database-mcp/databases.json` as the writable target when no existing config is found.

## 🆕 Release Notes

- **5.0.0**
  - Provide 54 tools, table projection/filtering/ordering, single-row identity inserts, index removal and composite index/primary-key support
  - Move MongoDB execution to SqlSugar built-in ADO; remove legacy tools, use batch update/delete semantics, and document output conversion and SRV restrictions
  - Add MCP Resources, Prompts, connection-name completion and health-check progress; target .NET 10 for source and tests
  - See the [5.0.0 release notes](Doc/release-5.0.0.md) for migration and verification commands; entries below describe historical releases

- **4.0.0**
  - Adopt `query_database`, typed parameters, named connections, bounded results, table paging and offline capability diagnostics; remove legacy query/output-parameter entry points and refresh client tool catalogs after upgrading
  - Add six native MongoDB collection tools with Extended JSON, write outcomes, and guards for multi-document or empty-filter writes
  - Split Host / Core / Infrastructure and group files by feature; unify execution admission and configuration state, and update the CLI usage skill
  - Upgrade MCP to 2.4.0 and SqlSugarCore to 5.1.4.221, bundle additional drivers, and fix read-only enforcement, numeric precision and MCP error signaling
  - Publish the .NET 10 tool while retaining .NET 9 / 10 source targets at that release; later source changes target .NET 10 only. See the historical [4.0.0 migration notes](Doc/release-4.0.0.md)

- **3.7.0**
  - Add optional configuration monitoring so persistent MCP stdio / `-web` processes follow file default changes; precedence is `--enable-monitor-config` > `ENABLE_MONITOR_CONFIG` > `enableMonitorConfig`, disabled by default
  - Fall back to `.database-mcp/databases.json` in the user profile when MCP stdio starts without `DB_CONFIG_PATH`
  - Use `enableDangerousOperations` while retaining the legacy JSON field and `--allow-dangerous-operations` CLI option; the new JSON field takes precedence when both are present
  - Fix runtime switching after Web default changes and isolate configuration path tests in temporary user profiles

- **3.6.6**
  - Add optional `commandTimeoutSeconds` (CLI: `--command-timeout-seconds`) to query and data-operation tools so callers can override the ~300s default
  - Isolate explicit timeouts with `CopyNew()` to avoid contaminating the shared client pool; `0` waits indefinitely and the valid range is `0–86400`
  - Sync `TOOLS.md`, CLI/skill docs, and unit-test coverage

- **3.6.5**
  - Upgrade `ModelContextProtocol` to 2.0.0 and adopt the new `ServerInfo` configuration plus tool `ReadOnly`/`Destructive`/`Idempotent` annotations
  - Bump `Microsoft.Extensions.*`, `SqlSugarCore`, `Serilog.Sinks.Seq`, and the test SDK to the latest stable versions
  - Build and 94 unit tests pass on net9.0 / net10.0

- **3.6.0**
  - Add Chinese/English switching, localized UI copy, and persisted locale preferences to the local Web console
  - Use an app-owned scroll container and disable overlay body scroll locking to prevent layout jitter when sheets and menus open
  - Add locale initialization and connection health tests, and refresh the embedded production assets

- **3.5.5**
  - Fix Oracle connection checks incorrectly reporting `ORA-00923` when executing `SELECT 1`
  - Use the dialect-independent connection probe for MCP connection tests, health checks, retry checks, and CLI doctor
  - Add `create_table` to the CLI release verifier, covering all 56 tools

- **3.5.0**
  - Add the `create_table` tool for creating tables from JSON column definitions
  - Add per-connection `enableDangerousOperations` configuration, disabled by default
  - Block `UPDATE` / `DELETE` without `WHERE` and bind each database client to the matching safety-policy snapshot

- **3.0.0**
  - **Breaking:** Remove the Excel export and database documentation tools together with the `ClosedXML` dependency
  - Add `batch_sql_query` for sequential execution of 1-5 read-only queries with per-query results
  - Clarify that `batch_execute_commands` is non-transactional and does not roll back earlier successful commands
  - Move source/tests under `src/` and `tests/`, fix pooled `SqlSugarScope` lifetime, and refresh the CLI documentation and skill

- **2.1.1**
  - Add `reload_database_config` to reload database connections from `DB_CONFIG_PATH` at runtime
  - Reset cached SqlSugar clients during config refresh so subsequent requests use updated connections
  - Expand tests for config reload and client recreation before release

- **2.5.0**
  - Add a local `-web` configuration console for managing `databases.json` and `cli-state.json` in the browser
  - Rework the frontend into a `Vue 3 + shadcn-vue` workspace with connection table, editor sheet, diagnostics, and import/export flows
  - Fix Web API fallback routing, invalid `--port` crashes, and stale frontend asset packaging, and add regression tests

- **2.2.2**
  - Fix `switch_database` in CLI mode so the selected current connection persists per config file path instead of only lasting for one process
  - Add tests for state restore, per-config-path isolation, and fallback to the default connection when a saved selection disappears
  - Update NuGet, MCP manifest, and README version metadata for the `2.2.2` package release

- **2.2.1**
  - Refine `database-mcp-cli` skill triggers, CLI workflow guidance, and troubleshooting matrix
  - Add `agents/openai.yaml` so the skill has consistent UI metadata and invocation semantics
  - Update NuGet, MCP manifest, and README version metadata for the `2.2.1` package release

- **2.2.0**
  - Add CLI mode so `DatabaseMcpServer tool <tool_name>` can invoke existing database tools directly
  - Preserve no-argument stdio MCP server behavior and add `tool list`, `tool help`, `--config`, and `--yes`
  - Suppress runtime log noise in CLI mode so successful calls emit tool result JSON only
  - Add CLI docs, SQLite/SQL Server verification scripts, and a draft `database-mcp-cli` skill

- **2.1.0**
  - Align version to 2.1.0 (badges/example commands/config)
  - Add Chinese XML summaries for tools, services, and strategies to improve IntelliSense/maintenance
  - Trim redundant tool APIs (multi-type scalar wrappers/redundant queries/redundant DML wrappers); keep core capabilities
  - Fix non-nullable model warnings; build is clean

### Adding New Tools

1. **Create Tool Class File**

   ```bash
   # Create new tool class in src/DatabaseMcpServer/Tools/ directory
   # Management/ - Connection and schema management
   # Query/ - Query tools
   # Command/ - Command tools
   ```

2. **Implement Tool Class**

   ```csharp
   using System.ComponentModel;
   using ModelContextProtocol.Server;
   using DatabaseMcpServer.Interfaces;
   using DatabaseMcpServer.Models;
   using DatabaseMcpServer.Services;
   using Microsoft.Extensions.Logging;

   namespace DatabaseMcpServer.Tools;

   [McpServerToolType]
   internal class YourNewTools(
       IDatabaseConfigService databases,
       IDatabaseHelperService helper,
       IJsonResultSerializer serializer,
       ILogger<YourNewTools> logger,
       DatabaseExecutionOptions options,
       DatabaseMetadataCache metadataCache)
       : McpToolBase(databases, helper, serializer, logger, options, metadataCache)
   {
       [McpServerTool(ReadOnly = true)]
       [Description("Return the connected database type.")]
       public string YourMethod([Description("Database connection name.")] string? databaseName = null)
           => WithClient(db => new { success = true, databaseType = db.CurrentConnectionConfig.DbType.ToString() }, databaseName);
   }
   ```

3. **Register Tool**
   Add an entry to `Registrations` in `Extensions/DatabaseMcpToolCatalog.cs`:

   ```csharp
   new(typeof(YourNewTools), static builder => builder.WithTools<YourNewTools>())
   ```

   The shared extension handles registration:

   ```csharp
   builder.Services.AddDatabaseMcpServer();
   ```

### Project Architecture

```
MCP Protocol Layer (stdio)
    ↓
Tools Layer (Connection/Query/Command/Schema)
    ↓
Services Layer (DatabaseConfigService)
    ↓
Data Access Layer (SqlSugar ORM)
```

**Key Components:**

- `DatabaseConfigService` - Configuration management and connection creation
- `DatabaseHelper` - Database type parsing and security checks
- `McpExceptionFilter` - Unified exception handling
- `ApiResult<T>` - Standardized return format

## 🛠️ Tech Stack

- **.NET 10 LTS** - The sole target framework for source, tests, benchmarks and the published tool
- **ModelContextProtocol 2.4.0** - MCP protocol C# SDK
- **SqlSugarCore 5.1.4.221** - ORM and SQL provider adapters
- **Serilog** - Structured logging framework
- **Microsoft.Extensions.Hosting** - Dependency injection and hosting

## 📚 Related Resources

- [MCP Official Documentation](https://modelcontextprotocol.io/)
- [MCP GitHub](https://github.com/modelcontextprotocol)
- [SqlSugar Documentation](https://github.com/DotNetNext/SqlSugar)
- [VS Code MCP Guide](https://code.visualstudio.com/docs/copilot/chat/mcp-servers)

## 🤝 Contributing

Welcome to submit Issues and Pull Requests!

1. Fork project
2. Create feature branch: `git checkout -b feature/AmazingFeature`
3. Commit changes: `git commit -m 'Add AmazingFeature'`
4. Push to branch: `git push origin feature/AmazingFeature`
5. Open Pull Request

## 📄 License

This project is licensed under MIT License - see [LICENSE](LICENSE) file for details.

## ⚠️ Disclaimer

- The current project version is 3.6.5
- Version 3.0.0 removes the Excel export and database documentation tools; review existing integrations before upgrading
- Version 2.0.0 contains breaking changes, please refer to the migration guide
- Please test thoroughly before using in production environment
- Regularly backup important data
- Pay attention to sensitive information protection in configuration

---

**DatabaseMCP** - Let AI assistants easily operate databases!

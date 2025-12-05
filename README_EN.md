# DatabaseMCP Database Operation Server

[![NuGet](https://img.shields.io/nuget/v/DatabaseMcpServer.svg)](https://www.nuget.org/packages/DatabaseMcpServer)
[![.NET Tool](https://img.shields.io/badge/.NET%20Tool-2.0.1-test-blue.svg)](https://www.nuget.org/packages/DatabaseMcpServer)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

[🇺🇸 English](README_EN.md) | [🇨🇳 中文](README.md) | [🌐 Website](https://databasemcp.ttcc.online/)

A powerful database operation MCP (Model Context Protocol) server that supports **34 database types**, **single-instance multi-database dynamic switching**, enabling AI assistants to safely and conveniently execute database operations.

## ✨ Core Features

- 🗄️ **Multi-Database Support** - Supports 34 database types (mainstream, domestic, distributed, time-series)
- 🔄 **Single-Instance Multi-Database** - One MCP Server instance can configure and dynamically switch between multiple database connections
- 🔒 **Security Protection** - Dangerous operation detection + SQL injection protection + sensitive information protection
- ⚡ **High Performance** - SqlSugarScope connection pool reuse + database-specific optimizations + automatic performance tuning
- 🔧 **Flexible Configuration** - JSON configuration file support for easy multi-database management
- 💾 **Complete Functionality** - 55+ MCP tools, covering queries, operations, schema management, health checks, etc.
- 🚀 **Production Ready** - Supports transactions, batch operations, stored procedures, automatic reconnection
- 📦 **.NET Global Tool** - Simple installation, one-click deployment
- 🌐 **Cross-Platform** - Full support for Windows, macOS, Linux

## 🗄️ Supported Database Types

### 🌐 Mainstream Databases

- **MySQL** (default)
- **SQL Server**
- **SQLite**
- **PostgreSQL**
- **Oracle**

### 🇨🇳 Domestic Databases

- **DaMeng Database** (dm)
- **RenDaJinCang** (kdbndp/kingbase)
- **ShenTong Database** (oscar)
- **HanGao Database** (hg)
- **NanDaTongYong GBase** (gbase)
- **XuGu Database** (xugu)
- **HaiLiang Database** (vastbase)
- **GoldenDB** (goldendb)

### 🚀 Distributed Databases

- **OceanBase** (oceanbase)
- **TiDB** (tidb)
- **PolarDB** (polardb)
- **Doris** (doris)

### ⏱️ Time-Series Databases

- **TDengine** (tdengine)
- **QuestDB** (questdb)
- **ClickHouse** (clickhouse)

### 🔍 Other Databases

**Analytical**: DuckDB, DuckDB
**Interfaces**: Microsoft Access, ODBC
**Enterprise**: SAP HANA, IBM DB2
**Document**: MongoDB
**Specialized**: OpenGauss, GaussDB, etc.

## 🚀 Quick Start

### Step 1: Install .NET Global Tool

```bash
# Install latest version
dotnet tool install --global DatabaseMcpServer

# Verify installation
DatabaseMcpServer --version
```

### Step 2: Create Database Configuration File

Create `databases.json` configuration file:

```json
{
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
dnx DatabaseMcpServer@2.0.1-test --yes
```

**MCP Configuration**:

```json
{
  "mcpServers": {
    "database": {
      "command": "dnx",
      "args": ["DatabaseMcpServer@2.0.1-test", "--yes"],
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
dotnet run
```

**MCP Configuration**:

```json
{
  "mcpServers": {
    "database": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/DatabaseMcpServer"],
      "env": {
        "DB_CONFIG_PATH": "D:\\config\\databases.json"
      }
    }
  }
}
```

## ⚙️ Configuration Guide

DatabaseMcpServer 2.0.1-test uses JSON configuration file for unified database connection management.

### Configuration File (Required)

Specify the **absolute path** of the configuration file through the environment variable `DB_CONFIG_PATH`:

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

**Configuration File Format (databases.json):**

```json
{
  "databases": [
    {
      "name": "mysql-main",
      "connectionString": "Server=localhost;Database=myapp;User=root;Password=123456;",
      "dbType": "MySql",
      "description": "MySQL Main Database",
      "isDefault": true,
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

**Multi-Database Management Tools:**
- `list_databases` - List all available database connections
- `switch_database` - Switch to a specified database
- `get_current_database` - Get current active database
- `test_connection_by_name` - Test connection for a specific database

**Performance Optimization Tools:**
- `health_check` - Perform health checks on all database connections (response time, connection status)
- `test_connection_with_retry` - Connection test with automatic retry (exponential backoff strategy)

---

## 🌐 Environment Configuration

### Required Environment Variables
- `DB_CONFIG_PATH`: Database configuration file path (required)
  - Example: `D:\config\databases.json`

### Optional Environment Variables
- `SEQ_SERVER_URL`: Seq log server address (optional)
- `SEQ_API_KEY`: Seq API key (optional)
- `DB_DDL_WHITELIST`: DDL operation whitelist (optional, semicolon-separated regex patterns)

### Database-Specific Optimization Configuration
Starting from version 2.0.1-test, all database-specific optimization configurations are set in the `optimizationSettings` section of `databases.json`.

**Detailed Configuration Documentation**:
- [MySQL Configuration Guide](DatabaseSetting/MySQL.md)
- [SQL Server Configuration Guide](DatabaseSetting/SQLServer.md)
- [Oracle Configuration Guide](DatabaseSetting/Oracle.md)
- [PostgreSQL Configuration Guide](DatabaseSetting/PostgreSQL.md)
- [SQLite Configuration Guide](DatabaseSetting/SQLite.md)
- [DaMeng Database Configuration Guide](DatabaseSetting/DM.md)
- [KingbaseES Configuration Guide](DatabaseSetting/Kdbndp.md)
- [GaussDB Configuration Guide](DatabaseSetting/GaussDB.md)
- [QuestDB Configuration Guide](DatabaseSetting/QuestDB.md)
- [Configuration Index](DatabaseSetting/README.md)

---

## 🔄 Migration from 1.x to 2.0

### ⚠️ Breaking Changes

DatabaseMcpServer 2.0.1-test has removed environment variable configuration method and unified to use JSON configuration file.

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
| `DB_QUESTDB_SYNC_WAL` | `databases[].optimizationSettings.syncWal` |
| `DB_ORACLE_CAMEL_CASE` | `databases[].optimizationSettings.camelCase` |
| `DB_POSTGRES_AUTO_TO_LOWER` | `databases[].optimizationSettings.autoToLower` |
| `DB_SQLITE_ENABLE_DEFAULT_VALUE` | `databases[].optimizationSettings.enableDefaultValue` |
| `DB_DISABLE_NVARCHAR` | `databases[].optimizationSettings.disableNvarchar` |

For complete mapping table, please refer to each database configuration documentation.

#### 3. Automatic Migration Detection

If you are still using old environment variable configuration, DatabaseMcpServer 2.0.1-test will automatically detect and display detailed migration prompts.

### Common Database Connection String Examples

| Database | Connection String Example | Detailed Documentation |
|----------|--------------------------|------------------------|
| **MySQL** | `Server=localhost;Port=3306;Database=mydb;User=root;Password=123456;` | [MySQL.md](DatabaseSetting/MySQL.md) |
| **SQL Server** | `Server=localhost;Database=mydb;User Id=sa;Password=123456;` | [SQLServer.md](DatabaseSetting/SQLServer.md) |
| **PostgreSQL** | `Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=123456;` | [PostgreSQL.md](DatabaseSetting/PostgreSQL.md) |
| **Oracle** | `Data Source=localhost/orcl;User ID=system;Password=oracle123;` | [Oracle.md](DatabaseSetting/Oracle.md) |
| **SQLite** | `Data Source=mydb.db;` | [SQLite.md](DatabaseSetting/SQLite.md) |
| **DaMeng Database** | `Server=localhost;Port=5236;Database=mydb;User=SYSDBA;Password=SYSDBA001;` | [DM.md](DatabaseSetting/DM.md) |
| **KingbaseES** | `Server=localhost;Port=54321;Database=mydb;User=SYSTEM;Password=system123;` | [Kdbndp.md](DatabaseSetting/Kdbndp.md) |
| **GaussDB** | `PORT=5432;DATABASE=mydb;HOST=localhost;PASSWORD=Gauss@123;USER ID=gaussdb;` | [GaussDB.md](DatabaseSetting/GaussDB.md) |
| **QuestDB** | `host=localhost;port=8812;username=admin;password=quest;database=mydb;` | [QuestDB.md](DatabaseSetting/QuestDB.md) |

For more connection strings and optimization configurations, please refer to the detailed documentation in the [DatabaseSetting/](DatabaseSetting/) directory.

---

## 📋 Complete Feature List (55+ Tools)

### 🔌 1. Connection and Configuration Management (9 tools)

**Basic Connection Management**:
- **test_connection** - Test current database connection
- **test_connection_by_name** - Test connection for a specific database
- **get_database_config** - Get current database configuration information
- **validate_configuration** - Validate if database configuration is correct

**Multi-Database Management**:
- **list_databases** - List all available database connections
- **switch_database** - Switch to a specified database
- **get_current_database** - Get current active database

**Performance and Health Checks**:
- **health_check** - Perform health checks on all database connections (response time, connection status)
- **test_connection_with_retry** - Connection test with automatic retry (exponential backoff strategy)

### 🔍 2. Database Schema Queries (12 tools)

- **get_data_base_list** - Get all database names
- **get_table_info_list** - Get all table names
- **get_view_info_list** - Query all views
- **get_column_infos_by_table_name** - Get column information by table name
- **get_table_schema** - Get complete table structure information
- **get_is_identities** - Get identity columns
- **get_primaries** - Get primary keys
- **get_index_list** - Get all index name collections
- **get_proc_list** - Get stored procedure name collections
- **get_func_list** - Get function collections
- **get_trigger_names** - Get trigger collections by table name
- **get_db_types** - Get database type collections

### 🔎 3. Existence Checks (7 tools)

- **is_any_table** - Check if table exists
- **is_any_column** - Check if column exists
- **is_primary_key** - Check if primary key exists
- **is_identity** - Check if identity exists
- **is_any_constraint** - Check if constraint exists
- **is_any_index** - Check if index exists
- **is_any_table_remark** - Check if table description exists

### 📊 4. Data Query Tools (17 tools)

**Basic Queries:**

- **sql_query** - Execute SQL query and return strongly typed entity collection (supports parameterized queries)
- **sql_query_single** - Execute SQL query and return single record
- **get_data_reader** - Get DataReader data (automatically handles disposal)

**Advanced Queries:**

- **get_data_set_all** - Get multiple result sets, supports executing multiple queries at once
- **sql_query_multiple** - Execute query and return two result sets
- **sql_query_with_in_parameter** - Handle IN parameter queries, supports array parameters

**Scalar Value Queries:**

- **get_scalar** - Get first row first column value (scalar value)
- **get_string** - Get first row first column string value
- **get_int** - Get first row first column integer value
- **get_long** - Get first row first column long integer value
- **get_double** - Get first row first column double precision floating point value
- **get_decimal** - Get first row first column decimal value
- **get_date_time** - Get first row first column datetime value

### ✏️ 5. Data Operation Tools (9 tools)

- **execute_command** - Execute SQL commands (INSERT, UPDATE, DELETE)
- **insert_data** - Insert data into table
- **update_data** - Update data in table
- **delete_data** - Delete data from table
- **execute_transaction** - Execute transaction containing multiple SQL commands
- **batch_execute_commands** - Batch execute SQL commands (performance optimized)
- **call_stored_procedure** - Call stored procedure (simple usage)
- **call_stored_procedure_with_output** - Call stored procedure with output parameters
- **execute_command_with_go** - Execute SQL Server script containing GO statements

### 🛠️ 6. Database Schema Operations (High Risk) (6 core tools)

**Table Operations:**

- **drop_table** - Drop table
- **truncate_table** - Truncate table
- **backup_table** - Backup table
- **rename_table** - Rename table

**Column Operations:**

- **add_column** - Add column
- **update_column** - Update column
- **drop_column** - Drop column
- **rename_column** - Rename column

**Constraints and Indexes:**

- **add_primary_key** - Add primary key
- **drop_constraint** - Drop constraint
- **create_index** - Create index or unique constraint

**Others:**

- **add_default_value** - Add default value
- **add_table_remark** - Add table description
- **add_column_remark** - Add column description

*For complete tool list, please refer to [.mcp/server.json](.mcp/server.json)*

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

### Example 3: Data Statistics and Analysis

**Aggregate Query**

```
Count product quantity and average price for each category in products table
```

**Multi-result Set Query**

```
Query simultaneously: 1) Total users and active user count 2) Order data for last 7 days
```

**Scalar Value Query**

```
Get total amount from orders table where order status is "completed"
```

### Example 4: Data Operations

**Insert New Data**

```
Insert new product into products table: name "MacBook Pro M3", price 14999, stock 50
```

**Batch Update**

```
Batch update VIP status for following users: user IDs 1,3,5,7,9 set to VIP, others set to regular user
```

**Transaction Operation**

```
Execute transfer operation: transfer 500 yuan from account A (ID:1001) to account B (ID:1002)
```

### Example 5: Schema Queries

**Get Table Structure**

```
Get complete structure information of orders table: columns, primary keys, indexes, identity columns, etc.
```

**Query Index Information**

```
Query all index information of users table
```

**Check if Table Exists**

```
Check if table named "user_logs" exists in database
```

### Example 6: Stored Procedure Calls

**Simple Stored Procedure**

```
Call stored procedure sp_monthly_report, pass parameters year 2025, month 11
```

**Stored Procedure with Output Parameters**

```
Call stored procedure sp_user_statistics, pass user ID 1001, get total order count and total amount for this user
```

## 🔒 Security Features

### Dangerous Operation Detection

System automatically detects and blocks the following dangerous operations:

- `DROP TABLE` / `DROP DATABASE` - Delete table/database
- `TRUNCATE TABLE` - Clear table data
- `ALTER TABLE` - Modify table structure
- `DELETE` / `UPDATE` without WHERE condition

To execute these operations, please use dedicated schema operation tools (such as `drop_table`, `truncate_table`, etc.), which will clearly prompt risks.

### SQL Injection Protection

All queries support parameterized queries, automatically preventing SQL injection:

```json
{
  "sql": "SELECT * FROM users WHERE age > @age AND city = @city",
  "parameters": "{\"age\":18,\"city\":\"Beijing\"}"
}
```

### Sensitive Information Protection

- Passwords in connection strings are automatically hidden (displayed as `Password=****`)
- Complete connection strings are not output in logs
- Configuration information is automatically desensitized when returned

## 💻 Development Guide

### Local Development

```bash
# Clone project
git clone https://github.com/ttcc666/DatabaseMcpServer.git
cd DatabaseMcpServer

# Create databases.json configuration file, then run
DB_CONFIG_PATH="path/to/databases.json" dotnet run

# Build project
dotnet build

# Run tests
dotnet test

# Package and publish
dotnet pack -c Release
```

### Adding New Tools

1. **Create Tool Class File**

   ```bash
   # Create new tool class in Tools/ directory
   # Management/ - Connection and schema management
   # Query/ - Query tools
   # Command/ - Command tools
   ```

2. **Implement Tool Class**

   ```csharp
   using System.ComponentModel;
   using ModelContextProtocol.Server;
   using DatabaseMcpServer.Interfaces;

   namespace DatabaseMcpServer.Tools;

   internal class YourNewTools
   {
       private readonly IDatabaseConfigService _databaseConfig;
       private readonly IDatabaseHelperService _databaseHelper;

       public YourNewTools(IDatabaseConfigService databaseConfig, IDatabaseHelperService databaseHelper)
       {
           _databaseConfig = databaseConfig;
           _databaseHelper = databaseHelper;
       }

       [McpServerTool]
       [Description("Your tool description")]
       public string YourMethod([Description("Parameter description")] string parameter)
       {
           using var db = _databaseConfig.CreateClient();
           // Implement your functionality
           return _databaseHelper.SerializeResult(new { success = true, data = "result" });
       }
   }
   ```

3. **Register Tool**
   In `Program.cs`:

   ```csharp
   builder.Services
       .AddMcpServer()
       .WithStdioServerTransport()
       .WithTools<ConnectionTools>()
       .WithTools<SchemaTools>()
       .WithTools<QueryTools>()
       .WithTools<CommandTools>()
       .WithTools<YourNewTools>(); // Add your tool
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

- **.NET 9.0** - Latest .NET platform
- **ModelContextProtocol 0.4.0** - MCP protocol C# SDK
- **SqlSugarCore 5.1.4** - Lightweight high-performance ORM
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

- This project has released version 2.0.1-test
- Version 2.0.1-test contains breaking changes, please refer to the migration guide
- Please test thoroughly before using in production environment
- Regularly backup important data
- Pay attention to sensitive information protection in configuration

---

**DatabaseMCP** - Let AI assistants easily operate databases!

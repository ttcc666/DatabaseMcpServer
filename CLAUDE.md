# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DatabaseMcpServer is a Model Context Protocol (MCP) server that provides database operations through a standardized interface. It's built with .NET 9.0 and uses SqlSugarCore ORM for database abstraction, supporting MySQL, SQL Server, SQLite, PostgreSQL, and Oracle databases.

## Development Commands

### Build and Run
```bash
# Development mode
dotnet run --project .

# Build for release
dotnet build -c Release

# Publish self-contained executable
dotnet publish -c Release --self-contained true -p:PublishSingleFile=true

# Install as global tool (after packing)
dotnet tool install --global DatabaseMcpServer --version 0.1.0-beta --add-source ./nupkg
```

### Testing
```bash
# Run all tests
dotnet test

# Test specific database connection
DB_CONNECTION_STRING="Server=localhost;Database=test;User=root;Password=123456;" DB_TYPE="MySql" dotnet run
```

## Architecture

### Core Components

**Program.cs** - Application entry point that configures MCP server with dependency injection and registers all tool classes.

**Services/DatabaseConfigService.cs** - Static service managing global database configuration through environment variables (`DB_CONNECTION_STRING`, `DB_TYPE`). Creates and manages the global SqlSugarClient instance.

**Helpers/DatabaseHelper.cs** - Static utility class providing common database operations, parameter parsing, dangerous operation detection, and connection string sanitization.

### Tool Organization

Tools are organized by functional domains under `Tools/`:

- **Management/** - Connection and schema management tools
  - `ConnectionTools.cs` - Connection testing and configuration validation
  - `SchemaTools.cs` - Database schema inspection (tables, columns, indexes)

- **Query/** - Data retrieval operations
  - `QueryTools.cs` - SQL query execution with support for single/multiple result sets

- **Command/** - Data modification operations
  - `CommandTools.cs` - Insert, update, delete, and transaction operations

### Configuration Pattern

The project uses environment variable-driven configuration:
- `DB_CONNECTION_STRING` - Database connection string
- `DB_TYPE` - Database type (MySql, SqlServer, Sqlite, PostgreSQL, Oracle)

All tools access the database through `DatabaseConfigService.CreateGlobalClient()` without needing connection parameters.

### Security Features

**Dangerous Operation Detection** - `DatabaseHelper.DetectDangerousOperation()` identifies potentially destructive SQL operations (DROP, TRUNCATE, ALTER, CREATE).

**Parameter Safety** - All queries support parameterized execution through `SugarParameter` objects parsed via `DatabaseHelper.ParseParameters()`.

**Credential Protection** - Connection strings automatically mask passwords in logs and error messages.

## Adding New Tools

1. Create tool class in appropriate `Tools/` subdirectory
2. Use `[McpServerTool]` attribute on class and `[Description]` on methods
3. Access database via `DatabaseConfigService.CreateGlobalClient()`
4. Register in `Program.cs` using `.WithTools<YourToolClass>()`
5. Follow existing patterns for error handling and parameter validation

## MCP Configuration

Example client configuration for development:
```json
{
  "command": "dotnet",
  "args": ["run", "--project", "path/to/DatabaseMcpServer"],
  "env": {
    "DB_CONNECTION_STRING": "your_connection_string",
    "DB_TYPE": "MySql"
  }
}
```

For production, use the published executable or NuGet global tool installation.
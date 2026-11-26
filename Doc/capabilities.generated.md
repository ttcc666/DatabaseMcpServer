# Generated capability reference

Generated from tool list --json --all by scripts/update-tool-manifest.ps1. Do not edit by hand.

Schema sections: columns, primaryKeys, identityColumns, indexes, foreignKeys, constraints, comments. Defaults: columns, primaryKeys, identityColumns, indexes.

An allowlist limits a capability to those products. An exclusion list marks known unsupported products; other products remain provider-dependent, not verified. Runtime configuration and permissions still apply.

| Capability | Allowed products | Excluded products |
| --- | --- | --- |
| Sql |  | MongoDb |
| MongoCollections | MongoDb |  |
| BoundedResults | Sqlite, MySql, PostgreSQL, SqlServer |  |
| NativeMetadata | Sqlite, MySql, PostgreSQL, SqlServer |  |
| AsyncSchema | Sqlite, MySql, PostgreSQL, SqlServer, DuckDB |  |
| EstimatedPlan | Sqlite, MySql, PostgreSQL, SqlServer |  |
| AtomicDml | Sqlite, PostgreSQL, SqlServer |  |
| StoredProcedures | MySql, SqlServer |  |
| SqlServerBatches | SqlServer |  |
| Routines |  | Sqlite, MongoDb, QuestDB, ClickHouse, TDengine, DuckDB, Doris |
| DatabaseListing |  | Sqlite, MongoDb, QuestDB, Oracle, Dm, OceanBaseForOracle, GBase |
| ConstraintLookup |  | Sqlite, MongoDb, QuestDB, ClickHouse, TDengine, DuckDB, Doris |
| RenameColumn |  | Sqlite, MongoDb, ClickHouse, TDengine, GBase |
| RenameTable |  | MongoDb, ClickHouse, Oscar, QuestDB, GBase, TDengine |
| AddPrimaryKey |  | Sqlite, MongoDb, QuestDB, ClickHouse, TDengine, Doris |
| DropConstraint |  | Sqlite, MongoDb, QuestDB, ClickHouse, TDengine, DuckDB, Doris |
| DropIndex |  | MongoDb, TDengine, QuestDB, ClickHouse |
| DefaultValue |  | Sqlite, MongoDb |
| SchemaComments |  | Sqlite, MongoDb, QuestDB, TDengine, Doris |
| TableRemarkLookup |  | Sqlite, MongoDb, MySql, Tidb, OceanBase, PolarDB, GoldenDB, Doris, TDSQL, PostgreSQL, Vastbase, GaussDB, OpenGauss, GaussDBNative, Kdbndp, Oscar, HG, DuckDB, ClickHouse, TDengine, QuestDB, GBase |
| DeleteColumnRemark |  | Sqlite, MongoDb, MySql, Tidb, OceanBase, PolarDB, GoldenDB, Doris, TDSQL, ClickHouse, TDengine, QuestDB |
| InsertIdentity | Sqlite, MySql, SqlServer |  |

## ClickHouse

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | unsupported |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | unsupported |
| schemaOperations | RenameColumn | unsupported |
| schemaOperations | RenameTable | unsupported |
| schemaOperations | AddPrimaryKey | unsupported |
| schemaOperations | DropConstraint | unsupported |
| schemaOperations | DropIndex | unsupported |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | unsupported |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## Dm

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | unsupported |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | provider-dependent |
| schemaOperations | DeleteColumnRemark | provider-dependent |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## Doris

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | unsupported |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | unsupported |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | unsupported |
| schemaOperations | DropConstraint | unsupported |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | unsupported |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | unsupported |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## DuckDB

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | supported |
| schemaSections | primaryKeys | supported |
| schemaSections | identityColumns | supported |
| schemaSections | indexes | supported |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | unsupported |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | unsupported |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | unsupported |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | provider-dependent |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## GBase

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | unsupported |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | unsupported |
| schemaOperations | RenameTable | unsupported |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | provider-dependent |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## GaussDB

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | provider-dependent |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## GaussDBNative

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | provider-dependent |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## GoldenDB

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | unsupported |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## HG

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | provider-dependent |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## Kdbndp

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | provider-dependent |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## MongoDb

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | unsupported |
| schemaSections | primaryKeys | unsupported |
| schemaSections | identityColumns | unsupported |
| schemaSections | indexes | unsupported |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | unsupported |
| schemaOperations | DatabaseListing | unsupported |
| schemaOperations | ConstraintLookup | unsupported |
| schemaOperations | RenameColumn | unsupported |
| schemaOperations | RenameTable | unsupported |
| schemaOperations | AddPrimaryKey | unsupported |
| schemaOperations | DropConstraint | unsupported |
| schemaOperations | DropIndex | unsupported |
| schemaOperations | DefaultValue | unsupported |
| schemaOperations | SchemaComments | unsupported |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | unsupported |
| cliExport | jsonl | unsupported |
| cliExport | csv | unsupported |

## MySql

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | supported |
| schemaSections | primaryKeys | supported |
| schemaSections | identityColumns | supported |
| schemaSections | indexes | supported |
| schemaSections | foreignKeys | supported |
| schemaSections | constraints | supported |
| schemaSections | comments | supported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | unsupported |
| cliExport | jsonl | supported |
| cliExport | csv | supported |

## OceanBase

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | unsupported |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## OceanBaseForOracle

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | unsupported |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | provider-dependent |
| schemaOperations | DeleteColumnRemark | provider-dependent |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## OpenGauss

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | provider-dependent |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## Oracle

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | unsupported |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | provider-dependent |
| schemaOperations | DeleteColumnRemark | provider-dependent |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## Oscar

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | unsupported |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | provider-dependent |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## PolarDB

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | unsupported |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## PostgreSQL

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | supported |
| schemaSections | primaryKeys | supported |
| schemaSections | identityColumns | supported |
| schemaSections | indexes | supported |
| schemaSections | foreignKeys | supported |
| schemaSections | constraints | supported |
| schemaSections | comments | supported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | provider-dependent |
| cliExport | jsonl | supported |
| cliExport | csv | supported |

## QuestDB

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | unsupported |
| schemaOperations | DatabaseListing | unsupported |
| schemaOperations | ConstraintLookup | unsupported |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | unsupported |
| schemaOperations | AddPrimaryKey | unsupported |
| schemaOperations | DropConstraint | unsupported |
| schemaOperations | DropIndex | unsupported |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | unsupported |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | unsupported |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## SqlServer

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | supported |
| schemaSections | primaryKeys | supported |
| schemaSections | identityColumns | supported |
| schemaSections | indexes | supported |
| schemaSections | foreignKeys | supported |
| schemaSections | constraints | supported |
| schemaSections | comments | supported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | provider-dependent |
| schemaOperations | DeleteColumnRemark | provider-dependent |
| cliExport | jsonl | supported |
| cliExport | csv | supported |

## Sqlite

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | supported |
| schemaSections | primaryKeys | supported |
| schemaSections | identityColumns | supported |
| schemaSections | indexes | supported |
| schemaSections | foreignKeys | supported |
| schemaSections | constraints | partial |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | unsupported |
| schemaOperations | DatabaseListing | unsupported |
| schemaOperations | ConstraintLookup | unsupported |
| schemaOperations | RenameColumn | unsupported |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | unsupported |
| schemaOperations | DropConstraint | unsupported |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | unsupported |
| schemaOperations | SchemaComments | unsupported |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | unsupported |
| cliExport | jsonl | supported |
| cliExport | csv | supported |

## TDSQL

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | unsupported |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## TDengine

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | unsupported |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | unsupported |
| schemaOperations | RenameColumn | unsupported |
| schemaOperations | RenameTable | unsupported |
| schemaOperations | AddPrimaryKey | unsupported |
| schemaOperations | DropConstraint | unsupported |
| schemaOperations | DropIndex | unsupported |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | unsupported |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | unsupported |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## Tidb

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | unsupported |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

## Vastbase

| Group | Feature | Support |
| --- | --- | --- |
| schemaSections | columns | provider-dependent |
| schemaSections | primaryKeys | provider-dependent |
| schemaSections | identityColumns | provider-dependent |
| schemaSections | indexes | provider-dependent |
| schemaSections | foreignKeys | unsupported |
| schemaSections | constraints | unsupported |
| schemaSections | comments | unsupported |
| schemaOperations | Routines | provider-dependent |
| schemaOperations | DatabaseListing | provider-dependent |
| schemaOperations | ConstraintLookup | provider-dependent |
| schemaOperations | RenameColumn | provider-dependent |
| schemaOperations | RenameTable | provider-dependent |
| schemaOperations | AddPrimaryKey | provider-dependent |
| schemaOperations | DropConstraint | provider-dependent |
| schemaOperations | DropIndex | provider-dependent |
| schemaOperations | DefaultValue | provider-dependent |
| schemaOperations | SchemaComments | provider-dependent |
| schemaOperations | TableRemarkLookup | unsupported |
| schemaOperations | DeleteColumnRemark | provider-dependent |
| cliExport | jsonl | provider-dependent-unverified |
| cliExport | csv | provider-dependent-unverified |

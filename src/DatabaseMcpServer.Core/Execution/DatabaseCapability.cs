namespace DatabaseMcpServer.Helpers;

public enum DatabaseCapability
{
    Sql,
    MongoCollections,
    BoundedResults,
    NativeMetadata,
    AsyncSchema,
    EstimatedPlan,
    AtomicDml,
    StoredProcedures,
    SqlServerBatches,
    Routines,
    DatabaseListing,
    ConstraintLookup,
    RenameColumn,
    RenameTable,
    AddPrimaryKey,
    DropConstraint,
    DropIndex,
    DefaultValue,
    SchemaComments,
    TableRemarkLookup,
    DeleteColumnRemark,
    InsertIdentity
}

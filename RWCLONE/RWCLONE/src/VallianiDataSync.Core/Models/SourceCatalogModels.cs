namespace VallianiDataSync.Core.Models;

/// <summary>Read-only metadata describing the configured source database.</summary>
public sealed class SourceCatalog
{
    public List<SourceTable> Tables { get; set; } = new();
    public List<SourceRelationship> Relationships { get; set; } = new();
}

public sealed class SourceTable
{
    public string Schema { get; set; } = "dbo";
    public string Name { get; set; } = string.Empty;
    public List<SourceColumn> Columns { get; set; } = new();
}

public sealed class SourceColumn
{
    public string Name { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public string FieldId { get; set; } = string.Empty;
}

public sealed class SourceRelationship
{
    public string Name { get; set; } = string.Empty;
    public string FromTable { get; set; } = string.Empty;
    public string FromColumn { get; set; } = string.Empty;
    public string ToTable { get; set; } = string.Empty;
    public string ToColumn { get; set; } = string.Empty;
}

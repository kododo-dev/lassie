namespace Lassie.Data.Auditing;

public enum AuditChangeType
{
    Modified,
    Deleted
}

public class AuditLog
{
    public long Id { get; set; }
    public required string EntityName { get; set; }
    public required string EntityId { get; set; }
    public AuditChangeType ChangeType { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
    public required string Snapshot { get; set; }
}

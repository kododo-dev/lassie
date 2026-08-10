namespace Lassie.Data.Auditing;

/// <summary>
/// Marks a property on an <see cref="IAuditable"/> entity as excluded from the
/// <see cref="AuditLog.Snapshot"/> JSON. Use for sensitive values (e.g. secrets/hashes)
/// that must not be persisted into the audit trail even though the rest of the entity
/// is tracked.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotAuditedAttribute : Attribute
{
}

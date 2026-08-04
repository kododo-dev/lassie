namespace Lassie.Data.Auditing;

/// <summary>
/// Marker interface: any entity implementing this opts into audit-history tracking.
/// A snapshot of its pre-change state is recorded in <see cref="AuditLog"/> whenever
/// it's modified or deleted.
/// </summary>
public interface IAuditable
{
}

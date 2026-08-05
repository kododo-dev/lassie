namespace Lassie.Data.Auditing;

/// <summary>
/// Marker interface: any entity implementing this opts into audit-history tracking.
/// A snapshot of its pre-change state is recorded in <see cref="AuditLog"/> whenever
/// it's modified or deleted.
/// </summary>
/// <remarks>
/// Snapshots are taken from EF Core's <c>OriginalValues</c>, which only reflects the
/// real prior row when the entity was loaded via a query first. Attach-and-mark-dirty
/// updates (e.g. <c>Attach(new License { Id = x }); Entry(x).State = Modified;</c>)
/// bypass this and would silently record the wrong "before" state. Always load an
/// <see cref="IAuditable"/> entity before mutating and saving it.
/// </remarks>
public interface IAuditable
{
}

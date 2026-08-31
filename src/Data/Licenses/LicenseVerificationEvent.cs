namespace Lassie.Data.Licenses;

// One row per verify-API call that resolved to a license. Deliberately NOT IAuditable:
// this is a per-read event log, not a before-image of a mutation, so it never rides the
// generic AuditLog path in LassieDbContext. No API key or key hash is ever stored here.
public class LicenseVerificationEvent
{
    public long Id { get; set; }

    public long LicenseId { get; set; }
    public License License { get; set; } = null!;

    public DateTimeOffset OccurredAtUtc { get; set; }

    // Resolved via HttpContext.Connection.RemoteIpAddress, which reflects the true external
    // caller only while Kestrel sits behind exactly one Caddy hop with no CDN in front (see
    // Current State Analysis in the plan). If a proxy hop is ever added, this becomes the
    // edge IP — ForwardedForRaw below is the hedge that lets the real IP be re-derived.
    public string? ClientIp { get; set; }

    // The User-Agent header as sent (often empty — a bare HttpClient sends none). Truncated
    // on the write side.
    public string? UserAgent { get; set; }

    // Raw inbound X-Forwarded-For / X-Original-For, verbatim and truncated. Kept as a
    // defensive record against a future change to the proxy topology — see ClientIp.
    public string? ForwardedForRaw { get; set; }

    // The license's computed Status at the instant of the call (Active / Expired /
    // Deactivated). The panel renders "valid" as ObservedStatus == Active.
    public LicenseStatus ObservedStatus { get; set; }
}

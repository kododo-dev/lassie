using Lassie.Data.Auditing;

namespace Lassie.Data.Licenses;

public enum LicenseStatus
{
    Active,
    Expired
}

public class License : IAuditable
{
    public long Id { get; set; }
    public required string Label { get; set; }
    public DateOnly? ExpiresOn { get; set; }

    [NotAudited]
    public required string ApiKeyHash { get; set; }

    public LicenseStatus Status =>
        ExpiresOn is null || ExpiresOn >= DateOnly.FromDateTime(DateTime.UtcNow)
            ? LicenseStatus.Active
            : LicenseStatus.Expired;
}

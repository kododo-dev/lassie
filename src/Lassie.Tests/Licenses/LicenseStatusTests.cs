using Lassie.Data.Licenses;
using Xunit;

namespace Lassie.Tests.Licenses;

public class LicenseStatusTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);
    private static readonly DateOnly Tomorrow = Today.AddDays(1);

    public static TheoryData<bool, DateOnly?, LicenseStatus> Cases() =>
        new()
        {
            // Deactivated trumps expiry in every case, regardless of ExpiresOn (PRD:
            // Deactivated > Expired > Active).
            { false, null, LicenseStatus.Deactivated },
            { false, Yesterday, LicenseStatus.Deactivated },
            { false, Today, LicenseStatus.Deactivated },
            { false, Tomorrow, LicenseStatus.Deactivated },

            { true, null, LicenseStatus.Active },
            // A license expiring "today" (UTC) is still active for the entire UTC day.
            { true, Today, LicenseStatus.Active },
            { true, Tomorrow, LicenseStatus.Active },
            { true, Yesterday, LicenseStatus.Expired },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Status_MatchesPrdPrecedence(bool isActive, DateOnly? expiresOn, LicenseStatus expected)
    {
        var license = new License
        {
            Label = "test-license",
            ApiKeyHash = "irrelevant-for-this-test",
            IsActive = isActive,
            ExpiresOn = expiresOn
        };

        Assert.Equal(expected, license.Status);
    }
}

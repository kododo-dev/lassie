using Lassie.Data.Auditing;

namespace Lassie.Data.Licenses;

public class License : IAuditable
{
    public long Id { get; set; }
    public required string Label { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public required string ApiKeyHash { get; set; }
}

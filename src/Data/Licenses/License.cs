namespace Lassie.Data.Licenses;

public class License
{
    public long Id { get; set; }
    public required string Label { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public required string ApiKeyHash { get; set; }
}

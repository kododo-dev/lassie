namespace Lassie.Data.LicenseFields;

public class LicenseFieldOption
{
    public long Id { get; set; }
    public long LicenseFieldId { get; set; }
    public required string Value { get; set; }
}

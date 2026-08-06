namespace Lassie.Data.LicenseFields;

public class LicenseField
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public LicenseFieldDataType DataType { get; set; }
    public List<LicenseFieldOption> Options { get; set; } = [];
}

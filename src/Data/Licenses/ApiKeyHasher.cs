using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Lassie.Data.Licenses;

public static class ApiKeyHasher
{
    public static (string RawKey, string Hash) Generate()
    {
        var rawKey = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (rawKey, Hash(rawKey));
    }

    public static string Hash(string rawKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));
}

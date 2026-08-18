using Lassie.Data.Licenses;
using Xunit;

namespace Lassie.Tests.Licenses;

public class ApiKeyHasherTests
{
    [Fact]
    public void Generate_RawKeyAndHash_AreNeverEqual()
    {
        var (rawKey, hash) = ApiKeyHasher.Generate();

        Assert.NotEqual(rawKey, hash);
    }

    [Fact]
    public void Generate_Hash_Is64CharacterUppercaseHex()
    {
        var (_, hash) = ApiKeyHasher.Generate();

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9A-F]{64}$", hash);
    }

    [Fact]
    public void Generate_CalledTwice_ProducesDistinctRawKeysAndHashes()
    {
        var (rawKey1, hash1) = ApiKeyHasher.Generate();
        var (rawKey2, hash2) = ApiKeyHasher.Generate();

        Assert.NotEqual(rawKey1, rawKey2);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Hash_CalledTwiceOnSameRawKey_IsDeterministic()
    {
        var (rawKey, _) = ApiKeyHasher.Generate();

        var hash1 = ApiKeyHasher.Hash(rawKey);
        var hash2 = ApiKeyHasher.Hash(rawKey);

        Assert.Equal(hash1, hash2);
    }
}

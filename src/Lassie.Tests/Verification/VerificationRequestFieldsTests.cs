using Lassie.Data.Verification;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Lassie.Tests.Verification;

public class VerificationRequestFieldsTests
{
    [Fact]
    public void Truncate_ReturnsNull_ForNullOrEmpty()
    {
        Assert.Null(VerificationRequestFields.Truncate(null, 10));
        Assert.Null(VerificationRequestFields.Truncate("", 10));
    }

    [Fact]
    public void Truncate_PassesThrough_WhenWithinLimit()
    {
        Assert.Equal("abc", VerificationRequestFields.Truncate("abc", 10));
        Assert.Equal("abcde", VerificationRequestFields.Truncate("abcde", 5));
    }

    [Fact]
    public void Truncate_CutsToMaxLength_WhenOverLimit()
    {
        Assert.Equal("abcde", VerificationRequestFields.Truncate("abcdefghij", 5));
        Assert.Equal(512, VerificationRequestFields.Truncate(new string('x', 1000), 512)!.Length);
    }

    [Fact]
    public void ReadForwardedFor_PrefersXForwardedFor()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers["X-Forwarded-For"] = "203.0.113.7";
        request.Headers["X-Original-For"] = "10.0.0.1";

        Assert.Equal("203.0.113.7", VerificationRequestFields.ReadForwardedFor(request));
    }

    [Fact]
    public void ReadForwardedFor_FallsBackToXOriginalFor()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers["X-Original-For"] = "198.51.100.4";

        Assert.Equal("198.51.100.4", VerificationRequestFields.ReadForwardedFor(request));
    }

    [Fact]
    public void ReadForwardedFor_ReturnsNull_WhenNeitherHeaderPresent()
    {
        Assert.Null(VerificationRequestFields.ReadForwardedFor(new DefaultHttpContext().Request));
    }
}

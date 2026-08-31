using Microsoft.AspNetCore.Http;

namespace Lassie.Data.Verification;

// Small pure helpers for turning a verify request into audit-row fields. Kept out of the
// endpoint lambda so they can be unit-tested without a DB (InternalsVisibleTo Lassie.Tests).
internal static class VerificationRequestFields
{
    public static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];

    // The raw inbound forwarding chain. With a single Caddy hop the ForwardedHeaders
    // middleware consumes the right-most X-Forwarded-For entry and stashes the original in
    // X-Original-For, so fall back to that when X-Forwarded-For is already empty.
    public static string? ReadForwardedFor(HttpRequest request)
    {
        var value = request.Headers["X-Forwarded-For"].ToString();
        if (string.IsNullOrEmpty(value))
        {
            value = request.Headers["X-Original-For"].ToString();
        }

        return string.IsNullOrEmpty(value) ? null : value;
    }
}

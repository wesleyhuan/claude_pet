using System.Net.Http;

namespace ClaudePet.Tests;

// SubscriptionUsageReader.PollAsync builds its Authorization header via
// HttpHeaders.TryAddWithoutValidation instead of the validating .Add(...)
// (see whole-branch review finding #2). These tests pin the exact framework
// contract that fix relies on: .Add throws FormatException with the raw
// header value - i.e. the "Bearer <token>" string - embedded verbatim in the
// exception message (which PollAsync's catch block would otherwise log to
// debug.log), while TryAddWithoutValidation never throws for any value
// content, so a malformed/corrupted token can never reach a log message via
// this path. SubscriptionUsageReader itself isn't unit-testable without
// injecting an HttpMessageHandler (out of scope per the fix's review
// findings), so this test targets the underlying HttpHeaders behavior
// directly rather than PollAsync.
public class SubscriptionUsageAuthorizationHeaderTests
{
    private const string MalformedValue = "Bearer bad\ntoken";

    [Fact]
    public void Add_WithControlCharacterInValue_ThrowsAndEmbedsValueInMessage()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.invalid");

        var ex = Assert.Throws<FormatException>(() => request.Headers.Add("Authorization", MalformedValue));

        // This is exactly the leak the finding describes: the raw value ends
        // up in the exception message, which the reader's catch block logs.
        Assert.Contains(MalformedValue, ex.Message);
    }

    [Fact]
    public void TryAddWithoutValidation_WithControlCharacterInValue_DoesNotThrow()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.invalid");

        var exception = Record.Exception(() => request.Headers.TryAddWithoutValidation("Authorization", MalformedValue));

        Assert.Null(exception);
    }
}

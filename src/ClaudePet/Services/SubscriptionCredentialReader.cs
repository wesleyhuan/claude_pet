using System.IO;
using System.Text.Json;

namespace ClaudePet.Services;

public sealed class SubscriptionCredentialReader
{
    private readonly string _filePath;
    private readonly Action<string>? _onError;

    public SubscriptionCredentialReader(string filePath, Action<string>? onError = null)
    {
        _filePath = filePath;
        _onError = onError;
    }

    // Never throws. Never reports the token value to onError - only short,
    // fixed diagnostic strings describing which check failed.
    public (string AccessToken, DateTimeOffset ExpiresAt)? TryRead()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _onError?.Invoke("credential file missing");
                return null;
            }

            var json = File.ReadAllText(_filePath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
            {
                _onError?.Invoke("credential file missing claudeAiOauth field");
                return null;
            }

            if (!oauth.TryGetProperty("accessToken", out var tokenElement)
                || tokenElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(tokenElement.GetString()))
            {
                _onError?.Invoke("credential file missing accessToken field");
                return null;
            }

            if (!oauth.TryGetProperty("expiresAt", out var expiresElement)
                || expiresElement.ValueKind != JsonValueKind.Number)
            {
                _onError?.Invoke("credential file missing expiresAt field");
                return null;
            }

            return (tokenElement.GetString()!, DateTimeOffset.FromUnixTimeMilliseconds(expiresElement.GetInt64()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _onError?.Invoke("credential file malformed");
            return null;
        }
    }
}

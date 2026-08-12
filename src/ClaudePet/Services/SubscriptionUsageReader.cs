using System.Net.Http;
using ClaudePet.Logging;
using ClaudePet.Models;

namespace ClaudePet.Services;

public sealed class SubscriptionUsageReader : IDisposable
{
    private const string CountTokensUrl = "https://api.anthropic.com/v1/messages/count_tokens";
    private const string AnthropicVersion = "2023-06-01";
    private const string OAuthBeta = "oauth-2025-04-20";
    // Cheapest available model; count_tokens does not bill for generation
    // regardless of model choice, but a valid model id is required. Matches
    // RateLimitReader's request body exactly - same endpoint, different auth.
    private const string RequestBody =
        """{"model":"claude-haiku-4-5","messages":[{"role":"user","content":"hi"}]}""";

    private const double BaseIntervalMs = 5 * 60 * 1000;
    private const double MaxIntervalMs = 60 * 60 * 1000;

    private readonly SubscriptionCredentialReader _credentialReader;
    private readonly DebugLog _log;
    private readonly HttpClient _httpClient = new();
    private readonly System.Timers.Timer _pollTimer;
    private (string AccessToken, DateTimeOffset ExpiresAt)? _cachedCredential;
    private bool _hasLoggedHeadersOnce;
    private string? _lastLoggedCredentialError;
    private string? _lastLoggedErrorStatus;
    private string? _lastLoggedExceptionType;

    public event Action<SubscriptionUsageSnapshot>? SubscriptionUsageChanged;

    public SubscriptionUsageReader(string credentialFilePath, DebugLog log)
    {
        _log = log;
        _credentialReader = new SubscriptionCredentialReader(credentialFilePath, OnCredentialError);

        _pollTimer = new System.Timers.Timer(BaseIntervalMs) { AutoReset = true };
        _pollTimer.Elapsed += async (_, _) => await PollAsync();
    }

    public void Start()
    {
        _ = Task.Run(PollAsync);
        _pollTimer.Start();
    }

    public void Stop() => _pollTimer.Stop();

    private void OnCredentialError(string reason)
    {
        if (_lastLoggedCredentialError == reason)
            return;
        _log.Write($"SubscriptionUsageReader: {reason}");
        _lastLoggedCredentialError = reason;
    }

    private async Task PollAsync()
    {
        // Local file read, not a network call - no backoff penalty for a
        // missing/malformed credential file. Just skip this cycle at
        // whatever the current interval already is.
        var credential = GetValidCredential();
        if (credential is null)
            return;
        _lastLoggedCredentialError = null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, CountTokensUrl);
            request.Headers.Add("Authorization", $"Bearer {credential.Value.AccessToken}");
            request.Headers.Add("anthropic-version", AnthropicVersion);
            request.Headers.Add("anthropic-beta", OAuthBeta);
            request.Content = new StringContent(RequestBody, System.Text.Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers)
                headers[header.Key] = string.Join(",", header.Value);
            foreach (var header in response.Content.Headers)
                headers[header.Key] = string.Join(",", header.Value);

            if (!_hasLoggedHeadersOnce)
            {
                _hasLoggedHeadersOnce = true;
                _log.Write(
                    "SubscriptionUsageReader: first response headers (for verifying real anthropic-ratelimit-unified-* names): " +
                    string.Join("; ", headers.Select(kv => $"{kv.Key}={kv.Value}")));
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // Token may have been rotated by Claude Code's own refresh
                // flow; force a fresh credential-file read next cycle
                // instead of retrying the same now-invalid cached token.
                _cachedCredential = null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = ((int)response.StatusCode).ToString();
                if (_lastLoggedErrorStatus != status)
                {
                    _log.Write($"SubscriptionUsageReader: non-success HTTP {status} from count_tokens");
                    _lastLoggedErrorStatus = status;
                }
                ApplyBackoff();
                return;
            }
            _lastLoggedErrorStatus = null;
            _lastLoggedExceptionType = null;

            var snapshot = SubscriptionUsageParser.Parse(headers);
            if (snapshot is null)
            {
                _log.Write("SubscriptionUsageReader: response missing expected anthropic-ratelimit-unified-* headers");
                ApplyBackoff();
                return;
            }

            ResetBackoff();
            SubscriptionUsageChanged?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            var exceptionType = ex.GetType().Name;
            if (_lastLoggedExceptionType != exceptionType)
            {
                _log.Write($"SubscriptionUsageReader.PollAsync exception: {ex}");
                _lastLoggedExceptionType = exceptionType;
            }
            ApplyBackoff();
        }
    }

    private (string AccessToken, DateTimeOffset ExpiresAt)? GetValidCredential()
    {
        // 1-minute safety margin: avoid sending a request with a token that
        // expires mid-flight.
        if (_cachedCredential is { } cached && DateTimeOffset.UtcNow < cached.ExpiresAt.AddMinutes(-1))
            return cached;

        _cachedCredential = _credentialReader.TryRead();
        return _cachedCredential;
    }

    private void ApplyBackoff() => _pollTimer.Interval = Math.Min(_pollTimer.Interval * 2, MaxIntervalMs);

    private void ResetBackoff() => _pollTimer.Interval = BaseIntervalMs;

    public void Dispose()
    {
        _pollTimer.Dispose();
        _httpClient.Dispose();
    }
}

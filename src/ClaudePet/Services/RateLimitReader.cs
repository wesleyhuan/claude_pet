using System.Net.Http;
using ClaudePet.Logging;
using ClaudePet.Models;

namespace ClaudePet.Services;

public sealed class RateLimitReader : IDisposable
{
    private const string CountTokensUrl = "https://api.anthropic.com/v1/messages/count_tokens";
    private const string AnthropicVersion = "2023-06-01";
    // Cheapest available model; count_tokens does not bill for generation
    // regardless of model choice, but a valid model id is required.
    private const string RequestBody =
        """{"model":"claude-haiku-4-5","messages":[{"role":"user","content":"hi"}]}""";

    private readonly string _apiKey;
    private readonly DebugLog _log;
    private readonly HttpClient _httpClient = new();
    private readonly System.Timers.Timer _pollTimer;
    private bool _hasLoggedHeadersOnce;
    private string? _lastLoggedErrorStatus;

    public event Action<RateLimitSnapshot?>? RateLimitChanged;

    public RateLimitReader(string apiKey, DebugLog log)
    {
        _apiKey = apiKey;
        _log = log;

        _pollTimer = new System.Timers.Timer(5 * 60 * 1000) { AutoReset = true };
        _pollTimer.Elapsed += async (_, _) => await PollAsync();
    }

    public void Start()
    {
        _ = PollAsync();
        _pollTimer.Start();
    }

    private async Task PollAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, CountTokensUrl);
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", AnthropicVersion);
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
                    "RateLimitReader: first response headers (for verifying real anthropic-ratelimit-* names): " +
                    string.Join("; ", headers.Select(kv => $"{kv.Key}={kv.Value}")));
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = ((int)response.StatusCode).ToString();
                if (_lastLoggedErrorStatus != status)
                {
                    _log.Write($"RateLimitReader: non-success HTTP {status} from count_tokens");
                    _lastLoggedErrorStatus = status;
                }
                return;
            }
            _lastLoggedErrorStatus = null;

            var snapshot = RateLimitHeaderParser.Parse(headers);
            if (snapshot is { Percent: null, RemainingTokens: null, LimitTokens: null })
            {
                _log.Write(
                    "RateLimitReader: parsed snapshot has no usable fields; raw headers were: " +
                    string.Join("; ", headers.Select(kv => $"{kv.Key}={kv.Value}")));
            }

            RateLimitChanged?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            _log.Write($"RateLimitReader.PollAsync exception: {ex}");
        }
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
        _httpClient.Dispose();
    }
}

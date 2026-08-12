# Claude Pet — Subscription Usage (Unofficial) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Post-implementation note (whole-branch review, 2026-08-12):** This is a historical record of what was *planned* — it is intentionally left as-written below, including Task 4's `count_tokens` code sample. During execution, Task 4's live verification against a real account showed `count_tokens` does not carry the `anthropic-ratelimit-unified-*` headers this feature needs, so the implementation used the design spec's own pre-approved fallback and substituted a real `POST /v1/messages` call (`max_tokens: 1`) instead. This was a correctly-executed, task-level decision, not a deviation. For the actual, corrected final behavior, see the design spec's second "Revision Note (2026-08-12, second pivot: `count_tokens` → real `messages.create`)" section in `docs/superpowers/specs/2026-08-09-subscription-usage-design.md`, and `src/ClaudePet/Services/SubscriptionUsageReader.cs` for the shipped code.

**Goal:** Add an opt-in tray-tooltip line showing the account's real Pro/Max subscription 5-hour/weekly usage, read from `anthropic-ratelimit-unified-*` response headers on an OAuth-authenticated `count_tokens` call, falling back to the existing API-key header-based `RateLimitReader` line whenever it's disabled, unpolled, or failing.

**Architecture:** A new `SubscriptionUsageReader` (mirrors the shipped `RateLimitReader`) polls `POST /v1/messages/count_tokens` every 5 minutes using the OAuth Bearer token read from `%USERPROFILE%\.claude\.credentials.json` via a new `SubscriptionCredentialReader`, parses the response's `anthropic-ratelimit-unified-5h-*`/`-7d-*` headers via a new pure `SubscriptionUsageParser` into a `SubscriptionUsageSnapshot`, and feeds it to `TrayIconManager` through the same `Dispatcher.BeginInvoke` wiring pattern already used for `UsageReader`/`RateLimitReader`. `TooltipFormatter` picks the subscription snapshot for its second line when present, falling back to the existing `RateLimitSnapshot`-based line otherwise. A new tray-menu checkbox (persisted in `AppSettings`) starts/stops the reader live.

**Tech Stack:** C# / .NET 8 (`net8.0-windows`), WPF (`System.Windows.Application`) + WinForms tray icon (`System.Windows.Forms.NotifyIcon`), `System.Text.Json`, `System.Net.Http.HttpClient`, `System.Timers.Timer`, xUnit 2.5.3 for tests.

## Global Constraints

- Target framework `net8.0-windows`, nullable reference types enabled, implicit usings enabled (matches `src/ClaudePet/ClaudePet.csproj` and `tests/ClaudePet.Tests/ClaudePet.Tests.csproj` — do not add new `<PackageReference>`s, everything needed is already referenced).
- Test framework is xUnit (`[Fact]`, `Assert.*`); no test framework attributes need a `using` (the `<Using Include="Xunit" />` global using is already configured).
- The OAuth access token value must never appear in a `DebugLog.Write(...)` call, anywhere, under any code path — this applies to every task that touches the credential.
- `NotifyIcon.Text` throws if assigned a string longer than `TooltipFormatter.MaxLength` (63 chars) — every formatting change must keep that invariant.
- The feature is opt-in and defaults to disabled (`AppSettings.ShowSubscriptionUsage = false`).
- Credential file path is `%USERPROFILE%\.claude\.credentials.json`, built via `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json")` — same pattern `App.xaml.cs` already uses for `projectsRoot`.
- Base poll interval is 5 minutes; on HTTP-level failure the interval doubles up to a 60-minute ceiling and resets to 5 minutes on the next success. A missing/malformed local credential file does **not** trigger backoff (it's a free local read, not a network call).
- A 401 response clears the in-memory cached OAuth token so the next poll re-reads the credential file.
- `SubscriptionUsageChanged` is only ever raised with a non-null snapshot — there is no "clear the line" signal; a failed poll leaves the tray showing whatever it last showed.
- Full spec: `docs/superpowers/specs/2026-08-09-subscription-usage-design.md` (see the 2026-08-12 revision note — this plan implements the revised, `count_tokens`-header-based version, not the original `/api/oauth/usage` version).

---

### Task 1: `SubscriptionUsageSnapshot` model

**Files:**
- Create: `src/ClaudePet/Models/SubscriptionUsageSnapshot.cs`

**Interfaces:**
- Produces: `record SubscriptionUsageSnapshot(double Percent, string WindowLabel, DateTimeOffset? ResetsAt)` — consumed by Tasks 3, 4, 6, 7.

- [ ] **Step 1: Create the model file**

```csharp
namespace ClaudePet.Models;

public sealed record SubscriptionUsageSnapshot(double Percent, string WindowLabel, DateTimeOffset? ResetsAt);
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build src/ClaudePet/ClaudePet.csproj`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add src/ClaudePet/Models/SubscriptionUsageSnapshot.cs
git commit -m "feat: add SubscriptionUsageSnapshot model"
```

---

### Task 2: `SubscriptionCredentialReader`

**Files:**
- Create: `src/ClaudePet/Services/SubscriptionCredentialReader.cs`
- Test: `tests/ClaudePet.Tests/SubscriptionCredentialReaderTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `SubscriptionCredentialReader(string filePath, Action<string>? onError = null)` with method `(string AccessToken, DateTimeOffset ExpiresAt)? TryRead()` — consumed by Task 4.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudePet.Tests/SubscriptionCredentialReaderTests.cs`:

```csharp
using ClaudePet.Services;

namespace ClaudePet.Tests;

public class SubscriptionCredentialReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ClaudePetCredentialTests_" + Guid.NewGuid());

    public SubscriptionCredentialReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string FilePath => Path.Combine(_dir, "credentials.json");

    private void WriteFile(string json) => File.WriteAllText(FilePath, json);

    [Fact]
    public void TryRead_FileDoesNotExist_ReturnsNullAndReportsMissing()
    {
        string? reportedError = null;
        var reader = new SubscriptionCredentialReader(FilePath, err => reportedError = err);

        var result = reader.TryRead();

        Assert.Null(result);
        Assert.Equal("credential file missing", reportedError);
    }

    [Fact]
    public void TryRead_ValidSchema_ReturnsTokenAndExpiry()
    {
        WriteFile("""{"claudeAiOauth":{"accessToken":"sk-ant-oat01-test","refreshToken":"sk-ant-ort01-test","expiresAt":1748276587173,"scopes":["user:inference"]}}""");
        var reader = new SubscriptionCredentialReader(FilePath);

        var result = reader.TryRead();

        Assert.NotNull(result);
        Assert.Equal("sk-ant-oat01-test", result!.Value.AccessToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1748276587173), result.Value.ExpiresAt);
    }

    [Fact]
    public void TryRead_MalformedJson_ReturnsNullAndReportsError()
    {
        WriteFile("{ not valid json ");
        string? reportedError = null;
        var reader = new SubscriptionCredentialReader(FilePath, err => reportedError = err);

        var result = reader.TryRead();

        Assert.Null(result);
        Assert.Equal("credential file malformed", reportedError);
    }

    [Fact]
    public void TryRead_MissingAccessTokenField_ReturnsNull()
    {
        WriteFile("""{"claudeAiOauth":{"expiresAt":1748276587173}}""");
        var reader = new SubscriptionCredentialReader(FilePath);

        var result = reader.TryRead();

        Assert.Null(result);
    }

    [Fact]
    public void TryRead_MissingExpiresAtField_ReturnsNull()
    {
        WriteFile("""{"claudeAiOauth":{"accessToken":"sk-ant-oat01-test"}}""");
        var reader = new SubscriptionCredentialReader(FilePath);

        var result = reader.TryRead();

        Assert.Null(result);
    }

    [Fact]
    public void TryRead_MissingClaudeAiOauthWrapper_ReturnsNull()
    {
        WriteFile("""{"someOtherField":true}""");
        var reader = new SubscriptionCredentialReader(FilePath);

        var result = reader.TryRead();

        Assert.Null(result);
    }

    [Fact]
    public void TryRead_EmptyAccessToken_ReturnsNull()
    {
        WriteFile("""{"claudeAiOauth":{"accessToken":"","expiresAt":1748276587173}}""");
        var reader = new SubscriptionCredentialReader(FilePath);

        var result = reader.TryRead();

        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~SubscriptionCredentialReaderTests`
Expected: FAIL — `SubscriptionCredentialReader` does not exist yet (compile error).

- [ ] **Step 3: Write the implementation**

Create `src/ClaudePet/Services/SubscriptionCredentialReader.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~SubscriptionCredentialReaderTests`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClaudePet/Services/SubscriptionCredentialReader.cs tests/ClaudePet.Tests/SubscriptionCredentialReaderTests.cs
git commit -m "feat: add SubscriptionCredentialReader for OAuth credential file parsing"
```

---

### Task 3: `SubscriptionUsageParser`

**Files:**
- Create: `src/ClaudePet/Services/SubscriptionUsageParser.cs`
- Test: `tests/ClaudePet.Tests/SubscriptionUsageParserTests.cs`

**Interfaces:**
- Consumes: `SubscriptionUsageSnapshot` (Task 1).
- Produces: `static SubscriptionUsageSnapshot? SubscriptionUsageParser.Parse(IReadOnlyDictionary<string, string> headers)` — consumed by Task 4.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudePet.Tests/SubscriptionUsageParserTests.cs`:

```csharp
using ClaudePet.Services;

namespace ClaudePet.Tests;

public class SubscriptionUsageParserTests
{
    private static Dictionary<string, string> Headers(params (string Key, string Value)[] pairs) =>
        new(pairs.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)),
            StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Parse_NoHeadersPresent_ReturnsNull()
    {
        var result = SubscriptionUsageParser.Parse(Headers());

        Assert.Null(result);
    }

    [Fact]
    public void Parse_OnlyFiveHourPresent_UsesFiveHour()
    {
        var headers = Headers(
            ("anthropic-ratelimit-unified-5h-utilization", "0.33"),
            ("anthropic-ratelimit-unified-5h-reset", "1774933200"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal(33.0, result!.Percent, precision: 3);
        Assert.Equal("5h", result.WindowLabel);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1774933200), result.ResetsAt);
    }

    [Fact]
    public void Parse_OnlySevenDayPresent_UsesSevenDay()
    {
        var headers = Headers(("anthropic-ratelimit-unified-7d-utilization", "0.13"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal(13.0, result!.Percent, precision: 3);
        Assert.Equal("7d", result.WindowLabel);
        Assert.Null(result.ResetsAt);
    }

    [Fact]
    public void Parse_BothPresentSevenDayHigher_PicksSevenDay()
    {
        var headers = Headers(
            ("anthropic-ratelimit-unified-5h-utilization", "0.33"),
            ("anthropic-ratelimit-unified-7d-utilization", "0.53"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal("7d", result!.WindowLabel);
        Assert.Equal(53.0, result.Percent, precision: 3);
    }

    [Fact]
    public void Parse_BothPresentFiveHourHigher_PicksFiveHour()
    {
        var headers = Headers(
            ("anthropic-ratelimit-unified-5h-utilization", "0.82"),
            ("anthropic-ratelimit-unified-7d-utilization", "0.20"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal("5h", result!.WindowLabel);
        Assert.Equal(82.0, result.Percent, precision: 3);
    }

    [Fact]
    public void Parse_ExactTie_PrefersFiveHour()
    {
        var headers = Headers(
            ("anthropic-ratelimit-unified-5h-utilization", "0.50"),
            ("anthropic-ratelimit-unified-7d-utilization", "0.50"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal("5h", result!.WindowLabel);
    }

    [Fact]
    public void Parse_NonNumericUtilization_TreatedAsWindowAbsent()
    {
        var headers = Headers(("anthropic-ratelimit-unified-5h-utilization", "not-a-number"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_UtilizationAboveOne_ClampsToOneHundredPercent()
    {
        // Defensive clamp: tolerate an unexpected already-percent-scale value
        // rather than producing an invalid (e.g. 8200%) tooltip line.
        var headers = Headers(("anthropic-ratelimit-unified-5h-utilization", "82"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal(100.0, result!.Percent);
    }

    [Fact]
    public void Parse_NonNumericReset_LeavesResetsAtNull()
    {
        var headers = Headers(
            ("anthropic-ratelimit-unified-5h-utilization", "0.10"),
            ("anthropic-ratelimit-unified-5h-reset", "not-a-timestamp"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Null(result!.ResetsAt);
    }

    [Fact]
    public void Parse_MixedCaseHeaderKeys_StillResolves()
    {
        var headers = Headers(("ANTHROPIC-RATELIMIT-UNIFIED-5H-UTILIZATION", "0.45"));

        var result = SubscriptionUsageParser.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal(45.0, result!.Percent, precision: 3);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~SubscriptionUsageParserTests`
Expected: FAIL — `SubscriptionUsageParser` does not exist yet (compile error).

- [ ] **Step 3: Write the implementation**

Create `src/ClaudePet/Services/SubscriptionUsageParser.cs`:

```csharp
using ClaudePet.Models;

namespace ClaudePet.Services;

public static class SubscriptionUsageParser
{
    // Header names are not confirmed against a live OAuth-authenticated
    // response (reconstructed from third-party reverse-engineering - see
    // the design spec's Open Assumptions). SubscriptionUsageReader (Task 4)
    // logs the full raw header set on first response so this can be
    // verified/corrected against a real logged-in session.
    private const string FiveHourUtilizationHeader = "anthropic-ratelimit-unified-5h-utilization";
    private const string FiveHourResetHeader = "anthropic-ratelimit-unified-5h-reset";
    private const string SevenDayUtilizationHeader = "anthropic-ratelimit-unified-7d-utilization";
    private const string SevenDayResetHeader = "anthropic-ratelimit-unified-7d-reset";

    public static SubscriptionUsageSnapshot? Parse(IReadOnlyDictionary<string, string> headers)
    {
        var fiveHour = ParseWindow(headers, FiveHourUtilizationHeader, FiveHourResetHeader);
        var sevenDay = ParseWindow(headers, SevenDayUtilizationHeader, SevenDayResetHeader);

        if (fiveHour is null && sevenDay is null)
            return null;
        if (fiveHour is null)
            return new SubscriptionUsageSnapshot(sevenDay!.Value.Percent, "7d", sevenDay.Value.ResetsAt);
        if (sevenDay is null)
            return new SubscriptionUsageSnapshot(fiveHour.Value.Percent, "5h", fiveHour.Value.ResetsAt);

        // Whichever window is more constrained (higher utilization) is the
        // more actionable single number for the one-line tooltip budget. On
        // an exact tie, prefer the 5-hour window - it resets sooner and is
        // what Claude Code's own status line surfaces first.
        return fiveHour.Value.Percent >= sevenDay.Value.Percent
            ? new SubscriptionUsageSnapshot(fiveHour.Value.Percent, "5h", fiveHour.Value.ResetsAt)
            : new SubscriptionUsageSnapshot(sevenDay.Value.Percent, "7d", sevenDay.Value.ResetsAt);
    }

    private static (double Percent, DateTimeOffset? ResetsAt)? ParseWindow(
        IReadOnlyDictionary<string, string> headers, string utilizationHeader, string resetHeader)
    {
        if (!headers.TryGetValue(utilizationHeader, out var utilizationValue) ||
            !double.TryParse(
                utilizationValue,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var utilization))
        {
            return null;
        }

        var percent = Math.Clamp(utilization * 100.0, 0, 100);

        DateTimeOffset? resetsAt = null;
        if (headers.TryGetValue(resetHeader, out var resetValue) && long.TryParse(resetValue, out var resetSeconds))
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(resetSeconds);

        return (percent, resetsAt);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~SubscriptionUsageParserTests`
Expected: PASS (10 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClaudePet/Services/SubscriptionUsageParser.cs tests/ClaudePet.Tests/SubscriptionUsageParserTests.cs
git commit -m "feat: add SubscriptionUsageParser for anthropic-ratelimit-unified-* headers"
```

---

### Task 4: `SubscriptionUsageReader`

**Files:**
- Create: `src/ClaudePet/Services/SubscriptionUsageReader.cs`

**Interfaces:**
- Consumes: `SubscriptionCredentialReader` (Task 2), `SubscriptionUsageParser` (Task 3), `SubscriptionUsageSnapshot` (Task 1), `ClaudePet.Logging.DebugLog` (existing).
- Produces: `SubscriptionUsageReader(string credentialFilePath, DebugLog log) : IDisposable`, `void Start()`, `void Stop()`, `event Action<SubscriptionUsageSnapshot>? SubscriptionUsageChanged` — consumed by Task 8.

No automated tests for this task — like the sibling `RateLimitReader`, it's a thin HTTP-call wrapper with no dedicated test file; correctness is verified manually against a live account in Step 3 below (this is also where the header-name assumption from Task 3 gets confirmed).

- [ ] **Step 1: Write the implementation**

Create `src/ClaudePet/Services/SubscriptionUsageReader.cs`:

```csharp
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
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build src/ClaudePet/ClaudePet.csproj`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Manually verify against a real logged-in session**

This is the point where the header-name assumption from Task 3 gets confirmed or corrected — do not skip it before considering the feature done.

1. Confirm `%USERPROFILE%\.claude\.credentials.json` exists (i.e. you're logged into Claude Code on this machine: `claude /login` if not).
2. Temporarily wire `SubscriptionUsageReader` into a throwaway console test, or wait until Task 8 wires it into the real app and enable the tray checkbox.
3. After one successful poll, open `%LOCALAPPDATA%\ClaudePet\debug.log` and find the line starting `SubscriptionUsageReader: first response headers`.
4. Confirm it contains `anthropic-ratelimit-unified-5h-utilization` and/or `anthropic-ratelimit-unified-7d-utilization` (case-insensitive). If the real header names differ, update the four `*Header` constants at the top of `SubscriptionUsageParser` (Task 3) to match, add a regression test for the corrected name, and re-run the Task 3 test suite.
5. Confirm no line in `debug.log` contains anything resembling `sk-ant-oat01-` (the access token prefix) — if it does, that's a bug in this task's logging, not acceptable to ship.

- [ ] **Step 4: Commit**

```bash
git add src/ClaudePet/Services/SubscriptionUsageReader.cs
git commit -m "feat: poll OAuth-authenticated count_tokens for subscription usage headers"
```

---

### Task 5: `AppSettings.ShowSubscriptionUsage`

**Files:**
- Modify: `src/ClaudePet/Settings/AppSettings.cs`
- Modify: `tests/ClaudePet.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `AppSettings.ShowSubscriptionUsage` (bool, default `false`) — consumed by Tasks 7 and 8.

- [ ] **Step 1: Write the failing tests**

In `tests/ClaudePet.Tests/SettingsStoreTests.cs`, update `Load_FileDoesNotExist_ReturnsDefaults` to also assert the new field's default, and add a dedicated round-trip test:

```csharp
    [Fact]
    public void Load_FileDoesNotExist_ReturnsDefaults()
    {
        var store = new SettingsStore(FilePath);

        var settings = store.Load();

        Assert.Null(settings.WindowLeft);
        Assert.Null(settings.WindowTop);
        Assert.False(settings.RunAtStartup);
        Assert.False(settings.ShowSubscriptionUsage);
    }
```

Add this new test anywhere after `SaveThenLoad_RoundTripsValues`:

```csharp
    [Fact]
    public void SaveThenLoad_RoundTripsShowSubscriptionUsage()
    {
        var store = new SettingsStore(FilePath);
        var original = new AppSettings { ShowSubscriptionUsage = true };

        store.Save(original);
        var loaded = store.Load();

        Assert.True(loaded.ShowSubscriptionUsage);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~SettingsStoreTests`
Expected: FAIL — `AppSettings` has no `ShowSubscriptionUsage` member (compile error).

- [ ] **Step 3: Add the field**

In `src/ClaudePet/Settings/AppSettings.cs`, add after `RunAtStartup`:

```csharp
    // Opt-in: polls Anthropic's undocumented anthropic-ratelimit-unified-*
    // headers via the user's Claude Code OAuth credential. See
    // docs/superpowers/specs/2026-08-09-subscription-usage-design.md.
    public bool ShowSubscriptionUsage { get; init; }
```

Full resulting file:

```csharp
namespace ClaudePet.Settings;

public sealed record AppSettings
{
    // double? (not a -1 sentinel): a saved position is legitimately negative on
    // multi-monitor setups where a monitor sits left of/above the primary one, so
    // "-1 means unset" can't be distinguished from "-1 is a real coordinate". null
    // unambiguously means "no saved position yet".
    public double? WindowLeft { get; init; }
    public double? WindowTop { get; init; }
    public bool RunAtStartup { get; init; }

    // Opt-in: polls Anthropic's undocumented anthropic-ratelimit-unified-*
    // headers via the user's Claude Code OAuth credential. See
    // docs/superpowers/specs/2026-08-09-subscription-usage-design.md.
    public bool ShowSubscriptionUsage { get; init; }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~SettingsStoreTests`
Expected: PASS (all `SettingsStoreTests`, including the 2 touched by this task).

- [ ] **Step 5: Commit**

```bash
git add src/ClaudePet/Settings/AppSettings.cs tests/ClaudePet.Tests/SettingsStoreTests.cs
git commit -m "feat: add ShowSubscriptionUsage setting"
```

---

### Task 6: `TooltipFormatter` — subscription line with fallback

**Files:**
- Modify: `src/ClaudePet/Tray/TooltipFormatter.cs`
- Modify: `tests/ClaudePet.Tests/TooltipFormatterTests.cs`

**Interfaces:**
- Consumes: `SubscriptionUsageSnapshot` (Task 1).
- Produces: `TooltipFormatter.Format(UsageSnapshot? usage, RateLimitSnapshot? rateLimit, SubscriptionUsageSnapshot? subscriptionUsage = null, DateTimeOffset? now = null)` — consumed by Task 7. **Breaking signature change:** `subscriptionUsage` is inserted before the existing `now` parameter, so every existing 3-positional-argument call site (`Format(usage, rateLimit, now)`) must become a named argument (`Format(usage, rateLimit, now: now)`) or it will fail to compile (`now`, a `DateTimeOffset`, can no longer bind positionally to the `SubscriptionUsageSnapshot?` slot).

- [ ] **Step 1: Replace the full test file with the updated version (existing tests fixed + new cases)**

Replace the full contents of `tests/ClaudePet.Tests/TooltipFormatterTests.cs`:

```csharp
using ClaudePet.Models;
using ClaudePet.Tray;

namespace ClaudePet.Tests;

public class TooltipFormatterTests
{
    [Fact]
    public void Format_NullUsageNullRateLimit_ReturnsNoActiveSessionLine()
    {
        var result = TooltipFormatter.Format(null, null);

        Assert.Equal("Claude Pet: no active session", result);
    }

    [Fact]
    public void Format_UsageOnly_MatchesOriginalSingleLineFormat()
    {
        var usage = new UsageSnapshot(443155, 1000000, 44.3);

        var result = TooltipFormatter.Format(usage, null);

        Assert.Equal("Claude Pet: 44% (443,155/1,000,000)", result);
    }

    [Fact]
    public void Format_UsageAndRateLimitWithReset_ProducesTwoLines()
    {
        var now = DateTimeOffset.UtcNow;
        var usage = new UsageSnapshot(443155, 1000000, 44.3);
        var rateLimit = new RateLimitSnapshot(880000, 1000000, 12.0, now.AddHours(3).AddMinutes(20));

        var result = TooltipFormatter.Format(usage, rateLimit, now: now);

        Assert.Equal("Claude Pet: 44% (443,155/1,000,000)\nRate limit: 12% used, 3h", result);
        Assert.True(result.Length <= TooltipFormatter.MaxLength);
    }

    [Fact]
    public void Format_RateLimitWithoutResetsAt_OmitsResetClause()
    {
        var rateLimit = new RateLimitSnapshot(500, 1000, 50.0, null);

        var result = TooltipFormatter.Format(null, rateLimit);

        Assert.Equal("Claude Pet: no active session\nRate limit: 50% used", result);
    }

    [Fact]
    public void Format_RateLimitWithNullPercent_OmitsSecondLineEntirely()
    {
        var rateLimit = new RateLimitSnapshot(null, null, null, null);

        var result = TooltipFormatter.Format(null, rateLimit);

        Assert.Equal("Claude Pet: no active session", result);
        Assert.DoesNotContain("Rate limit", result);
    }

    [Fact]
    public void Format_CombinedTextExceedsLimit_TruncatesRateLimitLineFirst()
    {
        // With the compact reset format, a normal-scale usage line (e.g.
        // 999,999/1,000,000) leaves enough budget for "Rate limit: NN%
        // used, Xh" to fit without truncation. Use int.MaxValue-scale
        // token counts (like the extreme-values test) to force a long
        // enough line 1 that line 2 must still be truncated.
        var now = DateTimeOffset.UtcNow;
        var usage = new UsageSnapshot(int.MaxValue, int.MaxValue, 100.0);
        var rateLimit = new RateLimitSnapshot(1, 1000000, 99.9999, now.AddHours(23).AddMinutes(59));

        var result = TooltipFormatter.Format(usage, rateLimit, now: now);

        Assert.True(result.Length <= TooltipFormatter.MaxLength);
        Assert.StartsWith("Claude Pet: 100% (2,147,483,647/2,147,483,647)\n", result);
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void Format_ResultNeverExceedsMaxLength_EvenWithExtremeValues()
    {
        var usage = new UsageSnapshot(int.MaxValue, int.MaxValue, 100.0);
        var rateLimit = new RateLimitSnapshot(0, int.MaxValue, 100.0, DateTimeOffset.UtcNow.AddDays(400));

        var result = TooltipFormatter.Format(usage, rateLimit);

        Assert.True(result.Length <= TooltipFormatter.MaxLength);
    }

    [Fact]
    public void Format_ResetsAtInPast_ShowsSoon()
    {
        var now = DateTimeOffset.UtcNow;
        var rateLimit = new RateLimitSnapshot(0, 1000, 50.0, now.AddMinutes(-5));

        var result = TooltipFormatter.Format(null, rateLimit, now: now);

        Assert.Equal("Claude Pet: no active session\nRate limit: 50% used, soon", result);
    }

    [Fact]
    public void Format_ResetsAtUnderOneHour_ShowsMinutes()
    {
        var now = DateTimeOffset.UtcNow;
        var rateLimit = new RateLimitSnapshot(0, 1000, 50.0, now.AddMinutes(45));

        var result = TooltipFormatter.Format(null, rateLimit, now: now);

        Assert.Equal("Claude Pet: no active session\nRate limit: 50% used, 45m", result);
    }

    [Fact]
    public void Format_ResetsAtUnderOneMinute_FloorsToOneMinute()
    {
        var now = DateTimeOffset.UtcNow;
        var rateLimit = new RateLimitSnapshot(0, 1000, 50.0, now.AddSeconds(20));

        var result = TooltipFormatter.Format(null, rateLimit, now: now);

        Assert.Equal("Claude Pet: no active session\nRate limit: 50% used, 1m", result);
    }

    [Fact]
    public void Format_ResetsAtWithinADay_ShowsHours()
    {
        var now = DateTimeOffset.UtcNow;
        var rateLimit = new RateLimitSnapshot(0, 1000, 50.0, now.AddHours(5).AddMinutes(30));

        var result = TooltipFormatter.Format(null, rateLimit, now: now);

        Assert.Equal("Claude Pet: no active session\nRate limit: 50% used, 5h", result);
    }

    [Fact]
    public void Format_ResetsAtBeyondADay_ShowsDays()
    {
        var now = DateTimeOffset.UtcNow;
        var rateLimit = new RateLimitSnapshot(0, 1000, 50.0, now.AddDays(2).AddHours(3));

        var result = TooltipFormatter.Format(null, rateLimit, now: now);

        Assert.Equal("Claude Pet: no active session\nRate limit: 50% used, 2d", result);
    }

    [Fact]
    public void Format_SubscriptionUsagePresent_TakesPriorityOverRateLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var rateLimit = new RateLimitSnapshot(500, 1000, 50.0, now.AddHours(1));
        var subscriptionUsage = new SubscriptionUsageSnapshot(82.0, "5h", now.AddHours(2).AddMinutes(30));

        var result = TooltipFormatter.Format(null, rateLimit, subscriptionUsage, now);

        Assert.Equal("Claude Pet: no active session\nSub: 82% (5h), 2h", result);
        Assert.DoesNotContain("Rate limit", result);
    }

    [Fact]
    public void Format_SubscriptionUsageNull_FallsBackToRateLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var rateLimit = new RateLimitSnapshot(500, 1000, 50.0, now.AddHours(1));

        var result = TooltipFormatter.Format(null, rateLimit, subscriptionUsage: null, now: now);

        Assert.Equal("Claude Pet: no active session\nRate limit: 50% used, 1h", result);
    }

    [Fact]
    public void Format_SubscriptionUsageWithoutResetsAt_OmitsResetClause()
    {
        var subscriptionUsage = new SubscriptionUsageSnapshot(13.0, "7d", null);

        var result = TooltipFormatter.Format(null, null, subscriptionUsage);

        Assert.Equal("Claude Pet: no active session\nSub: 13% (7d)", result);
    }

    [Fact]
    public void Format_SubscriptionLineCombinedExceedsLimit_TruncatesSubscriptionLine()
    {
        var now = DateTimeOffset.UtcNow;
        var usage = new UsageSnapshot(int.MaxValue, int.MaxValue, 100.0);
        var subscriptionUsage = new SubscriptionUsageSnapshot(99.9999, "5h", now.AddHours(23).AddMinutes(59));

        var result = TooltipFormatter.Format(usage, null, subscriptionUsage, now);

        Assert.True(result.Length <= TooltipFormatter.MaxLength);
        Assert.StartsWith("Claude Pet: 100% (2,147,483,647/2,147,483,647)\n", result);
        Assert.EndsWith("…", result);
    }
}
```

- [ ] **Step 2: Run tests to verify the new ones fail**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~TooltipFormatterTests`
Expected: FAIL — `Format` has no 3- or 4-argument overload accepting `SubscriptionUsageSnapshot?` yet (compile error).

- [ ] **Step 3: Update the implementation**

Replace the full contents of `src/ClaudePet/Tray/TooltipFormatter.cs`:

```csharp
using ClaudePet.Models;

namespace ClaudePet.Tray;

public static class TooltipFormatter
{
    // NotifyIcon.Text throws if assigned a string longer than this.
    public const int MaxLength = 63;

    public static string Format(
        UsageSnapshot? usage,
        RateLimitSnapshot? rateLimit,
        SubscriptionUsageSnapshot? subscriptionUsage = null,
        DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.UtcNow;
        var line1 = FormatUsageLine(usage);
        // Subscription usage (opt-in, real account data) takes priority over
        // the API-key header-based rate-limit line when present; falls back
        // to the header-based line when subscription data isn't available
        // (feature disabled, not yet polled, or currently failing).
        var line2 = FormatSubscriptionLine(subscriptionUsage, reference) ?? FormatRateLimitLine(rateLimit, reference);

        if (line2 is null)
            return Truncate(line1, MaxLength);

        var combined = $"{line1}\n{line2}";
        if (combined.Length <= MaxLength)
            return combined;

        // Truncate the second line first: the session-usage line is the
        // established primary signal.
        var budget = MaxLength - line1.Length - 1; // -1 for the newline
        if (budget <= 0)
            return Truncate(line1, MaxLength);

        return $"{line1}\n{Truncate(line2, budget)}";
    }

    private static string FormatUsageLine(UsageSnapshot? usage) =>
        usage is null
            ? "Claude Pet: no active session"
            : $"Claude Pet: {usage.Percent:F0}% ({usage.ContextTokens:N0}/{usage.ContextLimit:N0})";

    private static string? FormatSubscriptionLine(SubscriptionUsageSnapshot? subscriptionUsage, DateTimeOffset now)
    {
        if (subscriptionUsage is null)
            return null;

        var resetPart = subscriptionUsage.ResetsAt is { } resetsAt
            ? $", {FormatRelative(resetsAt, now)}"
            : "";
        return $"Sub: {subscriptionUsage.Percent:F0}% ({subscriptionUsage.WindowLabel}){resetPart}";
    }

    private static string? FormatRateLimitLine(RateLimitSnapshot? rateLimit, DateTimeOffset now)
    {
        if (rateLimit is null || rateLimit.Percent is null)
            return null;

        var resetPart = rateLimit.ResetsAt is { } resetsAt
            ? $", {FormatRelative(resetsAt, now)}"
            : "";
        return $"Rate limit: {rateLimit.Percent:F0}% used{resetPart}";
    }

    // Deliberately compact (single unit, no combined "Xh Ym"): the 63-char
    // NotifyIcon.Text budget leaves very little room for this clause once a
    // real session-usage line is present (see whole-branch review finding
    // #1 for the arithmetic). A reset window can span up to ~7 days, so
    // days/hours/minutes are each shown alone, never combined.
    private static string FormatRelative(DateTimeOffset resetsAt, DateTimeOffset now)
    {
        var delta = resetsAt - now;
        if (delta <= TimeSpan.Zero)
            return "soon";
        if (delta.TotalHours >= 24)
            return $"{(int)delta.TotalDays}d";
        if (delta.TotalHours >= 1)
            return $"{(int)delta.TotalHours}h";
        return $"{Math.Max(1, delta.Minutes)}m";
    }

    // Ellipsis instead of a blind character cut: a bare cut can leave a
    // fragment that reads as a real (wrong) value, e.g. a truncated number.
    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;
        if (maxLength <= 1)
            return text[..maxLength];
        return text[..(maxLength - 1)] + "…";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~TooltipFormatterTests`
Expected: PASS (all 17 tests).

- [ ] **Step 5: Run the full test suite to confirm nothing else broke**

Run: `dotnet test tests/ClaudePet.Tests`
Expected: PASS — no other test file calls `TooltipFormatter.Format` positionally with 3+ arguments.

- [ ] **Step 6: Commit**

```bash
git add src/ClaudePet/Tray/TooltipFormatter.cs tests/ClaudePet.Tests/TooltipFormatterTests.cs
git commit -m "feat: TooltipFormatter picks subscription usage line over rate-limit line"
```

---

### Task 7: `TrayIconManager` — opt-in checkbox and wiring

**Files:**
- Modify: `src/ClaudePet/Tray/TrayIconManager.cs`

**Interfaces:**
- Consumes: `SubscriptionUsageSnapshot` (Task 1), `AppSettings.ShowSubscriptionUsage` (Task 5), `TooltipFormatter.Format(...)` (Task 6).
- Produces: `TrayIconManager.UpdateSubscriptionUsage(SubscriptionUsageSnapshot? snapshot)`, `event Action<bool>? SubscriptionUsageToggled` — both consumed by Task 8.

No dedicated unit tests for this task — `TrayIconManager` has no existing test file (it's a thin `System.Windows.Forms.NotifyIcon`/`ContextMenuStrip` wrapper with no headless test harness in this codebase), consistent with how `RateLimitReader`'s wiring into this same class wasn't separately unit tested beyond `TooltipFormatter`. Verified by build + Task 8's manual smoke test.

- [ ] **Step 1: Update the implementation**

Replace the full contents of `src/ClaudePet/Tray/TrayIconManager.cs`:

```csharp
using System.Security;
using System.Windows;
using System.Windows.Forms;
using ClaudePet.Logging;
using ClaudePet.Models;
using ClaudePet.Native;
using ClaudePet.Settings;

namespace ClaudePet.Tray;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly PetWindow _petWindow;
    private readonly SettingsStore _settingsStore;
    private readonly DebugLog _log;
    private readonly ToolStripMenuItem _dragItem;
    private bool _dragMode;
    private UsageSnapshot? _lastUsage;
    private RateLimitSnapshot? _lastRateLimit;
    private SubscriptionUsageSnapshot? _lastSubscriptionUsage;

    public event Action<bool>? SubscriptionUsageToggled;

    public TrayIconManager(PetWindow petWindow, SettingsStore settingsStore, DebugLog log)
    {
        _petWindow = petWindow;
        _settingsStore = settingsStore;
        _log = log;

        _dragItem = new ToolStripMenuItem("Enable dragging", null, ToggleDragMode);

        var runAtStartupItem = new ToolStripMenuItem("Run at startup", null, ToggleRunAtStartup)
        {
            Checked = _settingsStore.Load().RunAtStartup
        };

        var subscriptionUsageItem = new ToolStripMenuItem("Show subscription usage (unofficial)", null, ToggleSubscriptionUsage)
        {
            Checked = _settingsStore.Load().ShowSubscriptionUsage
        };

        var quitItem = new ToolStripMenuItem("Quit", null, (_, _) => System.Windows.Application.Current.Shutdown());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_dragItem);
        menu.Items.Add(runAtStartupItem);
        menu.Items.Add(subscriptionUsageItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Claude Pet",
            ContextMenuStrip = menu
        };
    }

    public void UpdateUsage(UsageSnapshot? snapshot)
    {
        _lastUsage = snapshot;
        RefreshTooltip();
    }

    public void UpdateRateLimit(RateLimitSnapshot? snapshot)
    {
        _lastRateLimit = snapshot;
        RefreshTooltip();
    }

    public void UpdateSubscriptionUsage(SubscriptionUsageSnapshot? snapshot)
    {
        _lastSubscriptionUsage = snapshot;
        RefreshTooltip();
    }

    private void RefreshTooltip()
    {
        _notifyIcon.Text = TooltipFormatter.Format(_lastUsage, _lastRateLimit, _lastSubscriptionUsage);
    }

    private void ToggleDragMode(object? sender, EventArgs e)
    {
        _dragMode = !_dragMode;
        _dragItem.Checked = _dragMode;
        _petWindow.SetDragMode(_dragMode);
    }

    private void ToggleRunAtStartup(object? sender, EventArgs e)
    {
        var item = (ToolStripMenuItem)sender!;
        var newValue = !item.Checked;

        // StartupRegistration.SetEnabled touches the registry and can throw
        // (SecurityException / UnauthorizedAccessException) if the process lacks
        // registry access. Since this runs directly on a tray-menu click with no
        // global dispatcher-exception handler wired up yet, an uncaught throw here
        // would take down the whole app. Only flip the checkbox and persist the
        // setting once the registry write actually succeeds, so on failure the
        // menu state and saved settings stay consistent with reality.
        try
        {
            StartupRegistration.SetEnabled(newValue);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            _log.Write(
                $"[TrayIconManager.ToggleRunAtStartup] Failed to set startup registration to {newValue}: {ex}");
            return;
        }

        item.Checked = newValue;
        var settings = _settingsStore.Load() with { RunAtStartup = newValue };
        _settingsStore.Save(settings);
    }

    private void ToggleSubscriptionUsage(object? sender, EventArgs e)
    {
        var item = (ToolStripMenuItem)sender!;
        var newValue = !item.Checked;

        item.Checked = newValue;
        var settings = _settingsStore.Load() with { ShowSubscriptionUsage = newValue };
        _settingsStore.Save(settings);

        SubscriptionUsageToggled?.Invoke(newValue);
    }

    public void Dispose()
    {
        _notifyIcon.Dispose();
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build src/ClaudePet/ClaudePet.csproj`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Run the full test suite to confirm nothing broke**

Run: `dotnet test tests/ClaudePet.Tests`
Expected: PASS — this file has no dedicated tests, so this just confirms the change didn't break anything else that transitively depends on this assembly.

- [ ] **Step 4: Commit**

```bash
git add src/ClaudePet/Tray/TrayIconManager.cs
git commit -m "feat: add subscription-usage tray toggle"
```

---

### Task 8: Wire `SubscriptionUsageReader` into app startup

**Files:**
- Modify: `src/ClaudePet/App.xaml.cs`

**Interfaces:**
- Consumes: `SubscriptionUsageReader` (Task 4), `TrayIconManager.SubscriptionUsageToggled`/`.UpdateSubscriptionUsage(...)` (Task 7), `AppSettings.ShowSubscriptionUsage` (Task 5).
- Produces: nothing new for later tasks — this is the final integration point.

No dedicated unit tests — `App.xaml.cs` (`OnStartup`/`OnExit`) has no existing test file either. Verified by build + a manual end-to-end smoke test in Step 3.

- [ ] **Step 1: Add the new field**

In `src/ClaudePet/App.xaml.cs`, add next to the existing `_rateLimitReader` field declaration:

```csharp
    private RateLimitReader? _rateLimitReader;
    private SubscriptionUsageReader? _subscriptionUsageReader;
```

- [ ] **Step 2: Wire construction, event handling, and the toggle**

In `src/ClaudePet/App.xaml.cs`, immediately after the existing block that constructs and starts `_rateLimitReader` (the `if (!string.IsNullOrWhiteSpace(apiKey)) { ... }` block, still inside the outer `try`), insert:

```csharp
            var credentialFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", ".credentials.json");

            _subscriptionUsageReader = new SubscriptionUsageReader(credentialFilePath, log);
            _subscriptionUsageReader.SubscriptionUsageChanged += snapshot =>
            {
                // Same reasoning as the UsageChanged/RateLimitChanged handlers above:
                // BeginInvoke (never a blocking Invoke) and a
                // HasShutdownStarted/HasShutdownFinished guard, since this fires from
                // a background timer thread that can race application shutdown.
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                    return;

                try
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        _trayIconManager.UpdateSubscriptionUsage(snapshot);
                    });
                }
                catch (Exception ex) when (ex is TaskCanceledException or InvalidOperationException)
                {
                    log.Write($"SubscriptionUsageChanged handler: dispatcher unavailable, dropping update: {ex.Message}");
                }
            };

            _trayIconManager.SubscriptionUsageToggled += enabled =>
            {
                if (enabled)
                    _subscriptionUsageReader.Start();
                else
                    _subscriptionUsageReader.Stop();
            };

            if (settingsStore.Load().ShowSubscriptionUsage)
                _subscriptionUsageReader.Start();
```

This must be placed after `_trayIconManager` is constructed (it already is, earlier in `OnStartup`) and after the `settingsStore` local variable is in scope (also already true).

- [ ] **Step 3: Dispose it on exit**

In `src/ClaudePet/App.xaml.cs`, in `OnExit`, add next to the existing `_rateLimitReader?.Dispose();` line:

```csharp
    protected override void OnExit(ExitEventArgs e)
    {
        _usageReader?.Dispose();
        _rateLimitReader?.Dispose();
        _subscriptionUsageReader?.Dispose();
        _trayIconManager?.Dispose();
```

- [ ] **Step 4: Build to confirm it compiles**

Run: `dotnet build src/ClaudePet/ClaudePet.csproj`
Expected: Build succeeds with no errors.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test tests/ClaudePet.Tests`
Expected: PASS — all tests from Tasks 2, 3, 5, 6, plus everything pre-existing.

- [ ] **Step 6: Manual end-to-end smoke test**

1. Run the app: `dotnet run --project src/ClaudePet/ClaudePet.csproj`.
2. Right-click the tray icon; confirm "Show subscription usage (unofficial)" appears in the menu, unchecked.
3. Click it to enable. Confirm the checkbox is now checked.
4. Wait for the first poll (near-immediate — `Start()` fires one via `Task.Run` right away) and hover the tray icon; confirm the tooltip's second line either shows `Sub: NN% (5h)` / `Sub: NN% (7d)`, or — if this machine's `~/.claude/.credentials.json` is missing/expired/the headers don't match — falls back to the existing `Rate limit:` line (or no second line at all) with no crash.
5. Check `%LOCALAPPDATA%\ClaudePet\debug.log` for the one-time `SubscriptionUsageReader: first response headers` line and confirm no access-token value appears anywhere in the file.
6. Uncheck the checkbox; confirm the tray icon keeps running normally (no crash) and the tooltip's second line reverts to whatever `RateLimitReader` was already showing (or disappears, if that's also unset).
7. Quit the app via the tray menu; confirm it exits cleanly (no hung process, no exception dialog).

- [ ] **Step 7: Commit**

```bash
git add src/ClaudePet/App.xaml.cs
git commit -m "feat: wire subscription-usage reader into app startup and tray toggle"
```

---

## Self-Review Notes

- **Spec coverage:** every component in the design spec's Components section (1–8) maps 1:1 to a task here (Task 1↔§3, Task 2↔§1, Task 3↔§2, Task 4↔§4, Task 5↔§5, Task 6↔§7, Task 7↔§6, Task 8↔§8). Error-handling behaviors (credential-miss no-backoff, HTTP backoff/ceiling, 401 cache-clear, header-mismatch fallback, never-log-token) are all implemented in Task 4 and exercised manually in Task 4 Step 3 and Task 8 Step 6 (no automated coverage for the reader itself, matching the shipped `RateLimitReader` precedent).
- **Type consistency checked:** `SubscriptionUsageSnapshot(double Percent, string WindowLabel, DateTimeOffset? ResetsAt)` is used identically in Tasks 1, 3, 4, 6, 7. `SubscriptionCredentialReader(string filePath, Action<string>? onError = null)` / `TryRead()` signature matches between Task 2's definition and Task 4's construction call. `SubscriptionUsageReader(string credentialFilePath, DebugLog log)` matches between Task 4's definition and Task 8's construction call. `TooltipFormatter.Format`'s new 4-parameter signature is used consistently (named `now:`/positional `subscriptionUsage`) across every call site touched in Task 6.
- **No placeholders:** every step has complete, runnable code — no "implement per spec" or "similar to Task N" shortcuts.

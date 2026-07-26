# Rate-Limit Usage Indicator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a second, optional tray-tooltip line to Claude Pet showing the Anthropic API key's rolling rate-limit usage (percent used, reset time), alongside the existing session-context-window percentage.

**Architecture:** A new `RateLimitReader` polls Anthropic's `count_tokens` endpoint every 5 minutes via a plain `HttpClient` (no full SDK dependency needed for one lightweight call), parses `anthropic-ratelimit-*` response headers into a `RateLimitSnapshot` via a separate pure/testable `RateLimitHeaderParser`. The tray tooltip's text-building logic is extracted from `TrayIconManager` into a new pure/testable `TooltipFormatter` that combines the existing `UsageSnapshot?` and the new `RateLimitSnapshot?` into one string, respecting the 63-character `NotifyIcon.Text` limit. The feature is fully optional: if `ANTHROPIC_API_KEY` is unset, `RateLimitReader` is never constructed.

**Tech Stack:** .NET 8, WPF (`net8.0-windows`), `System.Net.Http.HttpClient`, xUnit.

## Global Constraints

- `ANTHROPIC_API_KEY` unset → feature silently absent, no error, no crash, no configuration UI (per spec's Goals).
- Poll via `POST https://api.anthropic.com/v1/messages/count_tokens` (no generation, no output-token billing) every 5 minutes — not `messages.create`.
- The exact `anthropic-ratelimit-*` header names returned by this endpoint are **not confirmed** — no `ANTHROPIC_API_KEY` was available during design or planning to verify live. Code must be written defensively: try multiple plausible header name variants, log the full raw header set to `DebugLog` on the first response received (success or failure) so the user can verify/correct against reality using their own key, and degrade to `null` fields rather than throwing when headers don't match.
- Rate-limit data is additive only — never changes `PetWindow`, `MoodStateMachine`, or the pet's sprite/color (per spec's Non-Goals).
- `NotifyIcon.Text` throws if assigned a string longer than 63 characters (pre-existing constraint, already handled in the current codebase — must remain respected).
- Solution root is `C:\Users\wesle\Desktop\claude_pet`. Source in `src/ClaudePet/`, tests in `tests/ClaudePet.Tests/`.

---

## Task 1: RateLimitSnapshot + RateLimitHeaderParser

**Files:**
- Create: `src/ClaudePet/Models/RateLimitSnapshot.cs`
- Create: `src/ClaudePet/Services/RateLimitHeaderParser.cs`
- Test: `tests/ClaudePet.Tests/RateLimitHeaderParserTests.cs`

**Interfaces:**
- Produces: `sealed record RateLimitSnapshot(int? RemainingTokens, int? LimitTokens, double? Percent, DateTimeOffset? ResetsAt)` and `static class RateLimitHeaderParser` with `RateLimitSnapshot Parse(IReadOnlyDictionary<string, string> headers)`. The `headers` dictionary must be constructed by the caller with `StringComparer.OrdinalIgnoreCase` — this class does not itself normalize casing. Consumed by `RateLimitReader` in Task 3.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudePet.Tests/RateLimitHeaderParserTests.cs`:

```csharp
using ClaudePet.Services;

namespace ClaudePet.Tests;

public class RateLimitHeaderParserTests
{
    private static Dictionary<string, string> Headers(params (string Key, string Value)[] pairs) =>
        new(pairs.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)),
            StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Parse_AllHeadersPresentWithIsoDateReset_ProducesFullSnapshot()
    {
        var resetTime = DateTimeOffset.UtcNow.AddHours(2);
        var headers = Headers(
            ("anthropic-ratelimit-tokens-remaining", "880000"),
            ("anthropic-ratelimit-tokens-limit", "1000000"),
            ("anthropic-ratelimit-tokens-reset", resetTime.ToString("O")));

        var result = RateLimitHeaderParser.Parse(headers);

        Assert.Equal(880000, result.RemainingTokens);
        Assert.Equal(1000000, result.LimitTokens);
        Assert.NotNull(result.Percent);
        Assert.Equal(12.0, result.Percent!.Value, precision: 3);
        Assert.NotNull(result.ResetsAt);
        Assert.Equal(resetTime, result.ResetsAt!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Parse_ResetAsIntegerSecondsFromNow_ResolvesToApproximateFutureTime()
    {
        var headers = Headers(
            ("anthropic-ratelimit-tokens-remaining", "500"),
            ("anthropic-ratelimit-tokens-limit", "1000"),
            ("anthropic-ratelimit-tokens-reset", "3600"));

        var result = RateLimitHeaderParser.Parse(headers);

        Assert.NotNull(result.ResetsAt);
        var expected = DateTimeOffset.UtcNow.AddSeconds(3600);
        Assert.Equal(expected, result.ResetsAt!.Value, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Parse_NoHeadersPresent_ProducesAllNullSnapshot()
    {
        var headers = Headers();

        var result = RateLimitHeaderParser.Parse(headers);

        Assert.Null(result.RemainingTokens);
        Assert.Null(result.LimitTokens);
        Assert.Null(result.Percent);
        Assert.Null(result.ResetsAt);
    }

    [Fact]
    public void Parse_OnlyRemainingAndLimitPresent_ComputesPercentWithNullResetsAt()
    {
        var headers = Headers(
            ("anthropic-ratelimit-tokens-remaining", "250"),
            ("anthropic-ratelimit-tokens-limit", "1000"));

        var result = RateLimitHeaderParser.Parse(headers);

        Assert.Equal(250, result.RemainingTokens);
        Assert.Equal(1000, result.LimitTokens);
        Assert.Equal(75.0, result.Percent);
        Assert.Null(result.ResetsAt);
    }

    [Fact]
    public void Parse_NonNumericRemainingValue_TreatedAsMissing()
    {
        var headers = Headers(
            ("anthropic-ratelimit-tokens-remaining", "not-a-number"),
            ("anthropic-ratelimit-tokens-limit", "1000"));

        var result = RateLimitHeaderParser.Parse(headers);

        Assert.Null(result.RemainingTokens);
        Assert.Equal(1000, result.LimitTokens);
        Assert.Null(result.Percent);
    }

    [Fact]
    public void Parse_FallsBackToInputTokensVariant_WhenPrimaryHeaderAbsent()
    {
        var headers = Headers(
            ("anthropic-ratelimit-input-tokens-remaining", "900"),
            ("anthropic-ratelimit-input-tokens-limit", "1000"));

        var result = RateLimitHeaderParser.Parse(headers);

        Assert.Equal(900, result.RemainingTokens);
        Assert.Equal(1000, result.LimitTokens);
        Assert.Equal(10.0, result.Percent);
    }

    [Fact]
    public void Parse_MixedCaseHeaderKeys_StillResolves()
    {
        var headers = Headers(
            ("Anthropic-RateLimit-Tokens-Remaining", "700"),
            ("ANTHROPIC-RATELIMIT-TOKENS-LIMIT", "1000"));

        var result = RateLimitHeaderParser.Parse(headers);

        Assert.Equal(700, result.RemainingTokens);
        Assert.Equal(1000, result.LimitTokens);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter RateLimitHeaderParserTests
```

Expected: compile error — `RateLimitSnapshot` and `RateLimitHeaderParser` don't exist yet.

- [ ] **Step 3: Implement `RateLimitSnapshot`**

Create `src/ClaudePet/Models/RateLimitSnapshot.cs`:

```csharp
namespace ClaudePet.Models;

public sealed record RateLimitSnapshot(int? RemainingTokens, int? LimitTokens, double? Percent, DateTimeOffset? ResetsAt);
```

- [ ] **Step 4: Implement `RateLimitHeaderParser`**

Create `src/ClaudePet/Services/RateLimitHeaderParser.cs`:

```csharp
using ClaudePet.Models;

namespace ClaudePet.Services;

public static class RateLimitHeaderParser
{
    // Header names are not confirmed against a live API response (no
    // ANTHROPIC_API_KEY was available during design/implementation). These
    // follow Anthropic's documented "anthropic-ratelimit-*" convention as the
    // most likely real names; RateLimitReader (Task 3) logs the full raw
    // header set on first response so a user with a real key can verify.
    private static readonly string[] RemainingHeaderCandidates =
    {
        "anthropic-ratelimit-tokens-remaining",
        "anthropic-ratelimit-input-tokens-remaining",
    };

    private static readonly string[] LimitHeaderCandidates =
    {
        "anthropic-ratelimit-tokens-limit",
        "anthropic-ratelimit-input-tokens-limit",
    };

    private static readonly string[] ResetHeaderCandidates =
    {
        "anthropic-ratelimit-tokens-reset",
        "anthropic-ratelimit-input-tokens-reset",
    };

    public static RateLimitSnapshot Parse(IReadOnlyDictionary<string, string> headers)
    {
        int? remaining = FindInt(headers, RemainingHeaderCandidates);
        int? limit = FindInt(headers, LimitHeaderCandidates);
        DateTimeOffset? resetsAt = FindResetTime(headers, ResetHeaderCandidates);

        double? percent = null;
        if (remaining is int r && limit is int l && l > 0)
        {
            percent = Math.Clamp((l - r) / (double)l * 100.0, 0, 100);
        }

        return new RateLimitSnapshot(remaining, limit, percent, resetsAt);
    }

    private static int? FindInt(IReadOnlyDictionary<string, string> headers, string[] candidates)
    {
        foreach (var name in candidates)
        {
            if (headers.TryGetValue(name, out var value) && int.TryParse(value, out var parsed))
                return parsed;
        }
        return null;
    }

    private static DateTimeOffset? FindResetTime(IReadOnlyDictionary<string, string> headers, string[] candidates)
    {
        foreach (var name in candidates)
        {
            if (!headers.TryGetValue(name, out var value))
                continue;

            if (DateTimeOffset.TryParse(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var parsedDate))
            {
                return parsedDate;
            }

            if (int.TryParse(value, out var secondsFromNow))
                return DateTimeOffset.UtcNow.AddSeconds(secondsFromNow);
        }
        return null;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test --filter RateLimitHeaderParserTests
```

Expected: all 7 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/ClaudePet/Models/RateLimitSnapshot.cs src/ClaudePet/Services/RateLimitHeaderParser.cs tests/ClaudePet.Tests/RateLimitHeaderParserTests.cs
git commit -m "feat: parse Anthropic rate-limit response headers into a snapshot"
```

---

## Task 2: TooltipFormatter (extract + extend tray tooltip logic)

**Files:**
- Create: `src/ClaudePet/Tray/TooltipFormatter.cs`
- Modify: `src/ClaudePet/Tray/TrayIconManager.cs` (full replacement below)
- Test: `tests/ClaudePet.Tests/TooltipFormatterTests.cs`

**Interfaces:**
- Consumes: `UsageSnapshot` (existing), `RateLimitSnapshot` (Task 1).
- Produces: `static class TooltipFormatter` with `const int MaxLength = 63` and `string Format(UsageSnapshot? usage, RateLimitSnapshot? rateLimit)`. Consumed by `TrayIconManager`.
- `TrayIconManager` gains `public void UpdateRateLimit(RateLimitSnapshot? snapshot)` alongside the existing `UpdateUsage`. Consumed by `App.xaml.cs` in Task 4.

This task extracts the tooltip-building logic that currently lives inline in `TrayIconManager.UpdateUsage` (`src/ClaudePet/Tray/TrayIconManager.cs:53-63`) into a new pure, testable class, then extends it to combine two data sources. This mirrors the project's existing pattern of separating pure logic (`PixelArtGenerator`, `MoodStateMachine`) from thin OS-integration wrappers.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudePet.Tests/TooltipFormatterTests.cs`:

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
        var usage = new UsageSnapshot(443155, 1000000, 44.3);
        var rateLimit = new RateLimitSnapshot(880000, 1000000, 12.0, DateTimeOffset.UtcNow.AddHours(3).AddMinutes(20));

        var result = TooltipFormatter.Format(usage, rateLimit);

        Assert.StartsWith("Claude Pet: 44% (443,155/1,000,000)\nRate limit: 12% used, resets in 3h ", result);
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
        var usage = new UsageSnapshot(999999, 1000000, 100.0);
        var rateLimit = new RateLimitSnapshot(1, 1000000, 99.9999, DateTimeOffset.UtcNow.AddHours(23).AddMinutes(59));

        var result = TooltipFormatter.Format(usage, rateLimit);

        Assert.True(result.Length <= TooltipFormatter.MaxLength);
        Assert.StartsWith("Claude Pet: 100% (999,999/1,000,000)\n", result);
    }

    [Fact]
    public void Format_ResultNeverExceedsMaxLength_EvenWithExtremeValues()
    {
        var usage = new UsageSnapshot(int.MaxValue, int.MaxValue, 100.0);
        var rateLimit = new RateLimitSnapshot(0, int.MaxValue, 100.0, DateTimeOffset.UtcNow.AddDays(400));

        var result = TooltipFormatter.Format(usage, rateLimit);

        Assert.True(result.Length <= TooltipFormatter.MaxLength);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter TooltipFormatterTests
```

Expected: compile error — `TooltipFormatter` doesn't exist yet.

- [ ] **Step 3: Implement `TooltipFormatter`**

Create `src/ClaudePet/Tray/TooltipFormatter.cs`:

```csharp
using ClaudePet.Models;

namespace ClaudePet.Tray;

public static class TooltipFormatter
{
    // NotifyIcon.Text throws if assigned a string longer than this.
    public const int MaxLength = 63;

    public static string Format(UsageSnapshot? usage, RateLimitSnapshot? rateLimit)
    {
        var line1 = FormatUsageLine(usage);
        var line2 = FormatRateLimitLine(rateLimit);

        if (line2 is null)
            return Truncate(line1, MaxLength);

        var combined = $"{line1}\n{line2}";
        if (combined.Length <= MaxLength)
            return combined;

        // Truncate the rate-limit line first: the session-usage line is the
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

    private static string? FormatRateLimitLine(RateLimitSnapshot? rateLimit)
    {
        if (rateLimit is null || rateLimit.Percent is null)
            return null;

        var resetPart = rateLimit.ResetsAt is { } resetsAt
            ? $", resets {FormatRelative(resetsAt)}"
            : "";
        return $"Rate limit: {rateLimit.Percent:F0}% used{resetPart}";
    }

    private static string FormatRelative(DateTimeOffset resetsAt)
    {
        var delta = resetsAt - DateTimeOffset.UtcNow;
        if (delta <= TimeSpan.Zero)
            return "soon";
        return delta.TotalHours >= 1
            ? $"in {(int)delta.TotalHours}h {delta.Minutes}m"
            : $"in {Math.Max(1, delta.Minutes)}m";
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length > maxLength ? text[..maxLength] : text;
}
```

- [ ] **Step 4: Replace `TrayIconManager.cs` in full**

Replace the entire contents of `src/ClaudePet/Tray/TrayIconManager.cs` with:

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

        var quitItem = new ToolStripMenuItem("Quit", null, (_, _) => System.Windows.Application.Current.Shutdown());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_dragItem);
        menu.Items.Add(runAtStartupItem);
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

    private void RefreshTooltip()
    {
        _notifyIcon.Text = TooltipFormatter.Format(_lastUsage, _lastRateLimit);
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

    public void Dispose()
    {
        _notifyIcon.Dispose();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test --filter TooltipFormatterTests
```

Expected: all 7 tests pass.

- [ ] **Step 6: Run the full suite to confirm no regressions**

```bash
dotnet test
```

Expected: all prior tests (66 as of the last plan) plus the 7 new `RateLimitHeaderParserTests` plus 7 new `TooltipFormatterTests` all pass — 80 total. `TrayIconManager` itself has no automated tests (unchanged from before this task — it's WinForms/WPF integration code, manually verified), so this refactor shouldn't have added or removed any test count for that file specifically.

- [ ] **Step 7: Commit**

```bash
git add src/ClaudePet/Tray/TooltipFormatter.cs src/ClaudePet/Tray/TrayIconManager.cs tests/ClaudePet.Tests/TooltipFormatterTests.cs
git commit -m "refactor: extract tray tooltip formatting into testable TooltipFormatter, add rate-limit line support"
```

---

## Task 3: RateLimitReader

**Files:**
- Create: `src/ClaudePet/Services/RateLimitReader.cs`

**Interfaces:**
- Consumes: `RateLimitHeaderParser.Parse` (Task 1), `DebugLog` (existing).
- Produces: `sealed class RateLimitReader : IDisposable` with constructor `RateLimitReader(string apiKey, DebugLog log)`, method `void Start()`, and `event Action<RateLimitSnapshot?>? RateLimitChanged`. Consumed by `App.xaml.cs` in Task 4.

**No automated tests for this task** — it makes a real HTTP call to Anthropic's API, and per the Global Constraints, no `ANTHROPIC_API_KEY` was available during planning to verify against a live response. This mirrors `UsageReader`'s precedent (also untested — real `FileSystemWatcher`/`Timer` integration, verified manually). Live verification of this class is explicitly deferred to the user, who will need to set `ANTHROPIC_API_KEY` themselves and check `%LOCALAPPDATA%\ClaudePet\debug.log` after running the app — this is called out explicitly in this task's manual-verification step and again in Task 4's.

- [ ] **Step 1: Implement `RateLimitReader`**

Create `src/ClaudePet/Services/RateLimitReader.cs`:

```csharp
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
```

- [ ] **Step 2: Add the LINQ using if the build requires it**

This project's implicit-usings set has previously been found to be missing some namespaces other WPF+WinForms projects get by default (`UsageReader.cs` needed an explicit `using System.IO;` for the same reason — see that file for the established pattern). Build first:

```bash
dotnet build
```

If you see an error about `Select`/`Enumerable` not being found, add `using System.Linq;` to the top of `RateLimitReader.cs` and rebuild. If you see an error about `Dictionary<,>` or `List<,>`, add `using System.Collections.Generic;`. Do not guess — add only what the compiler actually asks for, following the exact pattern `UsageReader.cs` already established for this project.

- [ ] **Step 3: Confirm the build is clean and the full suite still passes**

```bash
dotnet build
dotnet test
```

Expected: 0 warnings, 0 errors; same test count as the end of Task 2 (this task adds no new automated tests).

- [ ] **Step 4: Commit**

```bash
git add src/ClaudePet/Services/RateLimitReader.cs
git commit -m "feat: poll Anthropic count_tokens endpoint for rate-limit usage"
```

---

## Task 4: Wire RateLimitReader into App.xaml.cs

**Files:**
- Modify: `src/ClaudePet/App.xaml.cs`

**Interfaces:**
- Consumes: `RateLimitReader` (Task 3), `TrayIconManager.UpdateRateLimit` (Task 2).
- Produces: a fully wired, runnable app with the optional rate-limit feature active when `ANTHROPIC_API_KEY` is set.

- [ ] **Step 1: Add the `_rateLimitReader` field**

In `src/ClaudePet/App.xaml.cs`, add a new private field alongside the existing ones (near line 23-29, next to `_usageReader`):

```csharp
private RateLimitReader? _rateLimitReader;
```

- [ ] **Step 2: Construct and wire `RateLimitReader` conditionally, after `_usageReader.Start();`**

In `OnStartup`, immediately after the existing line `_usageReader.Start();` (around line 177, still inside the outer `try` block), add:

```csharp
var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (!string.IsNullOrWhiteSpace(apiKey))
{
    _rateLimitReader = new RateLimitReader(apiKey, log);
    _rateLimitReader.RateLimitChanged += snapshot =>
    {
        // Same reasoning as the UsageChanged handler above: BeginInvoke
        // (never a blocking Invoke) and a HasShutdownStarted/HasShutdownFinished
        // guard, since this fires from a background timer thread that can race
        // application shutdown.
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        try
        {
            Dispatcher.BeginInvoke(() =>
            {
                _trayIconManager.UpdateRateLimit(snapshot);
            });
        }
        catch (Exception ex) when (ex is TaskCanceledException or InvalidOperationException)
        {
            log.Write($"RateLimitChanged handler: dispatcher unavailable, dropping update: {ex.Message}");
        }
    };
    _rateLimitReader.Start();
}
```

- [ ] **Step 3: Dispose it in `OnExit`**

In `OnExit` (around line 189-192), add `_rateLimitReader?.Dispose();` alongside the existing disposal calls:

```csharp
protected override void OnExit(ExitEventArgs e)
{
    _usageReader?.Dispose();
    _rateLimitReader?.Dispose();
    _trayIconManager?.Dispose();
    ...
```

(Keep the rest of `OnExit` — the mutex release logic — unchanged.)

- [ ] **Step 4: Build and run the full suite**

```bash
dotnet build
dotnet test
```

Expected: 0 warnings, 0 errors; same test count as the end of Task 3 (this task adds no new automated tests — it's app-wiring code, same category as the rest of `App.xaml.cs`).

- [ ] **Step 5: Manual verification — WITHOUT an API key (this you CAN verify yourself)**

Confirm the feature is inert when unconfigured:

```bash
dotnet run --project src/ClaudePet
```

With `ANTHROPIC_API_KEY` unset (the default in this environment — confirmed absent during planning), the app should behave identically to before this plan: pet renders, tray tooltip shows only the session-usage line, no new errors in `%LOCALAPPDATA%\ClaudePet\debug.log`. Stop the process when confirmed. This step you CAN complete yourself without a key — it's proving the "off" path is truly inert.

- [ ] **Step 6: Document the deferred manual verification for the user**

This step requires a real `ANTHROPIC_API_KEY`, which is not available in this environment. Do not attempt to fabricate or simulate one. Write the following into your task report verbatim as an explicit, unchecked follow-up for the user:

> **Requires the user's own API key — not verified during implementation:**
> 1. Set `ANTHROPIC_API_KEY` in your environment and run the app.
> 2. Wait a few seconds, then check `%LOCALAPPDATA%\ClaudePet\debug.log` for a line starting with `RateLimitReader: first response headers`. This shows the exact real header names Anthropic's API returned.
> 3. Compare those real names against `RemainingHeaderCandidates`/`LimitHeaderCandidates`/`ResetHeaderCandidates` in `src/ClaudePet/Services/RateLimitHeaderParser.cs`. If none match, add the real header names to those arrays (they're checked in order, so add real names as new first entries) — no other code changes needed, this file's tests already cover the matching logic generically.
> 4. Right-click the tray icon's overflow area (or hover, depending on Windows version) to confirm the tooltip now shows a second "Rate limit: X% used, resets in ..." line.
> 5. If `debug.log` instead shows a line starting with `RateLimitReader: parsed snapshot has no usable fields`, that confirms step 3 is needed — the raw headers are logged right there in the same message.

- [ ] **Step 7: Commit**

```bash
git add src/ClaudePet/App.xaml.cs
git commit -m "feat: wire optional rate-limit reader into app startup"
```

---

## Self-Review Notes

- **Spec coverage:** Second tooltip line (Goals) → Task 2 (`TooltipFormatter`) + Task 4 (wiring). Optional/env-var-gated (Goals, Non-Goals) → Task 4 Step 2's `string.IsNullOrWhiteSpace(apiKey)` guard. Minimal-cost polling via `count_tokens` (Goals) → Task 3. No mood/sprite change (Non-Goals) → Task 2's `TrayIconManager` diff touches only tooltip logic, `PetWindow`/`MoodStateMachine` are untouched by this entire plan. Error handling (spec's Error Handling section: unset key → silent absence, network/HTTP failures → logged and skip-cycle, missing headers → partial/null snapshot) → all implemented in Task 3's `RateLimitReader.PollAsync`. Testing section (header-parsing pure/testable, HTTP call manual) → Tasks 1 and 3 respectively; tooltip-formatting pure/testable → Task 2, which the spec's Testing section anticipated by name.
- **No placeholders:** every step has complete, runnable code. The one deliberately-deferred item (live header-name verification) is not a placeholder in the code — the code is complete and defensively written — it's an explicit, actionable follow-up documented for the user because no API key existed to verify against during planning or implementation, consistent with the Global Constraints section stating this plainly up front.
- **Type consistency:** `RateLimitSnapshot(int? RemainingTokens, int? LimitTokens, double? Percent, DateTimeOffset? ResetsAt)` used identically in Tasks 1, 2, 3, 4. `TooltipFormatter.Format(UsageSnapshot?, RateLimitSnapshot?)` and `TrayIconManager.UpdateRateLimit(RateLimitSnapshot?)` used identically in Tasks 2 and 4. `RateLimitReader(string apiKey, DebugLog log)` / `.Start()` / `event Action<RateLimitSnapshot?>? RateLimitChanged` used identically in Tasks 3 and 4.
- **Scope:** single cohesive feature, no sub-projects needed.

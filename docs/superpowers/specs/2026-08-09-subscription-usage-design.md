# Claude Pet — Subscription Usage (Unofficial) — Design Spec

Date: 2026-08-09

## Overview

Adds an opt-in, second candidate source for the tray tooltip's rate-limit
line: the account's actual Pro/Max subscription 5-hour/weekly usage window,
polled from Anthropic's undocumented `GET /api/oauth/usage` endpoint using
the same OAuth credential Claude Code itself uses. This is the number the
original feedback ("did not show the current claude usage (5hr or weekly
usage)") actually asked for — the shipped `RateLimitReader` feature
(`docs/superpowers/specs/2026-07-26-rate-limit-usage-design.md`) is a proxy
via API-key rate-limit headers, not the real subscription number.

## Background / Why This Wasn't Built The First Time

The endpoint is not part of Anthropic's public API surface:

- No official documentation; behavior confirmed only via community
  reverse-engineering (GitHub issues on `anthropics/claude-code`, the
  `opencode` and `Claude-Code-Usage-Monitor` projects, and independent blog
  writeups).
- A related GitHub issue asking Anthropic to support third-party polling of
  this endpoint was closed "not planned" — there is no vendor commitment
  that it will keep working, keep its current shape, or stay reachable.
- It rate-limits aggressively and, per multiple independent reports, the
  429 backoff can get stuck and never recover within a session once
  triggered.
- Requires the OAuth session credential from
  `%USERPROFILE%\.claude\.credentials.json` — a broader-scoped, more
  sensitive credential than the `ANTHROPIC_API_KEY` the existing
  `RateLimitReader` feature uses, since it's the full Claude Code login
  session (plaintext file on Windows, protected only by NTFS permissions).

Given those risks, this is being built **opt-in only**, with defensive
handling throughout, and explicit user acknowledgment via the toggle label
that it's unofficial.

## Goals

- Show the real subscription-level 5-hour/weekly usage percentage in the
  tray tooltip, when the user explicitly opts in.
- Never worse than doing nothing: any failure (missing credential, network
  error, endpoint shape change) falls back to whatever the existing
  `RateLimitReader` path would show, with no crash and no silent data
  corruption.
- Minimize exposure of the OAuth credential: read it as few times as
  possible, hold it in memory only, never log its value.
- Be a good API citizen despite polling an endpoint with no public rate
  limit contract: poll infrequently, back off hard and increasingly on
  failure, never hammer.

## Non-Goals

- OAuth token refresh. If the cached access token expires and Claude Code
  hasn't rotated the credential file in the meantime, this feature simply
  stops producing data for the rest of the session (falls back per Goals
  above) rather than implementing a refresh-token flow.
- Any UI beyond a single tray-menu checkbox — no settings dialog, no
  separate window.
- Removing or changing `RateLimitReader` — it keeps running unconditionally
  exactly as today; this feature is a second candidate for the same tooltip
  slot, not a replacement of the header-based approach.

## Architecture

```
Tray menu: "Show subscription usage (unofficial)" checkbox
    │ (persisted to AppSettings.ShowSubscriptionUsage)
    │
    ├─ unchecked at startup ──▶ SubscriptionUsageReader constructed but never Start()ed
    │
    └─ checked (at startup, or toggled live) ──▶ SubscriptionUsageReader.Start()
            │  (every 5 min via a Timer; interval grows on failure, see Backoff)
            ▼
        Need a valid cached access token?
            │ no cached token, or now >= expiresAt - safety margin
            ▼
        SubscriptionCredentialReader reads
        %USERPROFILE%\.claude\.credentials.json
            │ any failure (missing file / bad JSON / missing field) → skip this poll cycle, retry next tick, no backoff penalty
            ▼
        GET https://api.anthropic.com/api/oauth/usage
          headers: Authorization: Bearer <token>
                   anthropic-beta: oauth-2025-04-20
                   User-Agent: claude-code/2.1.220
            │
            ├─ HTTP failure (429/401/5xx/exception) → dedup-logged once per
            │   distinct status/exception type, backoff interval doubles
            │   (capped at 60 min), last displayed value untouched.
            │   401 additionally clears the cached token so the next
            │   attempt re-reads the credential file.
            │
            └─ 200 OK → SubscriptionUsageParser.Parse(body)
                    │ schema mismatch → dedup-logged once, treated like any
                    │ other poll failure (backoff, no crash)
                    ▼
                SubscriptionUsageSnapshot
                    │ raises SubscriptionUsageChanged(snapshot); backoff
                    │ interval resets to 5 min
                    ▼
                App.xaml.cs handler → Dispatcher.BeginInvoke →
                TrayIconManager.UpdateSubscriptionUsage(snapshot)
                    ▼
                TooltipFormatter.Format(usage, rateLimit, subscriptionUsage)
                  line 2 = subscriptionUsage ?? rateLimit ?? (nothing)
                    ▼
                NotifyIcon.Text updated
```

`RateLimitReader` (the existing API-key header-based reader) keeps running
unconditionally in the background whenever `ANTHROPIC_API_KEY` is set,
regardless of this feature's toggle state — this is what makes the
"fall back to existing display" behavior work with no special-casing:
`TooltipFormatter` just prefers whichever of the two snapshots is present,
subscription first.

## Components

1. **`SubscriptionCredentialReader`** (new, `src/ClaudePet/Services/`) —
   reads `%USERPROFILE%\.claude\.credentials.json`, parses the
   `claudeAiOauth.accessToken` (string) and `claudeAiOauth.expiresAt`
   (epoch milliseconds) fields. Returns `null` on any failure: file
   missing, unreadable, malformed JSON, or missing/wrong-typed fields.
   Never throws out of `TryRead()`. Never logs the token value — log
   messages on failure reference only "credential file missing" /
   "credential file malformed" / "missing expected field", never file
   contents.

2. **`SubscriptionUsageParser`** (new, `src/ClaudePet/Services/`) — pure
   static parser mirroring `RateLimitHeaderParser`. Input: the raw JSON
   response body. Output: `SubscriptionUsageSnapshot?` (`null` if the
   expected fields aren't present in a recognizable shape). Responsibilities:
   - Read `five_hour.utilization`/`five_hour.resets_at` and
     `seven_day.utilization`/`seven_day.resets_at`; either may be absent
     or `null` (mirrors observed `seven_day_opus: null` behavior) and
     should be treated as "that window has no data," not an error.
   - **Utilization scale normalization:** sources disagree on whether
     `utilization` is 0–100 or 0–1. If the value is `<= 1.0`, treat it as
     a fraction and multiply by 100; otherwise treat it as already a
     percentage. Applied independently per window.
   - Pick whichever of the two windows (that has data) has the higher
     normalized percentage. If only one has data, use that one. If
     neither has data, return `null`.
   - `WindowLabel` is `"5h"` or `"7d"` depending on which window was
     selected.

3. **`SubscriptionUsageSnapshot`** (new, `src/ClaudePet/Models/`) — record
   `SubscriptionUsageSnapshot(double Percent, string WindowLabel,
   DateTimeOffset? ResetsAt)`.

4. **`SubscriptionUsageReader`** (new, `src/ClaudePet/Services/`),
   `IDisposable` — mirrors `RateLimitReader`'s `Timer` + dedup-logging
   pattern, with two differences:
   - **Mutable backoff interval:** base 5 minutes; on each consecutive
     HTTP-level failure, double the timer's `Interval` up to a 60-minute
     ceiling; reset to 5 minutes on the next success. Missing/malformed
     local credential file does *not* count as a failure for backoff
     purposes (it's a free local read, not a network call) — the timer
     stays at whatever its current interval is and just retries next tick.
   - **`Start()` / `Stop()`, not just `Dispose()`:** since this reader is
     toggled live from the tray menu rather than constructed once
     conditionally at app startup like `RateLimitReader`. `Stop()` stops
     the timer without disposing the `HttpClient`, so the same instance
     can be restarted if the user re-enables the toggle later in the same
     session. `Dispose()` (called at app exit) stops the timer and
     disposes the `HttpClient`.

   Holds the cached `(string AccessToken, DateTimeOffset ExpiresAt)?` in a
   private field, populated via `SubscriptionCredentialReader` on first
   need and re-populated when missing/expired/cleared-by-401. Constructs
   the request with `User-Agent: claude-code/2.1.220` (a hardcoded,
   plausible current Claude Code version string — see Open Assumptions)
   and `anthropic-beta: oauth-2025-04-20`. Exposes
   `event Action<SubscriptionUsageSnapshot?>? SubscriptionUsageChanged` —
   only invoked on a successful parse (never invoked with `null` on
   failure; the UI simply keeps showing whatever it last had, per the
   "leave displayed value untouched" error-handling rule).

5. **`AppSettings`** (modified) — adds
   `bool ShowSubscriptionUsage { get; init; }`, default `false`.

6. **`TrayIconManager`** (modified) — adds a
   `ToolStripMenuItem("Show subscription usage (unofficial)")` checkbox
   next to the existing toggles, `Checked` initialized from
   `AppSettings.ShowSubscriptionUsage`. Toggling it persists the setting
   (same pattern as `ToggleRunAtStartup`) and raises a
   `SubscriptionUsageToggled` event (bool) that `App.xaml.cs` subscribes
   to, so starting/stopping the reader stays `App.xaml.cs`'s
   responsibility rather than `TrayIconManager` owning the reader
   directly — matches the existing separation where `TrayIconManager`
   doesn't own `RateLimitReader` either. Adds `_lastSubscriptionUsage`
   field and `UpdateSubscriptionUsage(SubscriptionUsageSnapshot? snapshot)`,
   called independently of `UpdateUsage`/`UpdateRateLimit`.
   `RefreshTooltip` passes all three snapshots to `TooltipFormatter.Format`.

7. **`TooltipFormatter`** (modified) — `Format` gains a third parameter,
   `SubscriptionUsageSnapshot? subscriptionUsage`. Line 2 selection
   becomes: `subscriptionUsage` if non-null → formatted as
   `"Sub: {Percent:F0}% ({WindowLabel})"` with an optional
   `, {relative-reset}"` suffix reusing the existing `FormatRelative`
   helper; else fall back to the current `RateLimitSnapshot`-based line 2
   logic, unchanged. The existing 63-character truncation/budget logic
   applies identically regardless of which source produced line 2 — it
   operates on the already-formatted string, not on which snapshot type
   it came from.

8. **`App.xaml.cs`** (modified) — constructs `SubscriptionUsageReader`
   unconditionally (cheap — it does nothing until `Start()`), calls
   `.Start()` immediately if `settings.ShowSubscriptionUsage` was `true`
   at load. Subscribes to `SubscriptionUsageChanged` with the same
   `Dispatcher.BeginInvoke` + `HasShutdownStarted`/`HasShutdownFinished`
   guard pattern used for `UsageChanged`/`RateLimitChanged`. Subscribes to
   `TrayIconManager.SubscriptionUsageToggled` to call `Start()`/`Stop()`
   live. Disposes the reader in `OnExit` alongside the other services.

## Error Handling

- Credential file missing, unreadable, malformed JSON, or missing
  `claudeAiOauth.accessToken`/`expiresAt` → `SubscriptionCredentialReader`
  returns `null`; `PollAsync` skips this cycle (no HTTP call made), no
  backoff penalty, retried at the current interval next tick. No token
  value ever appears in a log message.
- Network exception / non-2xx HTTP response (429, 401, 5xx) → caught,
  dedup-logged once per distinct status/exception type (same pattern as
  `RateLimitReader`'s `_lastLoggedErrorStatus`/`_lastLoggedExceptionType`),
  backoff interval doubles up to the 60-minute ceiling, currently
  displayed tooltip value is left untouched (no flicker to blank on a
  transient failure). A 401 additionally clears the in-memory cached
  token so the next poll re-reads the credential file, in case Claude
  Code rotated it via its own refresh flow.
- Response parses as JSON but doesn't contain the expected `five_hour` /
  `seven_day` shape → `SubscriptionUsageParser` returns `null`, treated
  identically to an HTTP-level failure for logging/backoff purposes. This
  is the expected degradation path if Anthropic changes the endpoint's
  undocumented shape — the app keeps running, keeps showing whatever it
  showed before (falling back to `RateLimitReader`'s data if this feature
  never had a successful poll), never crashes.
- `SubscriptionUsageChanged` is only ever raised with a non-null snapshot;
  there's no "explicit null to clear the line" signal, by design, so a
  transient failure can't blank out a previously-good value.

## Testing

- `SubscriptionUsageParser` — pure unit tests: 0–100 vs 0–1 scale
  normalization for each window independently, `five_hour` vs `seven_day`
  selection when both present, selection when only one is present,
  `null`-window handling (mirroring observed `seven_day_opus: null`),
  and malformed/missing-field input returning `null`.
- `SubscriptionCredentialReader` — unit tests against synthetic JSON
  written to a temp file (never the real `.credentials.json`): valid
  schema, missing file, malformed JSON, missing `accessToken` field,
  missing `expiresAt` field.
- `TooltipFormatterTests` — new cases for: subscription line present +
  rate-limit line present (subscription wins line 2); subscription line
  null + rate-limit line present (falls back); both null (line 2 absent,
  matching current behavior); truncation still respects the 63-character
  budget with a subscription-sourced line 2.
- `SubscriptionUsageReader` itself (the HTTP call, backoff timer mutation,
  credential caching/expiry/401-clearing logic) is integration-style,
  verified manually against the real endpoint during implementation using
  the developer's own logged-in Claude Code session — same approach
  `RateLimitReader` used for its own header-name verification.

## Open Assumptions

- **Response schema is not independently confirmed against a live call**
  — it's reconstructed from third-party reverse-engineering writeups
  (GitHub issues/PRs, a blog post), which disagreed on the utilization
  scale. The implementing task should make one real call during
  implementation and log (structure only, not values) whether the 0–100
  or 0–1 assumption holds, adjusting `SubscriptionUsageParser` if needed
  — the normalization logic in this spec is designed to tolerate either,
  but should still be verified once against real data.
- **`User-Agent: claude-code/2.1.220` is a hardcoded version string, not
  dynamically detected.** It will eventually go stale as Claude Code
  releases new versions; per the community reports this design is based
  on, the presence of a `claude-code/*`-shaped value (not an exact
  version match) is what avoids the aggressive throttling, so staleness
  is expected to degrade gracefully rather than break outright. Not worth
  the complexity of reading the installed Claude Code version at runtime
  for a value that's read-only telemetry to a third-party endpoint.
- **This endpoint can be removed or reshaped by Anthropic at any time**
  with no notice, per the earlier investigation (a GitHub issue requesting
  official support was closed "not planned"). This design's entire error-
  handling posture is built around that assumption already holding true
  on day one, not as a hypothetical.
- **No token refresh is implemented.** If the access token expires and
  Claude Code isn't running/hasn't refreshed the file, this feature goes
  quiet (falls back per Error Handling) until either Claude Code refreshes
  the file on its own or the app restarts and re-reads a fresh token.

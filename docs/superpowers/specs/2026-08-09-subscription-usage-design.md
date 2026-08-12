# Claude Pet — Subscription Usage (Unofficial) — Design Spec

Date: 2026-08-09 (revised 2026-08-12 — switched endpoint, see Revision Notes)

## Overview

Adds an opt-in, second candidate source for the tray tooltip's rate-limit
line: the account's actual Pro/Max subscription 5-hour/weekly usage window,
read from `anthropic-ratelimit-unified-*` response headers on an
OAuth-authenticated call to Anthropic's `POST /v1/messages` endpoint (a real,
tiny generation call with `max_tokens: 1` — see the second Revision Note
below), using the same OAuth credential Claude Code itself uses. This is the
number the original feedback ("did not show the current claude usage (5hr or
weekly usage)") actually asked for — the shipped `RateLimitReader` feature
(`docs/superpowers/specs/2026-07-26-rate-limit-usage-design.md`) is a proxy
via per-API-key rate-limit headers, not the real subscription number.

## Revision Note (2026-08-12, endpoint family: `/api/oauth/usage` → Messages)

The original version of this spec (2026-08-09) targeted the undocumented
`GET /api/oauth/usage` endpoint. Further research turned up a materially
better path: Claude Code's own OAuth-authenticated API traffic already
carries `anthropic-ratelimit-unified-*` response headers on ordinary
Messages-family endpoints (confirmed across several independent
`anthropics/claude-code` GitHub issues, plus third-party tool repos —
several are literally issues asking Anthropic to *persist* headers Claude
Code already receives and discards). This spec now targets that path
instead. Everything about the opt-in/credential-handling/fallback
philosophy is unchanged; what changed is the endpoint, the auth headers,
and how the response is parsed (response headers instead of a JSON body).
See `RateLimitReader`/`RateLimitHeaderParser` in the shipped feature — this
new reader is now architecturally a close sibling of that one, not a
separate JSON-endpoint client.

## Revision Note (2026-08-12, second pivot: `count_tokens` → real `messages.create`)

This spec's first revision (above) still assumed the plan's originally
proposed `POST /v1/messages/count_tokens` endpoint — free, no-generation —
would carry the `anthropic-ratelimit-unified-*` headers. Task 4's live
verification against a real logged-in Claude Code session (the same
verification step this spec's Open Assumptions section called for) showed
that `count_tokens` responses do **not** carry those headers; only a real
`POST /v1/messages` call does. Implementation switched to a real
`messages.create` call with `max_tokens: 1` (this spec's own pre-approved
fallback — see Open Assumptions: "if implementation reveals `count_tokens`
doesn't carry the ... headers, fall back to a minimal `messages.create`
call instead"). This was correctly implemented and reviewed at the task
level; this note exists because the rest of this document (Architecture,
Components §4, Non-Goals) still described `count_tokens` and its
no-generation-billing premise, and has now been corrected to match.
Concretely, this means the feature issues a real (tiny, `max_tokens: 1`)
generation call roughly every 5–60 minutes while enabled — depending on
backoff state — consuming a small amount of the same subscription quota it
displays. See `src/ClaudePet/Services/SubscriptionUsageReader.cs` for the
exact current request.

## Background / Why This Wasn't Built The First Time

Neither the endpoint nor the headers are part of Anthropic's public,
documented API surface:

- No official documentation of `anthropic-ratelimit-unified-*`; behavior
  confirmed only via community reverse-engineering (multiple GitHub issues
  on `anthropics/claude-code`, `steipete/CodexBar`, `openclaw`, and an
  independent blog writeup).
- These headers require **OAuth Bearer authentication** (the Claude Code
  login credential) — they reflect the account's subscription usage
  specifically, "independent of individual API keys" per the sources
  found; a request authenticated with a plain `ANTHROPIC_API_KEY` is not
  expected to carry them.
- Requires reading `%USERPROFILE%\.claude\.credentials.json` — a
  broader-scoped, more sensitive credential than the `ANTHROPIC_API_KEY`
  the existing `RateLimitReader` feature uses, since it's the full Claude
  Code login session (plaintext file on Windows, protected only by NTFS
  permissions).
- No vendor commitment that these headers keep existing or keep their
  current names — this is Claude Code's own internal plumbing, not a
  published contract.

Given those risks, this is being built **opt-in only**, with defensive
handling throughout, and explicit user acknowledgment via the toggle label
that it's unofficial — same posture as the original endpoint-based version
of this spec, just now on more solid technical footing (a standard,
well-documented public endpoint, rather than a special hidden route that
independently reported broken/permanently-stuck 429 behavior).

## Goals

- Show the real subscription-level 5-hour/weekly usage percentage in the
  tray tooltip, when the user explicitly opts in.
- Never worse than doing nothing: any failure (missing credential, network
  error, headers absent/renamed) falls back to whatever the existing
  `RateLimitReader` path would show, with no crash and no silent data
  corruption.
- Minimize exposure of the OAuth credential: read it as few times as
  possible, hold it in memory only, never log its value.
- Be a good API citizen: poll infrequently, back off on failure — though
  since this now rides on a standard documented endpoint rather than a
  special hidden one, the aggressive/stuck-429 risk from the original
  design is expected to be much lower.

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
- Spoofing a `User-Agent: claude-code/<version>` value. That workaround was
  specific to the old `/api/oauth/usage` endpoint's reported
  User-Agent-gated throttling; there's no evidence the standard
  `POST /v1/messages` endpoint behaves that way, so this design sends an
  honest client identity (see Open Assumptions).

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
        POST https://api.anthropic.com/v1/messages
          headers: Authorization: Bearer <token>
                   anthropic-version: 2023-06-01
                   anthropic-beta: oauth-2025-04-20
          body: real minimal generation payload, max_tokens: 1 (only a real
                messages.create call carries the anthropic-ratelimit-unified-*
                headers — see Revision Note above; Components §4)
            │
            ├─ HTTP failure (429/401/5xx/exception) → dedup-logged once per
            │   distinct status/exception type, backoff interval doubles
            │   (capped at 60 min), last displayed value untouched.
            │   401 additionally clears the cached token so the next
            │   attempt re-reads the credential file.
            │
            └─ 200 OK → SubscriptionUsageParser.Parse(responseHeaders)
                    │ expected headers absent/unrecognized → dedup-logged
                    │ once, treated like any other poll failure (backoff,
                    │ no crash)
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
subscription first. The two readers are independent, different auth (API
key vs. OAuth Bearer), different credential source, different response
headers they read (`x-ratelimit-*`-family headers vs.
`anthropic-ratelimit-unified-*` headers) — and, per the second Revision
Note above, now also different target endpoints (`RateLimitReader` still
uses `count_tokens`; `SubscriptionUsageReader` uses a real
`POST /v1/messages` call, since only the latter carries the unified-header
family). They can run simultaneously without interfering with each other.

## Components

1. **`SubscriptionCredentialReader`** (new, `src/ClaudePet/Services/`) —
   reads `%USERPROFILE%\.claude\.credentials.json`, parses the
   `claudeAiOauth.accessToken` (string) and `claudeAiOauth.expiresAt`
   (epoch milliseconds) fields. Returns `null` on any failure: file
   missing, unreadable, malformed JSON, or missing/wrong-typed fields.
   Never throws out of `TryRead()`. Never logs the token value — log
   messages on failure reference only "credential file missing" /
   "credential file malformed" / "missing expected field", never file
   contents. **Unchanged from the original 2026-08-09 version.**

2. **`SubscriptionUsageParser`** (new, `src/ClaudePet/Services/`) — pure
   static parser, now mirroring `RateLimitHeaderParser`'s shape exactly:
   input is a `Dictionary<string, string>` of response headers (not a JSON
   body). Output: `SubscriptionUsageSnapshot?` (`null` if the expected
   headers aren't present in a recognizable shape). Responsibilities:
   - Read `anthropic-ratelimit-unified-5h-utilization` /
     `anthropic-ratelimit-unified-5h-reset` and
     `anthropic-ratelimit-unified-7d-utilization` /
     `anthropic-ratelimit-unified-7d-reset`. Either pair may be absent
     and should be treated as "that window has no data," not an error.
   - **Utilization scale:** sources agree this is a 0.0–1.0 fraction
     (e.g. `0.07`, `0.53`) — multiply by 100 for display. (Unlike the
     original endpoint-based design, there's no scale ambiguity here; all
     corroborating sources agree on 0.0–1.0. Still worth a defensive
     clamp to `[0, 100]` after conversion in case of a malformed value,
     but no dual-scale detection logic is needed.)
   - **Reset time:** Unix epoch **seconds** (not milliseconds, not ISO
     string) — convert via `DateTimeOffset.FromUnixTimeSeconds`.
   - Pick whichever of the two windows (that has data) has the higher
     percentage. If only one has data, use that one. If neither has data,
     return `null`.
   - `WindowLabel` is `"5h"` or `"7d"` depending on which window was
     selected.
   - The `anthropic-ratelimit-unified-status` and
     `-representative-claim` headers are not needed for this feature's
     display logic (they matter for request-blocking decisions, which
     this read-only tray display doesn't do) — parser ignores them.

3. **`SubscriptionUsageSnapshot`** (new, `src/ClaudePet/Models/`) — record
   `SubscriptionUsageSnapshot(double Percent, string WindowLabel,
   DateTimeOffset? ResetsAt)`. **Unchanged from the original version.**

4. **`SubscriptionUsageReader`** (new, `src/ClaudePet/Services/`),
   `IDisposable` — mirrors `RateLimitReader`'s overall shape (timer-driven
   polling, header-collection-into-`Dictionary` pattern before handing off
   to the parser, dedup-logging, backoff), but per the second Revision Note
   above targets a **different endpoint** than `RateLimitReader`: a real
   `POST https://api.anthropic.com/v1/messages` call (not `count_tokens` —
   live verification during Task 4 showed `count_tokens` doesn't carry the
   `anthropic-ratelimit-unified-*` headers this feature needs) with body
   `{"model":"claude-haiku-4-5","messages":[{"role":"user","content":"hi"}],
   "max_tokens":1}` — `max_tokens: 1` keeps the accepted, intentional
   generation cost (roughly one output token per poll, every 5–60 minutes
   depending on backoff) as small as possible. See
   `src/ClaudePet/Services/SubscriptionUsageReader.cs` for the exact
   current constants. Differs from `RateLimitReader` in:
   - **Endpoint:** real `messages.create` (`/v1/messages`), not
     `/v1/messages/count_tokens` — see above.
   - **Auth:** `Authorization: Bearer <oauth-access-token>` +
     `anthropic-beta: oauth-2025-04-20`, instead of `x-api-key`. Still
     sends `anthropic-version: 2023-06-01` (required on every Messages
     API call regardless of auth scheme).
   - **Credential source:** the OAuth token from
     `SubscriptionCredentialReader`, not a constructor-supplied API key.
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
   need and re-populated when missing/expired/cleared-by-401. Exposes
   `event Action<SubscriptionUsageSnapshot?>? SubscriptionUsageChanged` —
   only invoked on a successful parse (never invoked with `null` on
   failure; the UI simply keeps showing whatever it last had, per the
   "leave displayed value untouched" error-handling rule).

5. **`AppSettings`** (modified) — adds
   `bool ShowSubscriptionUsage { get; init; }`, default `false`.
   **Unchanged from the original version.**

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
   **Unchanged from the original version.**

7. **`TooltipFormatter`** (modified) — `Format` gains a third parameter,
   `SubscriptionUsageSnapshot? subscriptionUsage`. Line 2 selection
   becomes: `subscriptionUsage` if non-null → formatted as
   `"Sub: {Percent:F0}% ({WindowLabel})"` with an optional
   `, {relative-reset}"` suffix reusing the existing `FormatRelative`
   helper; else fall back to the current `RateLimitSnapshot`-based line 2
   logic, unchanged. The existing 63-character truncation/budget logic
   applies identically regardless of which source produced line 2 — it
   operates on the already-formatted string, not on which snapshot type
   it came from. **Unchanged from the original version.**

8. **`App.xaml.cs`** (modified) — constructs `SubscriptionUsageReader`
   unconditionally (cheap — it does nothing until `Start()`), calls
   `.Start()` immediately if `settings.ShowSubscriptionUsage` was `true`
   at load. Subscribes to `SubscriptionUsageChanged` with the same
   `Dispatcher.BeginInvoke` + `HasShutdownStarted`/`HasShutdownFinished`
   guard pattern used for `UsageChanged`/`RateLimitChanged`. Subscribes to
   `TrayIconManager.SubscriptionUsageToggled` to call `Start()`/`Stop()`
   live. Disposes the reader in `OnExit` alongside the other services.
   **Unchanged from the original version.**

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
- Response is a 200 but the expected `anthropic-ratelimit-unified-5h-*` /
  `-7d-*` headers are absent, or present but unparseable (non-numeric
  utilization, non-numeric reset) → `SubscriptionUsageParser` returns
  `null`, treated identically to an HTTP-level failure for
  logging/backoff purposes. This is the expected degradation path if
  Anthropic renames or removes these headers — the app keeps running,
  keeps showing whatever it showed before (falling back to
  `RateLimitReader`'s data if this feature never had a successful poll),
  never crashes.
- `SubscriptionUsageChanged` is only ever raised with a non-null snapshot;
  there's no "explicit null to clear the line" signal, by design, so a
  transient failure can't blank out a previously-good value.

## Testing

- `SubscriptionUsageParser` — pure unit tests: header presence/absence for
  each window independently, `5h` vs `7d` selection when both present,
  selection when only one is present, fraction-to-percent conversion,
  epoch-seconds-to-`DateTimeOffset` conversion, and malformed/missing
  header input returning `null`.
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
  `RateLimitReader` used for its own header-name verification. This is
  also the point at which the header-name and value-format assumptions
  below get confirmed or corrected.

## Open Assumptions

- **Header names/format are not independently confirmed against a live
  response** — reconstructed from third-party reverse-engineering
  (multiple GitHub issues, one blog post), not official docs. The
  implementing task should make one real OAuth-authenticated call during
  implementation and log the full header set once (names only where
  possible; if values must be logged to confirm the format, do so to a
  local console/temporary breakpoint during development, not into
  `debug.log`) before finalizing `SubscriptionUsageParser`'s exact header
  keys — mirroring exactly how `RateLimitHeaderParser` was verified
  against a real response for the original feature.
- **RESOLVED during Task 4 (see second Revision Note above):** whether
  `count_tokens` (vs. only real `messages.create` calls) returns these
  headers was open at design time; live verification confirmed
  `count_tokens` does **not** carry them, so the implementation uses the
  pre-approved fallback — a minimal real `messages.create` call
  (`max_tokens: 1`) — instead. Same `SubscriptionUsageSnapshot` output
  shape, no redesign needed, exactly as anticipated below. Note this means
  `RateLimitReader` and `SubscriptionUsageReader` no longer share a target
  endpoint (`RateLimitReader` still uses the free `count_tokens` endpoint
  for its own purposes; only `SubscriptionUsageReader` needed to move to a
  real generation call).
- **No `User-Agent` spoofing.** The original endpoint-based design sent
  `User-Agent: claude-code/<version>` because community reports tied that
  specific header to `/api/oauth/usage`'s throttling behavior. This
  design targets a standard public endpoint instead, with no evidence of
  User-Agent-gated throttling, so it sends whatever `HttpClient`'s default
  identity is (same as `RateLimitReader` already does today) rather than
  impersonating Claude Code.
- **These headers may only appear for accounts on a plan with "unified
  rate limits"** (per one source) — if the developer's own account
  doesn't have them for some plan-related reason, that surfaces during
  the same manual verification step above, and degrades to "feature
  never gets a successful poll, always falls back to `RateLimitReader`,"
  which is already a fully handled path per Error Handling.
- **This endpoint/header combination can still change with no notice** —
  it's Claude Code's internal plumbing, not a published contract, even
  though it's carried on a stable, documented endpoint. This design's
  error-handling posture is built around that assumption already holding
  true on day one, not as a hypothetical.
- **No token refresh is implemented.** If the access token expires and
  Claude Code isn't running/hasn't refreshed the file, this feature goes
  quiet (falls back per Error Handling) until either Claude Code refreshes
  the file on its own or the app restarts and re-reads a fresh token.

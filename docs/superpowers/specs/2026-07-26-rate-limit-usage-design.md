# Claude Pet — Rate-Limit Usage Indicator — Design Spec

Date: 2026-07-26

## Overview

Adds a second, independent usage signal to Claude Pet: the Anthropic API
key's rolling rate-limit usage (tokens remaining / reset time), shown
alongside the existing session-context-window percentage in the tray
tooltip. This directly answers the feedback that the app "did not show the
current claude usage (5hr or weekly usage)."

## Background / Investigated Alternatives

Claude Code's own UI shows a Pro/Max subscription's 5-hour/weekly usage
window. There is no confirmed, documented public API that exposes that
specific subscription-level number to a third-party application. What *is*
documented and confirmed accessible is the Messages API's per-key rate-limit
response headers (`x-ratelimit-*`), returned on every request against a real
`ANTHROPIC_API_KEY`. This spec targets that — it may or may not track 1:1
with what Claude Code's own UI shows for a Pro/Max plan, but it is the real,
buildable signal available today.

## Goals

- Show a second usage number (rate-limit % used, reset time) in the tray
  tooltip, alongside the existing session-context percentage.
- Fully optional: if `ANTHROPIC_API_KEY` is not set in the environment, the
  feature is silently absent — no error, no crash, no configuration UI.
- Minimal cost: poll via the `count_tokens` endpoint (no generation, no
  output-token billing) rather than a real `messages.create` call.

## Non-Goals

- Querying Claude Code's subscription-specific 5-hour/weekly session limit
  directly (no confirmed public API for this).
- Any API-key configuration UI (env var only, per user's explicit choice).
- Changing the pet's mood/color/sprite based on rate-limit usage — this is
  purely a second tooltip line, additive to the existing session-context
  mood mechanic.

## Architecture

```
App startup
    │
    ▼
ANTHROPIC_API_KEY present? ──No──▶ RateLimitReader never constructed (feature off)
    │ Yes
    ▼
RateLimitReader.Start()
    │  (every 5 min via a Timer)
    ▼
POST https://api.anthropic.com/v1/messages/count_tokens
  headers: x-api-key, anthropic-version: 2023-06-01
  body: minimal model + 1-message payload (no generation, no output tokens billed)
    │
    ▼
Parse x-ratelimit-* response headers → RateLimitSnapshot
    │  raises RateLimitChanged(snapshot)
    │
    ▼
App.xaml.cs handler → Dispatcher.BeginInvoke → TrayIconManager.UpdateRateLimit(snapshot)
    │
    ▼
NotifyIcon.Text updated with a second tooltip line
```

On any HTTP failure (network error, bad key, 429, 5xx) the reader logs via
`DebugLog` and skips that cycle — the tooltip keeps showing the last known
value (or omits the rate-limit line if there's never been a successful
call). The reader never crashes the app.

## Components

1. **`RateLimitReader`** (new, `src/ClaudePet/Services/`) — owns a `Timer`
   firing every 5 minutes. On each tick, POSTs to
   `https://api.anthropic.com/v1/messages/count_tokens` using a plain
   `HttpClient` (no full SDK dependency — this is a single lightweight call,
   the app doesn't need the SDK's typed response models). Reads the
   `x-ratelimit-*` response headers and parses them into a
   `RateLimitSnapshot`. Exposes `event Action<RateLimitSnapshot?>?
   RateLimitChanged`. Constructed in `App.xaml.cs` only when
   `Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")` is non-empty.

   **Known implementation risk, to be resolved during implementation, not
   guessed here:** the exact `x-ratelimit-*` header names returned by the
   `count_tokens` endpoint are not confirmed by the docs consulted during
   design. The implementing task must make one real call with the user's
   own `ANTHROPIC_API_KEY` and print every response header before writing
   the parsing code, rather than hardcoding assumed header names.

2. **`RateLimitSnapshot`** (new, `src/ClaudePet/Models/`) — record with
   `int? RemainingTokens`, `int? LimitTokens`, `double? Percent`,
   `DateTimeOffset? ResetsAt`. All nullable since not every header is
   guaranteed present on every response; the tooltip formatter degrades
   gracefully field-by-field.

3. **`TrayIconManager`** (modified) — gains
   `void UpdateRateLimit(RateLimitSnapshot? snapshot)`, called independently
   of `UpdateUsage`. Tooltip text becomes two lines when both are available;
   respects the existing 63-character `NotifyIcon.Text` limit (truncate the
   second line first if needed, since the session-usage line is the
   established primary signal).

4. **`App.xaml.cs`** (modified) — constructs `RateLimitReader` conditionally
   on the API key being present, wires `RateLimitChanged` to
   `TrayIconManager.UpdateRateLimit` via the same
   `Dispatcher.BeginInvoke`-outside-lock pattern already used for
   `UsageReader`.

## Error Handling

- `ANTHROPIC_API_KEY` unset → `RateLimitReader` is never constructed. No log
  entry needed (this is normal, expected configuration, not an error).
- Network failure / timeout → caught, logged via `DebugLog`, skip this
  cycle, keep prior snapshot.
- Non-2xx HTTP response (401 invalid key, 429 rate-limited, 5xx) → caught,
  logged via `DebugLog` with the status code, skip this cycle, keep prior
  snapshot. A 401 is expected to repeat every cycle if the key is invalid;
  the existing `_lastWarnedModel`-style dedup pattern from `UsageReader`
  should be reused here so a bad key doesn't spam `debug.log` every 5
  minutes forever.
- Missing/unexpected rate-limit headers on an otherwise-successful response
  → treated as a partial snapshot (nullable fields stay `null`), not an
  error; the tooltip formatter shows whatever fields are present.

## Testing

- `RateLimitSnapshot` parsing logic (given a set of header key/value pairs,
  produce the correct snapshot) is unit-testable in isolation — extract the
  header-parsing logic into a pure function separate from the HTTP call
  itself, mirroring how `UsageParser` is separate from `UsageReader`.
- The HTTP call itself (`RateLimitReader`) is integration-style, verified
  manually against the real API during implementation, same pattern as
  `UsageReader` in the original plan.
- `TrayIconManager.UpdateRateLimit`'s tooltip formatting (including the
  63-character truncation behavior with both lines present) is unit
  testable without any network dependency.

## Open Assumptions

- The `count_tokens` endpoint returns the same `x-ratelimit-*` headers as
  `messages.create` — not independently confirmed; if false, the
  implementation falls back to a minimal `messages.create` call instead
  (this is a build-time decision, not a redesign, since both paths produce
  the same `RateLimitSnapshot` shape).
- This rate-limit signal may not match Claude Code's own subscription-level
  5-hour/weekly display 1:1 — this is explicitly a best-available proxy per
  the Background section above, not a guaranteed equivalent.

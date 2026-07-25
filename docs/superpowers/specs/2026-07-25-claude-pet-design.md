# Claude Pet — Design Spec

Date: 2026-07-25

## Overview

A pixel-art desktop pet that lives on top of the Windows desktop and reflects
how full the current Claude Code session's context window is. Instead of a
numeric HUD, the pet's mood/animation changes as context usage climbs, giving
a glanceable signal for "how close am I to needing to compact/clear."

## Goals

- Always-on-top, transparent, click-through pixel-art overlay on the Windows
  desktop.
- Mood reflects the **active** Claude Code session's context window usage
  (not account-wide usage, not cumulative token spend).
- "Active session" = the most recently modified top-level session `*.jsonl`
  file under `~/.claude/projects/`.
- Runs continuously in the background with minimal resource footprint.

## Non-Goals

- Cross-platform support (Windows only for v1).
- Account-wide / billing usage (Anthropic Console usage API).
- Tracking multiple sessions simultaneously (one pet, one active session).
- Rate-limit / rolling-window tracking (5-hour or weekly limits).

## Data Source

Claude Code writes one JSONL file per session under
`~/.claude/projects/<project-dir>/<session-id>.jsonl`, plus a `subagents/`
subfolder per project containing per-subagent logs that must be excluded from
session selection.

Confirmed real line shape (pulled from this machine's own session log):

```json
"usage":{"input_tokens":2,"cache_creation_input_tokens":537,"cache_read_input_tokens":62887,"output_tokens":188,"server_tool_use":{"web_search_requests":0,"web_fetch_requests":0}}
```

and a `"model"` field on the same message, e.g. `"model":"claude-sonnet-5"`.

**Context usage estimate**: for the most recent assistant message with a
`usage` object,

```
current_context_tokens = input_tokens + cache_creation_input_tokens + cache_read_input_tokens
percent = current_context_tokens / context_window_limit(model)
```

`context_window_limit(model)` is a small static lookup table (e.g. 200k for
current Claude models), with a default of 200k for any unrecognized model
name (logged as a warning, once per session).

## Architecture

```
FileSystemWatcher (recursive, *.jsonl, ~/.claude/projects/)
        │  (debounced ~300ms)
        ▼
LocateActiveSession()  → newest top-level *.jsonl, skips subagents/
        │
        ▼
TailReader  → reads only new lines since last read position for that file;
              resets position to end-of-file (minus small lookback) when the
              active session changes, so switching sessions doesn't replay
              old history
        ▼
UsageParser  → last line with "usage" + "model" → percent
        │  raises UsageChanged(percent)
        ▼
MoodStateMachine.Update(percent) → raises MoodChanged(mood) only on actual
        │                          transitions (hysteresis at boundaries)
        ▼
PetWindow.SetMood(mood) → swaps active sprite animation loop
```

A backup poll every ~5s independently re-runs `LocateActiveSession` and a
full re-check, since `FileSystemWatcher` can silently drop events under
bursty writes.

## Components

1. **Usage Reader** — watches `~/.claude/projects/` recursively, identifies
   the active session file, tails new lines, parses usage + model, computes
   `percent`. Exposes `UsageChanged(percent)`.

2. **Mood State Engine** — pure function mapping `percent` → mood:
   - `Happy`: 0–40%
   - `Eating`: 40–75%
   - `Full`: 75–90%
   - `Stressed`: 90–100%
   - `NoSession`: no session file found yet
   Hysteresis prevents flapping when `percent` hovers near a boundary.

3. **Pet Window** — borderless, transparent, always-on-top WPF window
   (`WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`,
   `ShowInTaskbar=False`), click-through by default via a Win32
   `WS_EX_TRANSPARENT` style toggle. An `Image` control cycles a pixel-art
   sprite sheet per mood on a `DispatcherTimer`. Window position persists
   across restarts via a local JSON settings file.

4. **Tray Icon** — the only interactive surface, since the pet window is
   click-through by default:
   - Toggle drag-to-reposition (temporarily disables click-through)
   - Show exact token count / percent
   - Quit
   - Toggle "run at startup"

5. **App Shell** — no visible main window besides the pet overlay; wires
   Usage Reader → Mood State Engine → Pet Window on startup.

## Error Handling

All errors/warnings are written to a rolling debug log file at
`%LOCALAPPDATA%/ClaudePet/debug.log` (there's no console for a background
overlay app), per the project convention of surfacing real errors instead of
silently swallowing them.

- **No session files found yet**: pet shows `NoSession` mood instead of
  crashing or guessing.
- **Malformed/partial JSON line** (file being written mid-line as we read
  it): skip that line only, log it, retry on the next change event — don't
  fail the whole tail read.
- **Unknown model name**: fall back to the default 200k context limit, log a
  warning once per session (not spammed per line).
- **File locked/in-use during read**: catch `IOException`, retry with short
  backoff.
- **Settings file missing or corrupt**: fall back to defaults (bottom-right
  corner position, fresh read position) rather than failing startup.

## Testing

- **UsageParser**: unit tests against JSONL fixtures, including the real
  shape captured above, partial/truncated last lines, lines missing a
  `usage` field, and subagent files that must be excluded from session
  selection.
- **MoodStateMachine**: unit tests for threshold transitions and hysteresis.
- **Manual verification**: run against real `~/.claude/projects` logs on
  this machine to sanity-check computed percentages; visually confirm
  click-through, drag, and tray behavior on Windows 11.

## Open Assumptions

- `input_tokens + cache_creation_input_tokens + cache_read_input_tokens` on
  the latest assistant message is treated as a reasonable proxy for current
  context size. If Claude Code's caching behavior changes this relationship
  in the future, the formula may need revisiting.
- Context window limits per model are hardcoded rather than fetched from an
  API, since this must work fully offline.

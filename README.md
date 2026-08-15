# Claude Pet

A tiny pixel-art desktop pet for Windows that lives in your system tray and
reflects what your [Claude Code](https://code.claude.com) session is doing —
its mood tracks context-window usage, it perks up while Claude is actively
working, and it can show your real subscription usage right on the sprite.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4) ![WPF](https://img.shields.io/badge/UI-WPF%20%2B%20WinForms-blue)

## What it does

- **Mood tracking** — watches your most recent Claude Code session
  transcript (`~/.claude/projects/**/*.jsonl`) and reflects context-window
  usage as a mood: Happy → Eating → Full → Stressed, or a neutral idle look
  when nothing's active.
- **Working indicator** — a small accent pulses on the sprite while Claude
  Code is actively writing to the session transcript, and fades a few
  seconds after it goes quiet.
- **Worried indicator** — a second accent appears when usage crosses a
  threshold (subscription usage if you've opted in, session context usage
  otherwise) — an early warning before you hit a limit.
- **Subscription usage badge (unofficial)** — optionally polls Anthropic's
  undocumented `anthropic-ratelimit-unified-*` response headers via your
  existing Claude Code OAuth login and overlays your real 5-hour/weekly
  usage directly on the pet, plus a line in the tray tooltip. Off by
  default; see the design spec below for exactly what it does and why it's
  labeled unofficial.
- **Rate-limit tooltip** — if `ANTHROPIC_API_KEY` is set in your
  environment, shows API-key-based rate-limit usage in the tray tooltip.
- **Custom skins** — swap the built-in pixel art for your own. Drop a
  folder under `%LOCALAPPDATA%\ClaudePet\skins\<name>\` with a `skin.json`
  manifest and PNG or JSON pixel-grid frames (the two formats can be mixed
  within one skin), and pick it live from the tray's **Skin** submenu. A
  working example skin is generated on first run so there's always a valid
  starting point to copy.
- Draggable (toggle via tray menu), click-through when not being dragged,
  always-on-top, and optionally launches at Windows startup.

## Requirements

- Windows
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Claude Code](https://code.claude.com) installed and used at least once,
  for the pet to have session data to react to

Everything else (rate-limit tooltip, subscription usage badge) is optional
and opt-in.

## Build & run

```powershell
dotnet build src/ClaudePet/ClaudePet.csproj
dotnet run --project src/ClaudePet/ClaudePet.csproj
```

The app runs from the system tray — right-click the tray icon for options
(drag mode, run at startup, subscription usage, skin picker, quit).

## Distributing to someone else

Running from source requires the .NET 8 SDK. To hand someone a build that
needs **no prerequisites at all** — no .NET SDK or Runtime install — publish
a self-contained single-file executable:

```powershell
dotnet publish src/ClaudePet/ClaudePet.csproj -r win-x64 -c Release -o publish
```

This produces a single `publish\ClaudePet.exe` (~70MB, everything bundled
in) that runs standalone on any Windows machine. It's unsigned, so Windows
SmartScreen will likely warn on first run of a downloaded copy — that's
expected for an app without a code-signing certificate.

## Tests

```powershell
dotnet test tests/ClaudePet.Tests/ClaudePet.Tests.csproj
```

## Project layout

```
src/ClaudePet/
  Models/       Mood, usage/rate-limit snapshot types
  Services/     session tailing/parsing, mood state machine, usage readers
  Rendering/    procedural pixel-art generator (the built-in "Default" skin)
  Skins/        custom skin system - manifest parsing, PNG/JSON frame
                codecs, overlay compositing, skin discovery
  Settings/     persisted app settings (%LOCALAPPDATA%\ClaudePet\settings.json)
  Tray/         tray icon, context menu, tooltip formatting
  Native/       Win32 interop (click-through window styling, startup registration)
  PetWindow.*   the borderless always-on-top pet window
  App.xaml.cs   startup wiring

tests/ClaudePet.Tests/   xUnit tests for all of the above

docs/superpowers/specs/    design docs for each feature
docs/superpowers/plans/    implementation plans for each feature
```

The `docs/superpowers/` design specs and plans document each feature's
architecture and decisions in detail, including the subscription-usage
feature's unofficial/undocumented-API caveats.

## Data & privacy

Session data is read locally from your own `~/.claude/projects` directory
and never leaves your machine except for the two opt-in network features
(rate-limit tooltip via your own `ANTHROPIC_API_KEY`, and the unofficial
subscription-usage poll via your own Claude Code OAuth session) — both of
which only talk to Anthropic's API using credentials you already have.

## License

[MIT](LICENSE)

# Claude Pet Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows desktop pixel-art pet that overlays the desktop and changes mood/animation based on the active Claude Code session's context-window usage.

**Architecture:** A .NET 8 WPF app. A `UsageReader` watches `~/.claude/projects/` for the most-recently-modified session `*.jsonl` file, tails new lines, and parses each assistant message's `usage`/`model` fields into a context-window percentage. A `MoodStateMachine` maps that percentage (with hysteresis) to a `Mood`. A borderless, transparent, click-through `PetWindow` renders procedurally-generated pixel-art frames per mood via `WriteableBitmap`. A tray icon (`NotifyIcon`) is the only interactive surface, toggling drag mode and quit.

**Tech Stack:** .NET 8, WPF (`net8.0-windows`), Windows Forms interop (`NotifyIcon` only), xUnit, `System.Text.Json`.

## Global Constraints

- Windows only (per spec's non-goals — no cross-platform abstraction).
- Fully offline: context-window limits are a static lookup table, not fetched from any API.
- "Active session" = most-recently-modified top-level `*.jsonl` under `~/.claude/projects/`, excluding anything under a `subagents/` folder.
- Context tokens estimate = `input_tokens + cache_creation_input_tokens + cache_read_input_tokens` from the latest assistant message's `message.usage` object; default context limit is 200,000 for unrecognized models.
- All runtime errors/warnings are written to `%LOCALAPPDATA%/ClaudePet/debug.log`, never silently swallowed (per project convention — see spec's Error Handling section).
- Solution root is `C:\Users\wesle\Desktop\claude_pet`. Source in `src/ClaudePet/`, tests in `tests/ClaudePet.Tests/`.

---

## Task 1: Solution & Test Harness Scaffolding

**Files:**
- Create: `ClaudePet.sln`
- Create: `src/ClaudePet/ClaudePet.csproj`
- Create: `tests/ClaudePet.Tests/ClaudePet.Tests.csproj`
- Modify: `tests/ClaudePet.Tests/UnitTest1.cs` → rename/replace with `tests/ClaudePet.Tests/SmokeTests.cs`

**Interfaces:**
- Produces: a solution where `src/ClaudePet` (WPF, `net8.0-windows`, `UseWPF=true`, `UseWindowsForms=true`) is referenced by `tests/ClaudePet.Tests` (xUnit, `net8.0-windows`).

- [ ] **Step 1: Create the solution and projects**

Run from `C:\Users\wesle\Desktop\claude_pet`:

```bash
dotnet new sln -n ClaudePet
dotnet new wpf -o src/ClaudePet -n ClaudePet --framework net8.0
dotnet new xunit -o tests/ClaudePet.Tests -n ClaudePet.Tests --framework net8.0
dotnet sln add src/ClaudePet/ClaudePet.csproj tests/ClaudePet.Tests/ClaudePet.Tests.csproj
```

- [ ] **Step 2: Enable Windows Forms interop in the app project (needed later for the tray icon)**

Edit `src/ClaudePet/ClaudePet.csproj` so the `PropertyGroup` reads:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>

</Project>
```

- [ ] **Step 3: Retarget the test project to `net8.0-windows` and add the project reference**

The xUnit template only accepts `--framework net8.0`, but it must match the app project's TFM (`net8.0-windows`) before it can reference it. Edit `tests/ClaudePet.Tests/ClaudePet.Tests.csproj`, changing:

```xml
<TargetFramework>net8.0</TargetFramework>
```

to:

```xml
<TargetFramework>net8.0-windows</TargetFramework>
```

Then run:

```bash
dotnet add tests/ClaudePet.Tests/ClaudePet.Tests.csproj reference src/ClaudePet/ClaudePet.csproj
```

- [ ] **Step 4: Replace the template test with a real smoke test**

Delete `tests/ClaudePet.Tests/UnitTest1.cs` and create `tests/ClaudePet.Tests/SmokeTests.cs`:

```csharp
namespace ClaudePet.Tests;

public class SmokeTests
{
    [Fact]
    public void TestProjectCanReferenceAppProject()
    {
        var mood = ClaudePet.Models.Mood.Happy;
        Assert.Equal(ClaudePet.Models.Mood.Happy, mood);
    }
}
```

(This will not compile yet — `Mood` doesn't exist. That's expected; Task 2 creates it.)

- [ ] **Step 5: Create the `Mood` enum so the smoke test compiles**

Create `src/ClaudePet/Models/Mood.cs`:

```csharp
namespace ClaudePet.Models;

public enum Mood
{
    NoSession,
    Happy,
    Eating,
    Full,
    Stressed
}
```

- [ ] **Step 6: Build and run tests**

```bash
dotnet build
dotnet test
```

Expected: build succeeds, `SmokeTests.TestProjectCanReferenceAppProject` passes (1 total, 1 passed).

- [ ] **Step 7: Commit**

```bash
git add ClaudePet.sln src/ClaudePet tests/ClaudePet.Tests
git commit -m "chore: scaffold ClaudePet WPF solution and test project"
```

---

## Task 2: UsageParser

**Files:**
- Create: `src/ClaudePet/Models/UsageSnapshot.cs`
- Create: `src/ClaudePet/Services/UsageParser.cs`
- Test: `tests/ClaudePet.Tests/UsageParserTests.cs`

**Interfaces:**
- Consumes: `Mood` from Task 1 (not directly, but same `Models` namespace).
- Produces: `UsageSnapshot(int ContextTokens, int ContextLimit, double Percent)` and `static class UsageParser` with `UsageSnapshot? TryParseLine(string line, Action<string>? onUnknownModel = null)` and `UsageSnapshot? ParseLatest(IEnumerable<string> lines, Action<string>? onUnknownModel = null)`. Both consumed by `UsageReader` in Task 8.

Real log line shape (captured from an actual Claude Code session on this machine — top-level `type` is `"assistant"`, `model` and `usage` are nested under `message`):

```json
{"type":"assistant","message":{"model":"claude-sonnet-5","usage":{"input_tokens":2,"cache_creation_input_tokens":19271,"cache_read_input_tokens":26677,"output_tokens":874}}}
```

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudePet.Tests/UsageParserTests.cs`:

```csharp
using ClaudePet.Services;

namespace ClaudePet.Tests;

public class UsageParserTests
{
    private const string ValidLine =
        """{"type":"assistant","message":{"model":"claude-sonnet-5","usage":{"input_tokens":2,"cache_creation_input_tokens":19271,"cache_read_input_tokens":26677,"output_tokens":874}}}""";

    [Fact]
    public void TryParseLine_ValidAssistantLine_ComputesContextTokens()
    {
        var result = UsageParser.TryParseLine(ValidLine);

        Assert.NotNull(result);
        Assert.Equal(2 + 19271 + 26677, result!.ContextTokens);
    }

    [Fact]
    public void TryParseLine_ValidAssistantLine_ComputesPercentOfKnownModelLimit()
    {
        var result = UsageParser.TryParseLine(ValidLine);

        Assert.NotNull(result);
        Assert.Equal(200_000, result!.ContextLimit);
        Assert.Equal((2 + 19271 + 26677) / 200_000.0 * 100.0, result.Percent, precision: 6);
    }

    [Fact]
    public void TryParseLine_UnrecognizedModel_FallsBackToDefaultLimitAndReportsIt()
    {
        const string line = """{"type":"assistant","message":{"model":"claude-future-9","usage":{"input_tokens":1,"cache_creation_input_tokens":0,"cache_read_input_tokens":0,"output_tokens":1}}}""";
        string? reportedModel = null;

        var result = UsageParser.TryParseLine(line, model => reportedModel = model);

        Assert.NotNull(result);
        Assert.Equal(200_000, result!.ContextLimit);
        Assert.Equal("claude-future-9", reportedModel);
    }

    [Fact]
    public void TryParseLine_MissingUsageObject_ReturnsNull()
    {
        const string line = """{"type":"user","message":{"role":"user","content":"hi"}}""";

        var result = UsageParser.TryParseLine(line);

        Assert.Null(result);
    }

    [Fact]
    public void TryParseLine_MalformedJson_ReturnsNull()
    {
        const string line = """{"type":"assistant","message":{"usage":{"input_tokens":""";

        var result = UsageParser.TryParseLine(line);

        Assert.Null(result);
    }

    [Fact]
    public void TryParseLine_BlankLine_ReturnsNull()
    {
        Assert.Null(UsageParser.TryParseLine(""));
        Assert.Null(UsageParser.TryParseLine("   "));
    }

    [Fact]
    public void ParseLatest_MultipleLines_ReturnsLastValidOne()
    {
        const string first = """{"type":"assistant","message":{"model":"claude-sonnet-5","usage":{"input_tokens":1,"cache_creation_input_tokens":0,"cache_read_input_tokens":1000,"output_tokens":1}}}""";
        const string second = """{"type":"assistant","message":{"model":"claude-sonnet-5","usage":{"input_tokens":1,"cache_creation_input_tokens":0,"cache_read_input_tokens":5000,"output_tokens":1}}}""";

        var result = UsageParser.ParseLatest(new[] { first, "not json", second });

        Assert.NotNull(result);
        Assert.Equal(5001, result!.ContextTokens);
    }

    [Fact]
    public void ParseLatest_NoValidLines_ReturnsNull()
    {
        var result = UsageParser.ParseLatest(new[] { "", "not json", """{"type":"user"}""" });

        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter UsageParserTests
```

Expected: compile error — `UsageSnapshot` and `UsageParser` don't exist yet.

- [ ] **Step 3: Implement `UsageSnapshot`**

Create `src/ClaudePet/Models/UsageSnapshot.cs`:

```csharp
namespace ClaudePet.Models;

public sealed record UsageSnapshot(int ContextTokens, int ContextLimit, double Percent);
```

- [ ] **Step 4: Implement `UsageParser`**

Create `src/ClaudePet/Services/UsageParser.cs`:

```csharp
using System.Text.Json;
using ClaudePet.Models;

namespace ClaudePet.Services;

public static class UsageParser
{
    private const int DefaultContextLimit = 200_000;

    private static readonly Dictionary<string, int> ContextLimits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-4-8"] = 200_000,
        ["claude-sonnet-5"] = 200_000,
        ["claude-haiku-4-5"] = 200_000,
    };

    public static UsageSnapshot? TryParseLine(string line, Action<string>? onUnknownModel = null)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("message", out var message))
                return null;
            if (!message.TryGetProperty("usage", out var usage))
                return null;

            int inputTokens = GetInt(usage, "input_tokens");
            int cacheCreation = GetInt(usage, "cache_creation_input_tokens");
            int cacheRead = GetInt(usage, "cache_read_input_tokens");
            int contextTokens = inputTokens + cacheCreation + cacheRead;

            string? model = message.TryGetProperty("model", out var m) ? m.GetString() : null;

            int limit = DefaultContextLimit;
            if (model is not null)
            {
                if (ContextLimits.TryGetValue(model, out var knownLimit))
                    limit = knownLimit;
                else
                    onUnknownModel?.Invoke(model);
            }

            double percent = limit == 0 ? 0 : Math.Clamp(contextTokens / (double)limit * 100.0, 0, 100);

            return new UsageSnapshot(contextTokens, limit, percent);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static UsageSnapshot? ParseLatest(IEnumerable<string> lines, Action<string>? onUnknownModel = null)
    {
        UsageSnapshot? latest = null;
        foreach (var line in lines)
        {
            var parsed = TryParseLine(line, onUnknownModel);
            if (parsed is not null)
                latest = parsed;
        }
        return latest;
    }

    private static int GetInt(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out var value) ? value.GetInt32() : 0;
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test --filter UsageParserTests
```

Expected: all 8 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/ClaudePet/Models/UsageSnapshot.cs src/ClaudePet/Services/UsageParser.cs tests/ClaudePet.Tests/UsageParserTests.cs
git commit -m "feat: parse context-window usage from Claude Code session lines"
```

---

## Task 3: SessionLocator

**Files:**
- Create: `src/ClaudePet/Services/SessionLocator.cs`
- Test: `tests/ClaudePet.Tests/SessionLocatorTests.cs`

**Interfaces:**
- Produces: `static class SessionLocator` with `string? FindActiveSessionFile(string projectsRoot)`. Consumed by `UsageReader` in Task 8.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudePet.Tests/SessionLocatorTests.cs`:

```csharp
using ClaudePet.Services;

namespace ClaudePet.Tests;

public class SessionLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ClaudePetTests_" + Guid.NewGuid());

    public SessionLocatorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string WriteFile(string relativePath, DateTime lastWriteUtc)
    {
        var fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "{}");
        File.SetLastWriteTimeUtc(fullPath, lastWriteUtc);
        return fullPath;
    }

    [Fact]
    public void FindActiveSessionFile_NoDirectory_ReturnsNull()
    {
        var result = SessionLocator.FindActiveSessionFile(Path.Combine(_root, "does-not-exist"));
        Assert.Null(result);
    }

    [Fact]
    public void FindActiveSessionFile_NoJsonlFiles_ReturnsNull()
    {
        Directory.CreateDirectory(Path.Combine(_root, "proj"));
        Assert.Null(SessionLocator.FindActiveSessionFile(_root));
    }

    [Fact]
    public void FindActiveSessionFile_PicksMostRecentlyModified()
    {
        WriteFile(@"projA\older.jsonl", DateTime.UtcNow.AddMinutes(-10));
        var newer = WriteFile(@"projB\newer.jsonl", DateTime.UtcNow);

        var result = SessionLocator.FindActiveSessionFile(_root);

        Assert.Equal(newer, result);
    }

    [Fact]
    public void FindActiveSessionFile_ExcludesFilesUnderSubagentsFolder()
    {
        WriteFile(@"proj\subagents\agent-1.jsonl", DateTime.UtcNow);
        var topLevel = WriteFile(@"proj\session.jsonl", DateTime.UtcNow.AddMinutes(-5));

        var result = SessionLocator.FindActiveSessionFile(_root);

        Assert.Equal(topLevel, result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter SessionLocatorTests
```

Expected: compile error — `SessionLocator` doesn't exist yet.

- [ ] **Step 3: Implement `SessionLocator`**

Create `src/ClaudePet/Services/SessionLocator.cs`:

```csharp
namespace ClaudePet.Services;

public static class SessionLocator
{
    public static string? FindActiveSessionFile(string projectsRoot)
    {
        if (!Directory.Exists(projectsRoot))
            return null;

        return Directory.EnumerateFiles(projectsRoot, "*.jsonl", SearchOption.AllDirectories)
            .Where(path => !IsUnderSubagentsFolder(path))
            .Select(path => new FileInfo(path))
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .Select(fi => fi.FullName)
            .FirstOrDefault();
    }

    private static bool IsUnderSubagentsFolder(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        return dir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                  .Contains("subagents", StringComparer.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test --filter SessionLocatorTests
```

Expected: all 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/ClaudePet/Services/SessionLocator.cs tests/ClaudePet.Tests/SessionLocatorTests.cs
git commit -m "feat: locate the most recently active Claude Code session file"
```

---

## Task 4: TailReader

**Files:**
- Create: `src/ClaudePet/Services/TailReader.cs`
- Test: `tests/ClaudePet.Tests/TailReaderTests.cs`

**Interfaces:**
- Produces: `sealed class TailReader` with constructor `TailReader(int initialLookbackBytes = 65536)` and method `IReadOnlyList<string> ReadNewLines(string path)`. Consumed by `UsageReader` in Task 8.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudePet.Tests/TailReaderTests.cs`:

```csharp
using ClaudePet.Services;

namespace ClaudePet.Tests;

public class TailReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ClaudePetTailTests_" + Guid.NewGuid());

    public TailReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string NewFile(string content)
    {
        var path = Path.Combine(_dir, Guid.NewGuid() + ".jsonl");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ReadNewLines_FreshFileSmallerThanLookback_ReturnsAllLines()
    {
        var path = NewFile("line1\nline2\n");
        var reader = new TailReader(initialLookbackBytes: 65536);

        var lines = reader.ReadNewLines(path);

        Assert.Equal(new[] { "line1", "line2" }, lines);
    }

    [Fact]
    public void ReadNewLines_CalledAgainWithNoNewContent_ReturnsEmpty()
    {
        var path = NewFile("line1\n");
        var reader = new TailReader(initialLookbackBytes: 65536);
        reader.ReadNewLines(path);

        var lines = reader.ReadNewLines(path);

        Assert.Empty(lines);
    }

    [Fact]
    public void ReadNewLines_AfterAppend_ReturnsOnlyNewLines()
    {
        var path = NewFile("line1\n");
        var reader = new TailReader(initialLookbackBytes: 65536);
        reader.ReadNewLines(path);

        File.AppendAllText(path, "line2\nline3\n");
        var lines = reader.ReadNewLines(path);

        Assert.Equal(new[] { "line2", "line3" }, lines);
    }

    [Fact]
    public void ReadNewLines_SwitchingToDifferentFile_AppliesLookbackFromNewFile()
    {
        var pathA = NewFile("a-line1\n");
        var pathB = NewFile("b-line1\nb-line2\n");
        var reader = new TailReader(initialLookbackBytes: 65536);
        reader.ReadNewLines(pathA);

        var lines = reader.ReadNewLines(pathB);

        Assert.Equal(new[] { "b-line1", "b-line2" }, lines);
    }

    [Fact]
    public void ReadNewLines_SwitchingToFileLargerThanLookback_DropsPartialFirstLine()
    {
        // "AAAA\nBBBB\nCCCC\n" is 15 bytes; a lookback of 6 starts mid "BBBB\n",
        // so the (partial) first captured line must be dropped.
        var path = NewFile("AAAA\nBBBB\nCCCC\n");
        var reader = new TailReader(initialLookbackBytes: 6);

        var lines = reader.ReadNewLines(path);

        Assert.Equal(new[] { "CCCC" }, lines);
    }

    [Fact]
    public void ReadNewLines_FileTruncated_RestartsFromBeginning()
    {
        var path = NewFile("this-is-a-long-first-line\n");
        var reader = new TailReader(initialLookbackBytes: 65536);
        reader.ReadNewLines(path);

        File.WriteAllText(path, "short\n");
        var lines = reader.ReadNewLines(path);

        Assert.Equal(new[] { "short" }, lines);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter TailReaderTests
```

Expected: compile error — `TailReader` doesn't exist yet.

- [ ] **Step 3: Implement `TailReader`**

Create `src/ClaudePet/Services/TailReader.cs`:

```csharp
namespace ClaudePet.Services;

public sealed class TailReader
{
    private readonly int _initialLookbackBytes;
    private string? _currentPath;
    private long _position;

    public TailReader(int initialLookbackBytes = 65536)
    {
        _initialLookbackBytes = initialLookbackBytes;
    }

    public IReadOnlyList<string> ReadNewLines(string path)
    {
        bool isNewFile = !string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        bool startedMidFile = false;

        if (isNewFile)
        {
            _currentPath = path;
            var start = Math.Max(0, stream.Length - _initialLookbackBytes);
            startedMidFile = start > 0;
            _position = start;
        }
        else if (stream.Length < _position)
        {
            _position = 0;
        }

        stream.Seek(_position, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        _position = stream.Position;

        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                         .Select(l => l.TrimEnd('\r'))
                         .ToArray();

        return startedMidFile && lines.Length > 1 ? lines.Skip(1).ToArray() : lines;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test --filter TailReaderTests
```

Expected: all 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/ClaudePet/Services/TailReader.cs tests/ClaudePet.Tests/TailReaderTests.cs
git commit -m "feat: tail-read only new lines from the active session file"
```

---

## Task 5: MoodStateMachine

**Files:**
- Create: `src/ClaudePet/Services/MoodStateMachine.cs`
- Test: `tests/ClaudePet.Tests/MoodStateMachineTests.cs`

**Interfaces:**
- Consumes: `Mood` (Task 1), `UsageSnapshot` (Task 2).
- Produces: `sealed class MoodStateMachine` with property `Mood Current` and method `Mood Update(UsageSnapshot? snapshot)`. Consumed by `App.xaml.cs` in Task 13.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudePet.Tests/MoodStateMachineTests.cs`:

```csharp
using ClaudePet.Models;
using ClaudePet.Services;

namespace ClaudePet.Tests;

public class MoodStateMachineTests
{
    private static UsageSnapshot AtPercent(double percent) => new(0, 200_000, percent);

    [Fact]
    public void Update_NullSnapshot_ReturnsNoSession()
    {
        var sm = new MoodStateMachine();

        var mood = sm.Update(null);

        Assert.Equal(Mood.NoSession, mood);
    }

    [Theory]
    [InlineData(0, Mood.Happy)]
    [InlineData(39, Mood.Happy)]
    [InlineData(40, Mood.Eating)]
    [InlineData(74, Mood.Eating)]
    [InlineData(75, Mood.Full)]
    [InlineData(89, Mood.Full)]
    [InlineData(90, Mood.Stressed)]
    [InlineData(100, Mood.Stressed)]
    public void Update_FromFreshState_MapsPercentToExpectedMood(double percent, Mood expected)
    {
        var sm = new MoodStateMachine();

        var mood = sm.Update(AtPercent(percent));

        Assert.Equal(expected, mood);
    }

    [Fact]
    public void Update_HoveringNearEnterThreshold_DoesNotFlipFlop()
    {
        var sm = new MoodStateMachine();
        sm.Update(AtPercent(38)); // Happy
        sm.Update(AtPercent(41)); // crosses into Eating (enter at 40)

        var mood = sm.Update(AtPercent(37)); // dips just below 40 again

        // Exit-Eating threshold is 35, so 37 should NOT flip back to Happy yet.
        Assert.Equal(Mood.Eating, mood);
    }

    [Fact]
    public void Update_DropsBelowExitThreshold_ReturnsToHappy()
    {
        var sm = new MoodStateMachine();
        sm.Update(AtPercent(50)); // Eating

        var mood = sm.Update(AtPercent(30)); // below exit-Eating threshold (35)

        Assert.Equal(Mood.Happy, mood);
    }

    [Fact]
    public void Update_RisingThenFalling_PassesThroughFullBeforeHappy()
    {
        var sm = new MoodStateMachine();
        sm.Update(AtPercent(95)); // Stressed

        var afterDrop = sm.Update(AtPercent(80)); // below exit-Stressed (85), above enter-Full (75)

        Assert.Equal(Mood.Full, afterDrop);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter MoodStateMachineTests
```

Expected: compile error — `MoodStateMachine` doesn't exist yet.

- [ ] **Step 3: Implement `MoodStateMachine`**

Create `src/ClaudePet/Services/MoodStateMachine.cs`:

```csharp
using ClaudePet.Models;

namespace ClaudePet.Services;

public sealed class MoodStateMachine
{
    private const double EnterEatingAt = 40.0;
    private const double ExitEatingBelow = 35.0;
    private const double EnterFullAt = 75.0;
    private const double ExitFullBelow = 70.0;
    private const double EnterStressedAt = 90.0;
    private const double ExitStressedBelow = 85.0;

    public Mood Current { get; private set; } = Mood.NoSession;

    public Mood Update(UsageSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            Current = Mood.NoSession;
            return Current;
        }

        var percent = snapshot.Percent;

        Current = Current switch
        {
            Mood.NoSession or Mood.Happy => RisingMood(percent, floor: Mood.Happy),
            Mood.Eating => percent >= EnterStressedAt ? Mood.Stressed
                          : percent >= EnterFullAt ? Mood.Full
                          : percent < ExitEatingBelow ? Mood.Happy
                          : Mood.Eating,
            Mood.Full => percent >= EnterStressedAt ? Mood.Stressed
                        : percent < ExitFullBelow ? RisingMood(percent, floor: Mood.Happy)
                        : Mood.Full,
            Mood.Stressed => percent < ExitStressedBelow ? RisingMood(percent, floor: Mood.Happy)
                            : Mood.Stressed,
            _ => Mood.Happy
        };

        return Current;
    }

    private static Mood RisingMood(double percent, Mood floor) =>
        percent >= EnterStressedAt ? Mood.Stressed
        : percent >= EnterFullAt ? Mood.Full
        : percent >= EnterEatingAt ? Mood.Eating
        : floor;
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test --filter MoodStateMachineTests
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/ClaudePet/Services/MoodStateMachine.cs tests/ClaudePet.Tests/MoodStateMachineTests.cs
git commit -m "feat: map context-usage percent to pet mood with hysteresis"
```

---

## Task 6: SettingsStore

**Files:**
- Create: `src/ClaudePet/Settings/AppSettings.cs`
- Create: `src/ClaudePet/Settings/SettingsStore.cs`
- Test: `tests/ClaudePet.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `sealed record AppSettings { double WindowLeft = -1; double WindowTop = -1; bool RunAtStartup = false; }` and `sealed class SettingsStore` with constructor `SettingsStore(string filePath, Action<string>? onError = null)`, methods `AppSettings Load()` and `void Save(AppSettings settings)`. Consumed by `PetWindow` (Task 11), `TrayIconManager` (Task 12), `App.xaml.cs` (Task 13).

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudePet.Tests/SettingsStoreTests.cs`:

```csharp
using ClaudePet.Settings;

namespace ClaudePet.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ClaudePetSettingsTests_" + Guid.NewGuid());

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string FilePath => Path.Combine(_dir, "nested", "settings.json");

    [Fact]
    public void Load_FileDoesNotExist_ReturnsDefaults()
    {
        var store = new SettingsStore(FilePath);

        var settings = store.Load();

        Assert.Equal(-1, settings.WindowLeft);
        Assert.Equal(-1, settings.WindowTop);
        Assert.False(settings.RunAtStartup);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        var store = new SettingsStore(FilePath);
        var original = new AppSettings { WindowLeft = 100, WindowTop = 200, RunAtStartup = true };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void Save_CreatesParentDirectoryIfMissing()
    {
        var store = new SettingsStore(FilePath);

        store.Save(new AppSettings());

        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsAndReportsError()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ not valid json ");
        string? reportedError = null;
        var store = new SettingsStore(FilePath, err => reportedError = err);

        var settings = store.Load();

        Assert.Equal(-1, settings.WindowLeft);
        Assert.NotNull(reportedError);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter SettingsStoreTests
```

Expected: compile error — `AppSettings`/`SettingsStore` don't exist yet.

- [ ] **Step 3: Implement `AppSettings`**

Create `src/ClaudePet/Settings/AppSettings.cs`:

```csharp
namespace ClaudePet.Settings;

public sealed record AppSettings
{
    public double WindowLeft { get; init; } = -1;
    public double WindowTop { get; init; } = -1;
    public bool RunAtStartup { get; init; }
}
```

- [ ] **Step 4: Implement `SettingsStore`**

Create `src/ClaudePet/Settings/SettingsStore.cs`:

```csharp
using System.Text.Json;

namespace ClaudePet.Settings;

public sealed class SettingsStore
{
    private readonly string _filePath;
    private readonly Action<string>? _onError;

    public SettingsStore(string filePath, Action<string>? onError = null)
    {
        _filePath = filePath;
        _onError = onError;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new AppSettings();

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _onError?.Invoke($"Failed to load settings from {_filePath}: {ex.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (IOException ex)
        {
            _onError?.Invoke($"Failed to save settings to {_filePath}: {ex.Message}");
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test --filter SettingsStoreTests
```

Expected: all 4 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/ClaudePet/Settings tests/ClaudePet.Tests/SettingsStoreTests.cs
git commit -m "feat: persist window position and startup preference to local settings"
```

---

## Task 7: DebugLog

**Files:**
- Create: `src/ClaudePet/Logging/DebugLog.cs`
- Test: `tests/ClaudePet.Tests/DebugLogTests.cs`

**Interfaces:**
- Produces: `sealed class DebugLog` with constructor `DebugLog(string filePath)` and method `void Write(string message)`. Consumed by `SettingsStore` callers, `UsageReader` (Task 8), `App.xaml.cs` (Task 13).

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudePet.Tests/DebugLogTests.cs`:

```csharp
using ClaudePet.Logging;

namespace ClaudePet.Tests;

public class DebugLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ClaudePetLogTests_" + Guid.NewGuid());

    public DebugLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string FilePath => Path.Combine(_dir, "nested", "debug.log");

    [Fact]
    public void Constructor_CreatesParentDirectory()
    {
        _ = new DebugLog(FilePath);

        Assert.True(Directory.Exists(Path.GetDirectoryName(FilePath)));
    }

    [Fact]
    public void Write_AppendsMessageWithTimestamp()
    {
        var log = new DebugLog(FilePath);

        log.Write("something went wrong");

        var content = File.ReadAllText(FilePath);
        Assert.Contains("something went wrong", content);
    }

    [Fact]
    public void Write_CalledTwice_AppendsBothLinesWithoutTruncating()
    {
        var log = new DebugLog(FilePath);

        log.Write("first");
        log.Write("second");

        var lines = File.ReadAllLines(FilePath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("first", lines[0]);
        Assert.Contains("second", lines[1]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter DebugLogTests
```

Expected: compile error — `DebugLog` doesn't exist yet.

- [ ] **Step 3: Implement `DebugLog`**

Create `src/ClaudePet/Logging/DebugLog.cs`:

```csharp
namespace ClaudePet.Logging;

public sealed class DebugLog
{
    private readonly string _filePath;
    private readonly object _lock = new();

    public DebugLog(string filePath)
    {
        _filePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public void Write(string message)
    {
        var line = $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}";
        lock (_lock)
        {
            File.AppendAllText(_filePath, line);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test --filter DebugLogTests
```

Expected: all 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/ClaudePet/Logging tests/ClaudePet.Tests/DebugLogTests.cs
git commit -m "feat: add append-only debug log for background-process diagnostics"
```

---

## Task 8: PixelArtGenerator

**Files:**
- Create: `src/ClaudePet/Rendering/PixelFrame.cs`
- Create: `src/ClaudePet/Rendering/PixelArtGenerator.cs`
- Test: `tests/ClaudePet.Tests/PixelArtGeneratorTests.cs`

**Interfaces:**
- Consumes: `Mood` (Task 1).
- Produces: `sealed record PixelFrame(int Width, int Height, uint[] Pixels)` (ARGB, row-major, indexer `this[int x, int y]`) and `static class PixelArtGenerator` with `IReadOnlyList<PixelFrame> GenerateFrames(Mood mood)` — always returns exactly 2 frames (bounce animation), each 16×16. Consumed by `PetWindow` in Task 11.

This is deliberately simple procedural pixel art (a colored blob with two eyes) rather than hand-authored sprite sheets, so the whole rendering pipeline needs no external asset files and stays UI-framework-agnostic and unit-testable. Swap in real art later by changing only this file.

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudePet.Tests/PixelArtGeneratorTests.cs`:

```csharp
using ClaudePet.Models;
using ClaudePet.Rendering;

namespace ClaudePet.Tests;

public class PixelArtGeneratorTests
{
    [Fact]
    public void GenerateFrames_ReturnsExactlyTwoFrames()
    {
        var frames = PixelArtGenerator.GenerateFrames(Mood.Happy);

        Assert.Equal(2, frames.Count);
    }

    [Theory]
    [InlineData(Mood.Happy)]
    [InlineData(Mood.Eating)]
    [InlineData(Mood.Full)]
    [InlineData(Mood.Stressed)]
    [InlineData(Mood.NoSession)]
    public void GenerateFrames_AllFramesAre16x16(Mood mood)
    {
        foreach (var frame in PixelArtGenerator.GenerateFrames(mood))
        {
            Assert.Equal(16, frame.Width);
            Assert.Equal(16, frame.Height);
        }
    }

    [Theory]
    [InlineData(Mood.Happy)]
    [InlineData(Mood.Eating)]
    [InlineData(Mood.Full)]
    [InlineData(Mood.Stressed)]
    [InlineData(Mood.NoSession)]
    public void GenerateFrames_CenterPixelIsOpaqueBodyColor(Mood mood)
    {
        var frame = PixelArtGenerator.GenerateFrames(mood)[0];

        var centerPixel = frame[8, 8];

        Assert.Equal(0xFFu, centerPixel >> 24); // fully opaque alpha byte
    }

    [Fact]
    public void GenerateFrames_DifferentMoodsHaveDifferentBodyColors()
    {
        var happy = PixelArtGenerator.GenerateFrames(Mood.Happy)[0][8, 8];
        var stressed = PixelArtGenerator.GenerateFrames(Mood.Stressed)[0][8, 8];

        Assert.NotEqual(happy, stressed);
    }

    [Fact]
    public void GenerateFrames_SecondFrameIsSquishedRelativeToFirst()
    {
        var frames = PixelArtGenerator.GenerateFrames(Mood.Happy);

        // Row 2 (y=2) is part of the body in frame 0 (top starts at 2) but
        // transparent in frame 1 (top starts at 3, the "squish" frame).
        var topRowFrame0 = frames[0][8, 2];
        var topRowFrame1 = frames[1][8, 2];

        Assert.NotEqual(0u, topRowFrame0 >> 24);
        Assert.Equal(0u, topRowFrame1 >> 24);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter PixelArtGeneratorTests
```

Expected: compile error — `PixelFrame`/`PixelArtGenerator` don't exist yet.

- [ ] **Step 3: Implement `PixelFrame`**

Create `src/ClaudePet/Rendering/PixelFrame.cs`:

```csharp
namespace ClaudePet.Rendering;

public sealed record PixelFrame(int Width, int Height, uint[] Pixels)
{
    public uint this[int x, int y] => Pixels[y * Width + x];
}
```

- [ ] **Step 4: Implement `PixelArtGenerator`**

Create `src/ClaudePet/Rendering/PixelArtGenerator.cs`:

```csharp
using ClaudePet.Models;

namespace ClaudePet.Rendering;

public static class PixelArtGenerator
{
    private const int Size = 16;
    private const uint Transparent = 0x00000000;
    private const uint EyeColor = 0xFF212121;

    public static IReadOnlyList<PixelFrame> GenerateFrames(Mood mood)
    {
        var bodyColor = BodyColor(mood);
        return new[]
        {
            GenerateFrame(bodyColor, mood, squish: false),
            GenerateFrame(bodyColor, mood, squish: true)
        };
    }

    private static uint BodyColor(Mood mood) => mood switch
    {
        Mood.Happy => 0xFF4CAF50,
        Mood.Eating => 0xFFFFC107,
        Mood.Full => 0xFFFF7043,
        Mood.Stressed => 0xFFE53935,
        Mood.NoSession => 0xFF9E9E9E,
        _ => 0xFF9E9E9E
    };

    private static PixelFrame GenerateFrame(uint bodyColor, Mood mood, bool squish)
    {
        var pixels = new uint[Size * Size];
        Array.Fill(pixels, Transparent);

        int top = squish ? 3 : 2;
        int bottom = Size - 2;
        int left = 2;
        int right = Size - 3;

        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
                pixels[y * Size + x] = bodyColor;

        int eyeY = top + 3;
        if (mood == Mood.NoSession)
        {
            pixels[eyeY * Size + 5] = EyeColor;
            pixels[eyeY * Size + 6] = EyeColor;
            pixels[eyeY * Size + 9] = EyeColor;
            pixels[eyeY * Size + 10] = EyeColor;
        }
        else if (mood == Mood.Stressed)
        {
            pixels[eyeY * Size + 5] = EyeColor;
            pixels[(eyeY + 1) * Size + 6] = EyeColor;
            pixels[(eyeY + 1) * Size + 9] = EyeColor;
            pixels[eyeY * Size + 10] = EyeColor;
        }
        else
        {
            pixels[eyeY * Size + 5] = EyeColor;
            pixels[eyeY * Size + 10] = EyeColor;
        }

        return new PixelFrame(Size, Size, pixels);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test --filter PixelArtGeneratorTests
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/ClaudePet/Rendering tests/ClaudePet.Tests/PixelArtGeneratorTests.cs
git commit -m "feat: generate procedural pixel-art frames per mood"
```

---

## Task 9: UsageReader

**Files:**
- Create: `src/ClaudePet/Services/UsageReader.cs`

**Interfaces:**
- Consumes: `SessionLocator.FindActiveSessionFile` (Task 3), `TailReader` (Task 4), `UsageParser.ParseLatest` (Task 2), `DebugLog` (Task 7).
- Produces: `sealed class UsageReader : IDisposable` with constructor `UsageReader(string projectsRoot, DebugLog log)`, method `void Start()`, and `event Action<UsageSnapshot?>? UsageChanged`. Consumed by `App.xaml.cs` in Task 13.

This class wires a real `FileSystemWatcher` and `System.Timers.Timer` together, so it is verified manually rather than with xUnit (a watcher/timer integration test would be flaky and slow). The manual verification step below is required before moving on.

- [ ] **Step 1: Implement `UsageReader`**

Create `src/ClaudePet/Services/UsageReader.cs`:

```csharp
using ClaudePet.Logging;
using ClaudePet.Models;

namespace ClaudePet.Services;

public sealed class UsageReader : IDisposable
{
    private readonly string _projectsRoot;
    private readonly TailReader _tailReader = new();
    private readonly DebugLog _log;
    private readonly System.Timers.Timer _pollTimer;
    private readonly FileSystemWatcher? _watcher;
    private readonly object _refreshLock = new();
    private string? _lastWarnedModel;

    public event Action<UsageSnapshot?>? UsageChanged;

    public UsageReader(string projectsRoot, DebugLog log)
    {
        _projectsRoot = projectsRoot;
        _log = log;

        _pollTimer = new System.Timers.Timer(5000) { AutoReset = true };
        _pollTimer.Elapsed += (_, _) => Refresh();

        if (Directory.Exists(_projectsRoot))
        {
            _watcher = new FileSystemWatcher(_projectsRoot)
            {
                IncludeSubdirectories = true,
                Filter = "*.jsonl",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
            };
            _watcher.Changed += (_, _) => Refresh();
            _watcher.Created += (_, _) => Refresh();
            _watcher.EnableRaisingEvents = true;
        }
        else
        {
            _log.Write($"Projects root does not exist yet: {_projectsRoot}");
        }
    }

    public void Start()
    {
        Refresh();
        _pollTimer.Start();
    }

    private void Refresh()
    {
        lock (_refreshLock)
        {
            try
            {
                var activePath = SessionLocator.FindActiveSessionFile(_projectsRoot);
                if (activePath is null)
                {
                    UsageChanged?.Invoke(null);
                    return;
                }

                var lines = _tailReader.ReadNewLines(activePath);
                if (lines.Count == 0)
                    return;

                var snapshot = UsageParser.ParseLatest(lines, model =>
                {
                    if (_lastWarnedModel != model)
                    {
                        _log.Write($"Unknown model '{model}', falling back to default context limit.");
                        _lastWarnedModel = model;
                    }
                });

                if (snapshot is not null)
                    UsageChanged?.Invoke(snapshot);
            }
            catch (IOException ex)
            {
                _log.Write($"UsageReader.Refresh IOException for active session file: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        _pollTimer.Dispose();
        _watcher?.Dispose();
    }
}
```

- [ ] **Step 2: Manually verify against a fake session directory**

Create a throwaway console-style check (delete after use) or run via `dotnet fsi`/a scratch test — simplest is a temporary xUnit fact you delete afterward. Create `tests/ClaudePet.Tests/UsageReaderManualCheck.cs` temporarily:

```csharp
using ClaudePet.Logging;
using ClaudePet.Services;

namespace ClaudePet.Tests;

public class UsageReaderManualCheck
{
    [Fact(Skip = "Manual verification only — run explicitly, then delete this file.")]
    public async Task ManuallyObserveUsageChanged()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ClaudePetManual_" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(dir, "proj"));
        var log = new DebugLog(Path.Combine(dir, "debug.log"));
        var reader = new UsageReader(dir, log);

        UsageSnapshot? last = null;
        reader.UsageChanged += s => last = s;
        reader.Start();

        var sessionFile = Path.Combine(dir, "proj", "session.jsonl");
        await File.WriteAllTextAsync(sessionFile,
            """{"type":"assistant","message":{"model":"claude-sonnet-5","usage":{"input_tokens":1,"cache_creation_input_tokens":0,"cache_read_input_tokens":1000,"output_tokens":1}}}""" + "\n");

        await Task.Delay(6000); // allow FileSystemWatcher or poll timer to catch it

        Assert.NotNull(last);
        Assert.Equal(1001, last!.ContextTokens);
    }
}
```

Run it explicitly (bypassing `Skip`):

```bash
dotnet test --filter UsageReaderManualCheck
```

Temporarily remove `Skip = "..."` from the attribute to actually execute it, confirm it passes, then delete `tests/ClaudePet.Tests/UsageReaderManualCheck.cs` entirely — it was only a scratch check, not part of the permanent suite.

- [ ] **Step 3: Commit**

```bash
git add src/ClaudePet/Services/UsageReader.cs
git commit -m "feat: wire session location, tailing, and parsing into a live UsageReader"
```

---

## Task 10: ClickThroughHelper & StartupRegistration

**Files:**
- Create: `src/ClaudePet/Native/ClickThroughHelper.cs`
- Create: `src/ClaudePet/Native/StartupRegistration.cs`

**Interfaces:**
- Produces: `static class ClickThroughHelper` with `void SetClickThrough(IntPtr hwnd, bool clickThrough)`; `static class StartupRegistration` with `void SetEnabled(bool enabled)`. Consumed by `PetWindow` (Task 11) and `TrayIconManager` (Task 12).

Both are thin Win32/registry wrappers with no meaningful unit-testable logic (they require a real `HWND` or the real registry); verified manually in Task 13's end-to-end check.

- [ ] **Step 1: Implement `ClickThroughHelper`**

Create `src/ClaudePet/Native/ClickThroughHelper.cs`:

```csharp
using System.Runtime.InteropServices;

namespace ClaudePet.Native;

public static class ClickThroughHelper
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public static void SetClickThrough(IntPtr hwnd, bool clickThrough)
    {
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        int newStyle = clickThrough
            ? style | WS_EX_TRANSPARENT | WS_EX_LAYERED
            : style & ~WS_EX_TRANSPARENT;
        SetWindowLong(hwnd, GWL_EXSTYLE, newStyle);
    }
}
```

- [ ] **Step 2: Implement `StartupRegistration`**

Create `src/ClaudePet/Native/StartupRegistration.cs`:

```csharp
using System.Diagnostics;
using Microsoft.Win32;

namespace ClaudePet.Native;

public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudePet";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null)
            return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath is not null)
                key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
```

- [ ] **Step 3: Build to confirm it compiles**

```bash
dotnet build
```

Expected: build succeeds (0 errors).

- [ ] **Step 4: Commit**

```bash
git add src/ClaudePet/Native
git commit -m "feat: add Win32 click-through toggle and startup registry helpers"
```

---

## Task 11: PetWindow

**Files:**
- Create: `src/ClaudePet/PetWindow.xaml`
- Create: `src/ClaudePet/PetWindow.xaml.cs`

**Interfaces:**
- Consumes: `SettingsStore`/`AppSettings` (Task 6), `PixelArtGenerator`/`PixelFrame` (Task 8), `ClickThroughHelper` (Task 10), `Mood` (Task 1).
- Produces: `public partial class PetWindow : Window` with constructor `PetWindow(SettingsStore settingsStore)` and methods `void SetMood(Mood mood)`, `void SetDragMode(bool enabled)`. Consumed by `TrayIconManager` (Task 12) and `App.xaml.cs` (Task 13).

- [ ] **Step 1: Create the XAML**

Create `src/ClaudePet/PetWindow.xaml`:

```xml
<Window x:Class="ClaudePet.PetWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Claude Pet"
        Width="128" Height="128"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        ResizeMode="NoResize">
    <Image x:Name="SpriteImage"
           Width="128" Height="128"
           RenderOptions.BitmapScalingMode="NearestNeighbor"
           Stretch="Uniform" />
</Window>
```

- [ ] **Step 2: Implement the code-behind**

Create `src/ClaudePet/PetWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClaudePet.Models;
using ClaudePet.Native;
using ClaudePet.Rendering;
using ClaudePet.Settings;

namespace ClaudePet;

public partial class PetWindow : Window
{
    private readonly DispatcherTimer _animationTimer;
    private readonly SettingsStore _settingsStore;
    private IReadOnlyList<PixelFrame> _currentFrames = PixelArtGenerator.GenerateFrames(Mood.NoSession);
    private int _frameIndex;
    private bool _dragMode;

    public PetWindow(SettingsStore settingsStore)
    {
        InitializeComponent();
        _settingsStore = settingsStore;

        var settings = _settingsStore.Load();
        if (settings.WindowLeft >= 0 && settings.WindowTop >= 0)
        {
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 16;
            Top = workArea.Bottom - Height - 16;
        }

        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _animationTimer.Tick += (_, _) => AdvanceFrame();
        _animationTimer.Start();

        SourceInitialized += (_, _) => ApplyClickThrough(!_dragMode);
        MouseLeftButtonDown += (_, _) => { if (_dragMode) DragMove(); };
        Closing += (_, _) => SavePosition();

        Render();
    }

    public void SetMood(Mood mood)
    {
        _currentFrames = PixelArtGenerator.GenerateFrames(mood);
        _frameIndex = 0;
        Render();
    }

    public void SetDragMode(bool enabled)
    {
        _dragMode = enabled;
        ApplyClickThrough(!enabled);
    }

    private void AdvanceFrame()
    {
        _frameIndex = (_frameIndex + 1) % _currentFrames.Count;
        Render();
    }

    private void Render()
    {
        var frame = _currentFrames[_frameIndex];
        var bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
        // uint[] holds 0xAARRGGBB per pixel; on little-endian Windows this is
        // byte-identical to the B,G,R,A layout WritePixels expects for Bgra32.
        bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Width * 4, 0);
        SpriteImage.Source = bitmap;
    }

    private void ApplyClickThrough(bool clickThrough)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            ClickThroughHelper.SetClickThrough(hwnd, clickThrough);
    }

    private void SavePosition()
    {
        var settings = _settingsStore.Load() with { WindowLeft = Left, WindowTop = Top };
        _settingsStore.Save(settings);
    }
}
```

- [ ] **Step 3: Build to confirm it compiles**

```bash
dotnet build
```

Expected: build succeeds (0 errors). This does not yet run standalone — `App.xaml.cs` (Task 13) is what instantiates and shows it. Manual visual verification happens in Task 13's end-to-end check.

- [ ] **Step 4: Commit**

```bash
git add src/ClaudePet/PetWindow.xaml src/ClaudePet/PetWindow.xaml.cs
git commit -m "feat: add transparent click-through pet window with mood-driven sprite animation"
```

---

## Task 12: TrayIconManager

**Files:**
- Create: `src/ClaudePet/Tray/TrayIconManager.cs`

**Interfaces:**
- Consumes: `PetWindow.SetDragMode` (Task 11), `SettingsStore`/`AppSettings` (Task 6), `StartupRegistration` (Task 10).
- Produces: `sealed class TrayIconManager : IDisposable` with constructor `TrayIconManager(PetWindow petWindow, SettingsStore settingsStore)`. Consumed by `App.xaml.cs` (Task 13).

- [ ] **Step 1: Implement `TrayIconManager`**

Create `src/ClaudePet/Tray/TrayIconManager.cs`:

```csharp
using System.Windows;
using System.Windows.Forms;
using ClaudePet.Native;
using ClaudePet.Settings;

namespace ClaudePet.Tray;

public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly PetWindow _petWindow;
    private readonly SettingsStore _settingsStore;
    private readonly ToolStripMenuItem _dragItem;
    private bool _dragMode;

    public TrayIconManager(PetWindow petWindow, SettingsStore settingsStore)
    {
        _petWindow = petWindow;
        _settingsStore = settingsStore;

        _dragItem = new ToolStripMenuItem("Enable dragging", null, ToggleDragMode);

        var runAtStartupItem = new ToolStripMenuItem("Run at startup", null, ToggleRunAtStartup)
        {
            Checked = _settingsStore.Load().RunAtStartup
        };

        var quitItem = new ToolStripMenuItem("Quit", null, (_, _) => Application.Current.Shutdown());

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

    private void ToggleDragMode(object? sender, EventArgs e)
    {
        _dragMode = !_dragMode;
        _dragItem.Checked = _dragMode;
        _petWindow.SetDragMode(_dragMode);
    }

    private void ToggleRunAtStartup(object? sender, EventArgs e)
    {
        var item = (ToolStripMenuItem)sender!;
        item.Checked = !item.Checked;

        var settings = _settingsStore.Load() with { RunAtStartup = item.Checked };
        _settingsStore.Save(settings);
        StartupRegistration.SetEnabled(item.Checked);
    }

    public void Dispose()
    {
        _notifyIcon.Dispose();
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build
```

Expected: build succeeds (0 errors).

- [ ] **Step 3: Commit**

```bash
git add src/ClaudePet/Tray
git commit -m "feat: add tray icon for drag-mode toggle, startup toggle, and quit"
```

---

## Task 13: App Wiring & End-to-End Verification

**Files:**
- Modify: `src/ClaudePet/App.xaml`
- Modify: `src/ClaudePet/App.xaml.cs`
- Delete: `src/ClaudePet/MainWindow.xaml`, `src/ClaudePet/MainWindow.xaml.cs` (template defaults, replaced by `PetWindow`)

**Interfaces:**
- Consumes: everything from Tasks 2–12.
- Produces: a runnable app.

- [ ] **Step 1: Remove the template's default window**

Delete `src/ClaudePet/MainWindow.xaml` and `src/ClaudePet/MainWindow.xaml.cs`.

- [ ] **Step 2: Update `App.xaml`**

Replace the contents of `src/ClaudePet/App.xaml` with:

```xml
<Application x:Class="ClaudePet.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
</Application>
```

(`OnExplicitShutdown` is required because there's no traditional main window whose close triggers app shutdown — the tray's Quit item calls `Application.Current.Shutdown()` explicitly.)

- [ ] **Step 3: Implement `App.xaml.cs`**

Replace the contents of `src/ClaudePet/App.xaml.cs` with:

```csharp
using System.IO;
using System.Windows;
using ClaudePet.Logging;
using ClaudePet.Services;
using ClaudePet.Settings;
using ClaudePet.Tray;

namespace ClaudePet;

public partial class App : Application
{
    private UsageReader? _usageReader;
    private TrayIconManager? _trayIconManager;
    private PetWindow? _petWindow;
    private MoodStateMachine? _moodStateMachine;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudePet");
        var log = new DebugLog(Path.Combine(appDataDir, "debug.log"));
        var settingsStore = new SettingsStore(Path.Combine(appDataDir, "settings.json"), log.Write);

        _petWindow = new PetWindow(settingsStore);
        _petWindow.Show();

        _trayIconManager = new TrayIconManager(_petWindow, settingsStore);
        _moodStateMachine = new MoodStateMachine();

        var projectsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");

        _usageReader = new UsageReader(projectsRoot, log);
        _usageReader.UsageChanged += snapshot =>
        {
            Dispatcher.Invoke(() =>
            {
                var mood = _moodStateMachine.Update(snapshot);
                _petWindow.SetMood(mood);
            });
        };
        _usageReader.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _usageReader?.Dispose();
        _trayIconManager?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 4: Build the full solution**

```bash
dotnet build
```

Expected: build succeeds (0 errors).

- [ ] **Step 5: Run the full test suite**

```bash
dotnet test
```

Expected: all tests pass (the `UsageReaderManualCheck` scratch file from Task 9 should already have been deleted).

- [ ] **Step 6: Manual end-to-end verification**

Run the app against this machine's real Claude Code session logs:

```bash
dotnet run --project src/ClaudePet
```

Confirm all of the following:
1. A small pixel-art blob appears near the bottom-right of the screen, animating (gentle bounce) every ~500ms.
2. Clicking on desktop icons or windows *underneath* the pet works normally (click-through is active by default).
3. Right-click the tray icon (system tray, bottom-right of taskbar) → menu shows "Enable dragging", "Run at startup", "Quit".
4. Click "Enable dragging", then click-and-drag the pet — it moves and the click-through no longer blocks the drag.
5. Click "Enable dragging" again to turn it off; close and relaunch the app — the pet reappears at the position you dragged it to (position persisted via `SettingsStore`).
6. With this machine's real `~/.claude/projects` logs present, confirm the pet's mood is *not* stuck on `NoSession` — it should reflect actual usage from the most recently active session (this repo's own session log is a good source of live data, since it updates as this conversation continues).
7. Toggle "Run at startup" on, then check the registry:
   ```powershell
   Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name ClaudePet
   ```
   Expected: a value pointing at the built exe. Toggle it off and confirm the value is removed.
8. Click "Quit" — the tray icon disappears and the process exits.
9. Inspect `%LOCALAPPDATA%\ClaudePet\debug.log` — confirm it exists and contains no unexpected errors from this run.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: wire usage reader, mood engine, pet window, and tray into a runnable app"
```

---

## Self-Review Notes

- **Spec coverage:** Usage Reader → Task 2/3/4/9. Mood State Engine → Task 5. Pet Window (transparent/click-through/topmost, sprite animation, position persistence) → Task 11. Tray Icon (drag toggle, quit, startup toggle) → Task 12. App Shell wiring → Task 13. Error handling (no session, malformed JSON, unknown model, locked file, corrupt settings) → covered in Tasks 2, 6, 9. Testing strategy (parser/state-machine unit tests, manual verification for UI/OS-interop pieces) → covered throughout.
- **No placeholders:** every step has complete, runnable code; the one "manual verification only" step (Task 9) is explicitly justified (flaky to automate) and has concrete pass/fail criteria, not a TBD.
- **Type consistency:** `UsageSnapshot(int ContextTokens, int ContextLimit, double Percent)` used identically in Tasks 2, 5, 9. `Mood` enum values (`NoSession/Happy/Eating/Full/Stressed`) used identically in Tasks 1, 5, 8, 11. `PixelFrame(int Width, int Height, uint[] Pixels)` used identically in Tasks 8, 11. `AppSettings { WindowLeft, WindowTop, RunAtStartup }` used identically in Tasks 6, 11, 12, 13.
- **Scope:** single cohesive app, no sub-projects needed.

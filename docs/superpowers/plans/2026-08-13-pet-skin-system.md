# Claude Pet — Custom Skin System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user replace the pet's built-in procedural pixel art with their own — hand-drawn (PNG) or LLM-generated (PNG or a JSON pixel grid) — loaded from `%LOCALAPPDATA%\ClaudePet\skins\<folder-name>\`, selectable from the tray menu, alongside the existing built-in "Default" skin.

**Architecture:** A new `ClaudePet.Skins` namespace adds a validated, file-based alternative frame source (`Skin`) that produces the exact same `IReadOnlyList<PixelFrame>` shape `PixelArtGenerator` already does. `SkinLoader` discovers and fully validates every folder under `skins\` at startup (any single defect skips that whole skin, never crashes); `SkinManifestParser`/`PixelGridParser`/`PngFrameCodec` parse the manifest and frame files; `PixelCompositor` alpha-blends the Working/Worried overlay frames onto the mood's base frame, mirroring the layering the built-in generator already does. `PetWindow` forks its frame source between the active `Skin` (if any) and `PixelArtGenerator`. `TrayIconManager` gains a radio-select "Skin" submenu; `AppSettings.ActiveSkinName` persists the choice. An `ExampleSkinGenerator` seeds `skins\example\` on first run, derived directly from the built-in art, so there's always a valid, working skin folder to copy.

**Tech Stack:** C# / .NET 8 (`net8.0-windows`), WPF (`System.Windows.Media.Imaging` for PNG encode/decode), `System.Text.Json`, xUnit 2.5.3 for tests.

## Global Constraints

- Target framework `net8.0-windows`, nullable reference types enabled, implicit usings enabled (matches `src/ClaudePet/ClaudePet.csproj` and `tests/ClaudePet.Tests/ClaudePet.Tests.csproj` — do not add new `<PackageReference>`s beyond what's already referenced).
- Test framework is xUnit (`[Fact]`, `Assert.*`); no test framework attributes need a `using` (the `<Using Include="Xunit" />` global using is already configured).
- Every skin frame — PNG or JSON grid — is exactly 16x16 pixels. Any other size invalidates the whole skin.
- Every mood (`NoSession`/`Happy`/`Eating`/`Full`/`Stressed`) and both overlays (`working`/`worried`) require exactly 2 frames each (`frame0`/`frame1`), matching the existing 500ms idle/squish animation. No variable frame counts.
- A skin must supply **all** 5 moods and both overlays or is rejected entirely — never partially loaded, never mixed with Default's or another skin's art.
- Any single validation failure (missing file, wrong manifest shape, wrong PNG dimensions, malformed JSON grid, unresolved palette character) skips that whole skin folder — logged via `DebugLog`, never thrown past `SkinLoader`, never crashes the app.
- Pixel format is `uint[]` per `PixelFrame` (`src/ClaudePet/Rendering/PixelFrame.cs`), one `0xAARRGGBB` value per pixel — this is byte-identical to WPF's `PixelFormats.Bgra32` byte layout on little-endian Windows (see the existing comment in `PetWindow.Render()`), so decoding/encoding PNGs and reading/writing hex palette values must preserve that exact layout.
- JSON grid palette values are 8 hex digits, `AARRGGBB`, parsed via `uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ...)` into that same `uint` layout.
- Skins are discovered once at app startup only. Adding a *new* skin folder requires an app restart to appear in the tray submenu. Switching *between* already-discovered skins does **not** require a restart.
- `AppSettings.ActiveSkinName = null` always means the built-in Default skin — never persist an empty string as a sentinel for "no skin".
- If the persisted `ActiveSkinName` no longer resolves to a valid skin at startup, silently fall back to Default, log why, and correct the persisted setting back to `null`.
- Full spec: `docs/superpowers/specs/2026-08-13-pet-skin-system-design.md`.

---

### Task 1: `PixelArtGenerator` — extract standalone overlay-frame generators

**Files:**
- Modify: `src/ClaudePet/Rendering/PixelArtGenerator.cs`
- Modify: `tests/ClaudePet.Tests/PixelArtGeneratorTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `PixelArtGenerator.GenerateWorkingOverlayFrame(bool squish)` and `PixelArtGenerator.GenerateWorriedOverlayFrame(bool squish)`, both returning a 16x16 `PixelFrame` with a fully transparent background except the overlay's own accent pixels — consumed by Task 9 (`ExampleSkinGenerator`).

This extracts the existing inline overlay-pixel logic (already shipped this session) into two standalone, reusable methods, and has `GenerateFrame` call them instead of duplicating the coordinate math. Behavior is unchanged — `PixelArtGeneratorTests.cs`'s existing overlay tests must still pass unmodified.

- [ ] **Step 1: Write the failing tests**

Append to `tests/ClaudePet.Tests/PixelArtGeneratorTests.cs`, inside the `PixelArtGeneratorTests` class:

```csharp
    [Fact]
    public void GenerateWorkingOverlayFrame_NotSquished_HasExactlyOneOpaquePixelNearTopRight()
    {
        var frame = PixelArtGenerator.GenerateWorkingOverlayFrame(squish: false);

        Assert.Equal(1, CountOpaquePixels(frame));
        Assert.NotEqual(0u, frame[12, 2] >> 24);
    }

    [Fact]
    public void GenerateWorkingOverlayFrame_Squished_SparkMovesOnePixelLeft()
    {
        var frame = PixelArtGenerator.GenerateWorkingOverlayFrame(squish: true);

        Assert.NotEqual(0u, frame[11, 3] >> 24);
        Assert.Equal(1, CountOpaquePixels(frame));
    }

    [Fact]
    public void GenerateWorriedOverlayFrame_NotSquished_HasExactlyThreeOpaquePixelsNearTopLeft()
    {
        var frame = PixelArtGenerator.GenerateWorriedOverlayFrame(squish: false);

        Assert.Equal(3, CountOpaquePixels(frame));
        Assert.NotEqual(0u, frame[2, 2] >> 24);
    }

    private static int CountOpaquePixels(PixelFrame frame)
    {
        int count = 0;
        for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
                if ((frame[x, y] >> 24) != 0)
                    count++;
        return count;
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~PixelArtGeneratorTests`
Expected: FAIL — `PixelArtGenerator` has no `GenerateWorkingOverlayFrame`/`GenerateWorriedOverlayFrame` members (compile error).

- [ ] **Step 3: Add the two standalone generators and refactor `GenerateFrame` to use them**

Replace the full contents of `src/ClaudePet/Rendering/PixelArtGenerator.cs`:

```csharp
using ClaudePet.Models;

namespace ClaudePet.Rendering;

public static class PixelArtGenerator
{
    private const int Size = 16;
    private const uint Transparent = 0x00000000;
    private const uint EyeColor = 0xFF212121;
    // Overlay accents - independent of mood/body color, so both can render
    // at once (e.g. worried while the body is still Full-colored).
    private const uint WorkingColor = 0xFF29B6F6;
    private const uint WorriedColor = 0xFF81D4FA;

    public static IReadOnlyList<PixelFrame> GenerateFrames(Mood mood, bool isWorking = false, bool isWorried = false)
    {
        var bodyColor = BodyColor(mood);
        return new[]
        {
            GenerateFrame(bodyColor, mood, squish: false, isWorking, isWorried),
            GenerateFrame(bodyColor, mood, squish: true, isWorking, isWorried)
        };
    }

    // Standalone overlay frames (transparent everywhere except the accent
    // pixels) - used by GenerateFrame below, and reused directly by
    // ExampleSkinGenerator to seed the starter skin's overlay art without
    // duplicating this coordinate math.
    public static PixelFrame GenerateWorkingOverlayFrame(bool squish)
    {
        var pixels = new uint[Size * Size];
        Array.Fill(pixels, Transparent);

        int top = squish ? 3 : 2;
        int right = Size - 3;
        int sparkX = squish ? right - 2 : right - 1;
        pixels[top * Size + sparkX] = WorkingColor;

        return new PixelFrame(Size, Size, pixels);
    }

    public static PixelFrame GenerateWorriedOverlayFrame(bool squish)
    {
        var pixels = new uint[Size * Size];
        Array.Fill(pixels, Transparent);

        int top = squish ? 3 : 2;
        int left = 2;
        pixels[top * Size + left] = WorriedColor;
        pixels[(top + 1) * Size + left] = WorriedColor;
        pixels[(top + 1) * Size + (left + 1)] = WorriedColor;

        return new PixelFrame(Size, Size, pixels);
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

    private static PixelFrame GenerateFrame(uint bodyColor, Mood mood, bool squish, bool isWorking, bool isWorried)
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

        if (isWorking)
            CopyOpaquePixels(pixels, GenerateWorkingOverlayFrame(squish));

        if (isWorried)
            CopyOpaquePixels(pixels, GenerateWorriedOverlayFrame(squish));

        return new PixelFrame(Size, Size, pixels);
    }

    private static void CopyOpaquePixels(uint[] destination, PixelFrame overlay)
    {
        for (int i = 0; i < destination.Length; i++)
            if ((overlay.Pixels[i] >> 24) != 0)
                destination[i] = overlay.Pixels[i];
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~PixelArtGeneratorTests`
Expected: PASS — all `PixelArtGeneratorTests`, including the 3 new tests and every pre-existing test (`GenerateFrames_IsWorking_AddsAccentPixelNearTopRight`, `GenerateFrames_IsWorried_AddsSweatDropNearTopLeft`, `GenerateFrames_WorkingAndWorried_BothOverlaysCoexist`, `GenerateFrames_IsWorking_SparkPositionAlternatesBetweenFrames`, etc.) with no changes to their assertions.

- [ ] **Step 5: Run the full test suite to confirm no regressions elsewhere**

Run: `dotnet test tests/ClaudePet.Tests`
Expected: PASS — all tests (133 as of this session, plus the 3 added here).

- [ ] **Step 6: Commit**

```bash
git add src/ClaudePet/Rendering/PixelArtGenerator.cs tests/ClaudePet.Tests/PixelArtGeneratorTests.cs
git commit -m "refactor: extract standalone Working/Worried overlay-frame generators"
```

---

### Task 2: `PixelCompositor` — alpha-over blending

**Files:**
- Create: `src/ClaudePet/Skins/PixelCompositor.cs`
- Test: `tests/ClaudePet.Tests/PixelCompositorTests.cs`

**Interfaces:**
- Consumes: `PixelFrame` (`src/ClaudePet/Rendering/PixelFrame.cs`, existing).
- Produces: `static PixelFrame PixelCompositor.CompositeOver(PixelFrame @base, PixelFrame overlay)` — consumed by Task 7 (`Skin`).

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudePet.Tests/PixelCompositorTests.cs`:

```csharp
using ClaudePet.Rendering;
using ClaudePet.Skins;

namespace ClaudePet.Tests;

public class PixelCompositorTests
{
    private static PixelFrame SolidFrame(int width, int height, uint color)
    {
        var pixels = new uint[width * height];
        Array.Fill(pixels, color);
        return new PixelFrame(width, height, pixels);
    }

    [Fact]
    public void CompositeOver_OverlayFullyTransparent_ReturnsBaseUnchanged()
    {
        var @base = SolidFrame(2, 1, 0xFF112233);
        var overlay = SolidFrame(2, 1, 0x00000000);

        var result = PixelCompositor.CompositeOver(@base, overlay);

        Assert.Equal(0xFF112233u, result[0, 0]);
        Assert.Equal(0xFF112233u, result[1, 0]);
    }

    [Fact]
    public void CompositeOver_OverlayFullyOpaque_ReplacesBase()
    {
        var @base = SolidFrame(1, 1, 0xFF112233);
        var overlay = SolidFrame(1, 1, 0xFFAABBCC);

        var result = PixelCompositor.CompositeOver(@base, overlay);

        Assert.Equal(0xFFAABBCCu, result[0, 0]);
    }

    [Fact]
    public void CompositeOver_BaseFullyTransparentOverlayFullyOpaque_ReplacesBase()
    {
        var @base = SolidFrame(1, 1, 0x00000000);
        var overlay = SolidFrame(1, 1, 0xFFAABBCC);

        var result = PixelCompositor.CompositeOver(@base, overlay);

        Assert.Equal(0xFFAABBCCu, result[0, 0]);
    }

    [Fact]
    public void CompositeOver_PartialAlphaOverlay_BlendsProportionally()
    {
        // 50% alpha white over opaque black -> ~mid-grey, fully opaque.
        var @base = SolidFrame(1, 1, 0xFF000000);
        var overlay = SolidFrame(1, 1, 0x80FFFFFF);

        var result = PixelCompositor.CompositeOver(@base, overlay)[0, 0];

        var alpha = result >> 24;
        var red = (result >> 16) & 0xFF;
        Assert.Equal(0xFFu, alpha);
        Assert.InRange((int)red, 120, 135);
    }

    [Fact]
    public void CompositeOver_BothFullyTransparent_StaysFullyTransparent()
    {
        var @base = SolidFrame(1, 1, 0x00000000);
        var overlay = SolidFrame(1, 1, 0x00000000);

        var result = PixelCompositor.CompositeOver(@base, overlay);

        Assert.Equal(0u, result[0, 0] >> 24);
    }

    [Fact]
    public void CompositeOver_PreservesFrameDimensions()
    {
        var @base = SolidFrame(3, 2, 0xFF000000);
        var overlay = SolidFrame(3, 2, 0x00000000);

        var result = PixelCompositor.CompositeOver(@base, overlay);

        Assert.Equal(3, result.Width);
        Assert.Equal(2, result.Height);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~PixelCompositorTests`
Expected: FAIL — the `ClaudePet.Skins` namespace / `PixelCompositor` type doesn't exist yet (compile error).

- [ ] **Step 3: Implement `PixelCompositor`**

Create `src/ClaudePet/Skins/PixelCompositor.cs`:

```csharp
using ClaudePet.Rendering;

namespace ClaudePet.Skins;

public static class PixelCompositor
{
    // Straight (non-premultiplied) alpha "src over dst" - the app's PixelFrame
    // pixels are always straight alpha (a fully transparent pixel is exactly
    // 0x00000000; an opaque one always carries its full, unscaled RGB - see
    // PixelFrame/PixelArtGenerator).
    public static PixelFrame CompositeOver(PixelFrame @base, PixelFrame overlay)
    {
        var pixels = new uint[@base.Width * @base.Height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = CompositePixel(@base.Pixels[i], overlay.Pixels[i]);

        return new PixelFrame(@base.Width, @base.Height, pixels);
    }

    private static uint CompositePixel(uint dst, uint src)
    {
        var srcA = (byte)(src >> 24);
        if (srcA == 0)
            return dst;
        if (srcA == 255)
            return src;

        var srcR = (byte)(src >> 16);
        var srcG = (byte)(src >> 8);
        var srcB = (byte)src;
        var dstA = (byte)(dst >> 24);
        var dstR = (byte)(dst >> 16);
        var dstG = (byte)(dst >> 8);
        var dstB = (byte)dst;

        int outA = srcA + dstA * (255 - srcA) / 255;
        if (outA == 0)
            return 0;

        byte Blend(byte s, byte d) => (byte)((s * srcA + d * dstA * (255 - srcA) / 255) / outA);

        var outR = Blend(srcR, dstR);
        var outG = Blend(srcG, dstG);
        var outB = Blend(srcB, dstB);

        return (uint)(outA << 24 | outR << 16 | outG << 8 | outB);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~PixelCompositorTests`
Expected: PASS (all 6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClaudePet/Skins/PixelCompositor.cs tests/ClaudePet.Tests/PixelCompositorTests.cs
git commit -m "feat: add PixelCompositor for alpha-over overlay blending"
```

---

### Task 3: `PixelGridParser` — JSON pixel-grid frame parsing

**Files:**
- Create: `src/ClaudePet/Skins/PixelGridParser.cs`
- Test: `tests/ClaudePet.Tests/PixelGridParserTests.cs`

**Interfaces:**
- Consumes: `PixelFrame` (existing).
- Produces: `static PixelFrame? PixelGridParser.Parse(string json, Action<string>? onError = null)` — consumed by Task 4 (round-trip test), Task 8 (`SkinLoader`).

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudePet.Tests/PixelGridParserTests.cs`:

```csharp
using ClaudePet.Skins;

namespace ClaudePet.Tests;

public class PixelGridParserTests
{
    private static string ValidGridJson() => """
    {
      "palette": { "R": "FF4CAF50", ".": "00000000" },
      "pixels": [
        "................",
        "................",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "..RRRRRRRRRRRR..",
        "................",
        "................"
      ]
    }
    """;

    [Fact]
    public void Parse_ValidGrid_ProducesExpectedPixels()
    {
        var frame = PixelGridParser.Parse(ValidGridJson());

        Assert.NotNull(frame);
        Assert.Equal(16, frame!.Width);
        Assert.Equal(16, frame.Height);
        Assert.Equal(0xFF4CAF50u, frame[2, 2]);
        Assert.Equal(0u, frame[0, 0]);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsNullAndReportsError()
    {
        string? error = null;

        var frame = PixelGridParser.Parse("{ not valid json ", err => error = err);

        Assert.Null(frame);
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_MissingPalette_ReturnsNull()
    {
        var frame = PixelGridParser.Parse("""{ "pixels": [] }""");

        Assert.Null(frame);
    }

    [Fact]
    public void Parse_WrongRowCount_ReturnsNullAndReportsError()
    {
        var json = """
        {
          "palette": { ".": "00000000" },
          "pixels": [ "................" ]
        }
        """;
        string? error = null;

        var frame = PixelGridParser.Parse(json, err => error = err);

        Assert.Null(frame);
        Assert.Contains("16", error);
    }

    [Fact]
    public void Parse_WrongColumnCount_ReturnsNullAndReportsError()
    {
        var rows = Enumerable.Repeat("\"...\"", 16);
        var json = $$"""{ "palette": { ".": "00000000" }, "pixels": [ {{string.Join(",", rows)}} ] }""";
        string? error = null;

        var frame = PixelGridParser.Parse(json, err => error = err);

        Assert.Null(frame);
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_UnresolvedPaletteCharacter_ReturnsNullAndReportsError()
    {
        var rows = Enumerable.Repeat("\"................\"", 15).Append("\"X...............\"");
        var json = $$"""{ "palette": { ".": "00000000" }, "pixels": [ {{string.Join(",", rows)}} ] }""";
        string? error = null;

        var frame = PixelGridParser.Parse(json, err => error = err);

        Assert.Null(frame);
        Assert.Contains("X", error);
    }

    [Fact]
    public void Parse_InvalidHexInPalette_ReturnsNullAndReportsError()
    {
        var rows = Enumerable.Repeat("\"................\"", 16);
        var json = $$"""{ "palette": { ".": "ZZZZZZZZ" }, "pixels": [ {{string.Join(",", rows)}} ] }""";
        string? error = null;

        var frame = PixelGridParser.Parse(json, err => error = err);

        Assert.Null(frame);
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_PartialAlphaPaletteValue_PreservesAlphaByte()
    {
        var rows = Enumerable.Repeat("\"................\"", 15).Append("\"H...............\"");
        var json = $$"""{ "palette": { ".": "00000000", "H": "80112233" }, "pixels": [ {{string.Join(",", rows)}} ] }""";

        var frame = PixelGridParser.Parse(json);

        Assert.NotNull(frame);
        Assert.Equal(0x80112233u, frame![0, 15]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~PixelGridParserTests`
Expected: FAIL — `PixelGridParser` doesn't exist yet (compile error).

- [ ] **Step 3: Implement `PixelGridParser`**

Create `src/ClaudePet/Skins/PixelGridParser.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using ClaudePet.Rendering;

namespace ClaudePet.Skins;

public static class PixelGridParser
{
    private const int Size = 16;

    public static PixelFrame? Parse(string json, Action<string>? onError = null)
    {
        GridDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<GridDto>(json);
        }
        catch (JsonException)
        {
            onError?.Invoke("pixel grid is not valid JSON");
            return null;
        }

        if (dto?.Palette is null || dto.Pixels is null)
        {
            onError?.Invoke("pixel grid missing palette or pixels");
            return null;
        }

        if (dto.Pixels.Length != Size)
        {
            onError?.Invoke($"pixel grid must have exactly {Size} rows, found {dto.Pixels.Length}");
            return null;
        }

        var palette = new Dictionary<char, uint>();
        foreach (var (key, hex) in dto.Palette)
        {
            if (key.Length != 1)
            {
                onError?.Invoke($"palette key '{key}' must be a single character");
                return null;
            }
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var color))
            {
                onError?.Invoke($"palette entry '{key}' has an invalid hex color '{hex}'");
                return null;
            }
            palette[key[0]] = color;
        }

        var pixels = new uint[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            var row = dto.Pixels[y];
            if (row.Length != Size)
            {
                onError?.Invoke($"pixel grid row {y} must have exactly {Size} characters, found {row.Length}");
                return null;
            }
            for (int x = 0; x < Size; x++)
            {
                if (!palette.TryGetValue(row[x], out var color))
                {
                    onError?.Invoke($"pixel grid character '{row[x]}' at row {y}, column {x} has no palette entry");
                    return null;
                }
                pixels[y * Size + x] = color;
            }
        }

        return new PixelFrame(Size, Size, pixels);
    }

    private sealed class GridDto
    {
        public Dictionary<string, string>? Palette { get; set; }
        public string[]? Pixels { get; set; }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~PixelGridParserTests`
Expected: PASS (all 8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClaudePet/Skins/PixelGridParser.cs tests/ClaudePet.Tests/PixelGridParserTests.cs
git commit -m "feat: add PixelGridParser for JSON pixel-grid skin frames"
```

---

### Task 4: `PixelGridWriter` — inverse of `PixelGridParser`

**Files:**
- Create: `src/ClaudePet/Skins/PixelGridWriter.cs`
- Test: `tests/ClaudePet.Tests/PixelGridWriterTests.cs`

**Interfaces:**
- Consumes: `PixelFrame` (existing), `PixelGridParser.Parse` (Task 3, for round-trip tests only).
- Produces: `static string PixelGridWriter.Write(PixelFrame frame)` — consumed by Task 9 (`ExampleSkinGenerator`).

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudePet.Tests/PixelGridWriterTests.cs`:

```csharp
using ClaudePet.Rendering;
using ClaudePet.Skins;

namespace ClaudePet.Tests;

public class PixelGridWriterTests
{
    [Fact]
    public void Write_ThenParse_RoundTripsPixelsExactly()
    {
        var pixels = new uint[16 * 16];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = i % 3 == 0 ? 0xFF4CAF50u : i % 3 == 1 ? 0x00000000u : 0x80112233u;
        var original = new PixelFrame(16, 16, pixels);

        var json = PixelGridWriter.Write(original);
        var parsed = PixelGridParser.Parse(json);

        Assert.NotNull(parsed);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                Assert.Equal(original[x, y], parsed![x, y]);
    }

    [Fact]
    public void Write_FullyTransparentFrame_RoundTripsToAllTransparent()
    {
        var pixels = new uint[16 * 16];
        var original = new PixelFrame(16, 16, pixels);

        var json = PixelGridWriter.Write(original);
        var parsed = PixelGridParser.Parse(json);

        Assert.NotNull(parsed);
        Assert.Equal(0u, parsed![0, 0]);
        Assert.Equal(0u, parsed[15, 15]);
    }

    [Fact]
    public void Write_ProducesValidJsonParseableByPixelGridParser()
    {
        var pixels = new uint[16 * 16];
        Array.Fill(pixels, 0xFFAABBCCu);
        var original = new PixelFrame(16, 16, pixels);

        var json = PixelGridWriter.Write(original);

        string? error = null;
        var parsed = PixelGridParser.Parse(json, err => error = err);
        Assert.NotNull(parsed);
        Assert.Null(error);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~PixelGridWriterTests`
Expected: FAIL — `PixelGridWriter` doesn't exist yet (compile error).

- [ ] **Step 3: Implement `PixelGridWriter`**

Create `src/ClaudePet/Skins/PixelGridWriter.cs`:

```csharp
using System.Text;
using System.Text.Json;
using ClaudePet.Rendering;

namespace ClaudePet.Skins;

// Inverse of PixelGridParser - writes a PixelFrame as a JSON pixel grid,
// auto-deriving a minimal palette from the frame's own distinct colors.
// Used only to seed ExampleSkinGenerator's starter skin, whose frames use a
// handful of colors at most, so the single-character palette-key space
// ('A'.."Z", then continuing past 'Z') is never a practical concern here.
public static class PixelGridWriter
{
    public static string Write(PixelFrame frame)
    {
        var palette = new Dictionary<uint, char>();
        var nextKey = 'A';
        var rows = new string[frame.Height];

        for (int y = 0; y < frame.Height; y++)
        {
            var row = new StringBuilder(frame.Width);
            for (int x = 0; x < frame.Width; x++)
            {
                var color = frame[x, y];
                if (!palette.TryGetValue(color, out var key))
                {
                    key = nextKey;
                    nextKey = (char)(nextKey + 1);
                    palette[color] = key;
                }
                row.Append(key);
            }
            rows[y] = row.ToString();
        }

        var paletteDto = palette.ToDictionary(kv => kv.Value.ToString(), kv => kv.Key.ToString("X8"));
        var dto = new { palette = paletteDto, pixels = rows };
        return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~PixelGridWriterTests`
Expected: PASS (all 3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClaudePet/Skins/PixelGridWriter.cs tests/ClaudePet.Tests/PixelGridWriterTests.cs
git commit -m "feat: add PixelGridWriter (inverse of PixelGridParser)"
```

---

### Task 5: `PngFrameCodec` — PNG frame encode/decode

**Files:**
- Create: `src/ClaudePet/Skins/PngFrameCodec.cs`
- Test: `tests/ClaudePet.Tests/PngFrameCodecTests.cs`
- Modify: `tests/ClaudePet.Tests/ClaudePet.Tests.csproj`

**Interfaces:**
- Consumes: `PixelFrame` (existing).
- Produces: `static byte[] PngFrameCodec.Encode(PixelFrame frame)` and `static PixelFrame? PngFrameCodec.Decode(byte[] pngBytes, Action<string>? onError = null)` — consumed by Task 8 (`SkinLoader`, decode) and Task 9 (`ExampleSkinGenerator`, encode).

`Encode`/`Decode` use WPF's `System.Windows.Media.Imaging` types internally, but neither public signature exposes a WPF type — only `PixelFrame`/`byte[]`. The test project doesn't currently set `UseWPF`, which normally only matters for a project's *own* code referencing WPF types directly (it doesn't here), but since this task's tests are the first in the suite to exercise WPF imaging machinery transitively (via the `ClaudePet.csproj` reference), Step 1 below adds `UseWPF` to the test project proactively to remove any doubt about runtime assembly resolution, rather than leaving it to be discovered as a test failure.

- [ ] **Step 1: Add `UseWPF` to the test project**

In `tests/ClaudePet.Tests/ClaudePet.Tests.csproj`, add `<UseWPF>true</UseWPF>` to the `<PropertyGroup>`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>

    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\ClaudePet\ClaudePet.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write the failing tests**

Create `tests/ClaudePet.Tests/PngFrameCodecTests.cs`:

```csharp
using ClaudePet.Rendering;
using ClaudePet.Skins;

namespace ClaudePet.Tests;

public class PngFrameCodecTests
{
    private static PixelFrame SampleFrame()
    {
        var pixels = new uint[16 * 16];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = i % 4 == 0 ? 0xFF4CAF50u : i % 4 == 1 ? 0x00000000u : i % 4 == 2 ? 0xFF212121u : 0x80AABBCCu;
        return new PixelFrame(16, 16, pixels);
    }

    [Fact]
    public void Encode_ThenDecode_RoundTripsPixelsExactly()
    {
        var original = SampleFrame();

        var bytes = PngFrameCodec.Encode(original);
        var decoded = PngFrameCodec.Decode(bytes);

        Assert.NotNull(decoded);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                Assert.Equal(original[x, y], decoded![x, y]);
    }

    [Fact]
    public void Decode_NotAPngFile_ReturnsNullAndReportsError()
    {
        string? error = null;

        var result = PngFrameCodec.Decode(new byte[] { 1, 2, 3, 4 }, err => error = err);

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void Decode_WrongDimensions_ReturnsNullAndReportsError()
    {
        var pixels = new uint[8 * 8];
        var wrongSizeFrame = new PixelFrame(8, 8, pixels);
        var bytes = PngFrameCodec.Encode(wrongSizeFrame);
        string? error = null;

        var result = PngFrameCodec.Decode(bytes, err => error = err);

        Assert.Null(result);
        Assert.Contains("16", error);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~PngFrameCodecTests`
Expected: FAIL — `PngFrameCodec` doesn't exist yet (compile error).

- [ ] **Step 4: Implement `PngFrameCodec`**

Create `src/ClaudePet/Skins/PngFrameCodec.cs`:

```csharp
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClaudePet.Rendering;

namespace ClaudePet.Skins;

public static class PngFrameCodec
{
    private const int Size = 16;

    public static PixelFrame? Decode(byte[] pngBytes, Action<string>? onError = null)
    {
        BitmapSource decoded;
        try
        {
            using var stream = new MemoryStream(pngBytes);
            decoded = BitmapDecoder.Create(
                stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        }
        catch (Exception ex) when (ex is FileFormatException or NotSupportedException)
        {
            onError?.Invoke("not a valid PNG file");
            return null;
        }

        if (decoded.PixelWidth != Size || decoded.PixelHeight != Size)
        {
            onError?.Invoke($"PNG must be exactly {Size}x{Size}, found {decoded.PixelWidth}x{decoded.PixelHeight}");
            return null;
        }

        var converted = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
        var stride = Size * 4;
        var bytes = new byte[stride * Size];
        converted.CopyPixels(bytes, stride, 0);

        var pixels = new uint[Size * Size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = BitConverter.ToUInt32(bytes, i * 4);

        return new PixelFrame(Size, Size, pixels);
    }

    public static byte[] Encode(PixelFrame frame)
    {
        var stride = frame.Width * 4;
        var bytes = new byte[stride * frame.Height];
        for (int i = 0; i < frame.Pixels.Length; i++)
            BitConverter.GetBytes(frame.Pixels[i]).CopyTo(bytes, i * 4);

        var bitmap = BitmapSource.Create(
            frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, bytes, stride);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~PngFrameCodecTests`
Expected: PASS (all 3 tests). If this fails with an assembly-load error instead of a normal test failure, confirm Step 1's `<UseWPF>true</UseWPF>` was saved and re-run `dotnet build tests/ClaudePet.Tests` first.

- [ ] **Step 6: Run the full test suite to confirm no regressions from the csproj change**

Run: `dotnet test tests/ClaudePet.Tests`
Expected: PASS — every existing test still passes with `UseWPF` added.

- [ ] **Step 7: Commit**

```bash
git add src/ClaudePet/Skins/PngFrameCodec.cs tests/ClaudePet.Tests/PngFrameCodecTests.cs tests/ClaudePet.Tests/ClaudePet.Tests.csproj
git commit -m "feat: add PngFrameCodec for PNG-based skin frames"
```

---

### Task 6: `SkinManifestParser` — `skin.json` parsing and validation

**Files:**
- Create: `src/ClaudePet/Skins/SkinManifestParser.cs`
- Test: `tests/ClaudePet.Tests/SkinManifestParserTests.cs`

**Interfaces:**
- Consumes: `Mood` (`src/ClaudePet/Models/Mood.cs`, existing enum with members `NoSession`, `Happy`, `Eating`, `Full`, `Stressed`).
- Produces:
  - `sealed record SkinFramePaths(string Frame0, string Frame1)`
  - `sealed record SkinManifest(string DisplayName, IReadOnlyDictionary<Mood, SkinFramePaths> Moods, SkinFramePaths WorkingOverlay, SkinFramePaths WorriedOverlay)`
  - `static SkinManifest? SkinManifestParser.Parse(string json, Action<string>? onError = null)`
  - All consumed by Task 8 (`SkinLoader`).

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudePet.Tests/SkinManifestParserTests.cs`:

```csharp
using ClaudePet.Models;
using ClaudePet.Skins;

namespace ClaudePet.Tests;

public class SkinManifestParserTests
{
    private static string ValidManifestJson() => """
    {
      "displayName": "My Cool Pet",
      "moods": {
        "NoSession": { "frame0": "nosession_0.png", "frame1": "nosession_1.png" },
        "Happy":     { "frame0": "happy_0.png",     "frame1": "happy_1.png" },
        "Eating":    { "frame0": "eating_0.json",   "frame1": "eating_1.json" },
        "Full":      { "frame0": "full_0.png",      "frame1": "full_1.png" },
        "Stressed":  { "frame0": "stressed_0.png",  "frame1": "stressed_1.png" }
      },
      "overlays": {
        "working": { "frame0": "working_0.png", "frame1": "working_1.png" },
        "worried": { "frame0": "worried_0.png", "frame1": "worried_1.png" }
      }
    }
    """;

    [Fact]
    public void Parse_ValidManifest_ReturnsFullyPopulatedManifest()
    {
        var manifest = SkinManifestParser.Parse(ValidManifestJson());

        Assert.NotNull(manifest);
        Assert.Equal("My Cool Pet", manifest!.DisplayName);
        Assert.Equal(5, manifest.Moods.Count);
        Assert.Equal("happy_0.png", manifest.Moods[Mood.Happy].Frame0);
        Assert.Equal("happy_1.png", manifest.Moods[Mood.Happy].Frame1);
        Assert.Equal("eating_0.json", manifest.Moods[Mood.Eating].Frame0);
        Assert.Equal("working_0.png", manifest.WorkingOverlay.Frame0);
        Assert.Equal("worried_1.png", manifest.WorriedOverlay.Frame1);
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsNullAndReportsError()
    {
        string? error = null;

        var manifest = SkinManifestParser.Parse("{ not valid json ", err => error = err);

        Assert.Null(manifest);
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_MissingDisplayName_ReturnsNullAndReportsError()
    {
        var json = """{ "moods": {}, "overlays": {} }""";
        string? error = null;

        var manifest = SkinManifestParser.Parse(json, err => error = err);

        Assert.Null(manifest);
        Assert.Contains("displayName", error);
    }

    [Fact]
    public void Parse_MissingOneMood_ReturnsNullAndReportsWhichOne()
    {
        var json = """
        {
          "displayName": "X",
          "moods": {
            "NoSession": { "frame0": "a.png", "frame1": "b.png" },
            "Happy":     { "frame0": "a.png", "frame1": "b.png" },
            "Eating":    { "frame0": "a.png", "frame1": "b.png" },
            "Full":      { "frame0": "a.png", "frame1": "b.png" }
          },
          "overlays": {
            "working": { "frame0": "a.png", "frame1": "b.png" },
            "worried": { "frame0": "a.png", "frame1": "b.png" }
          }
        }
        """;
        string? error = null;

        var manifest = SkinManifestParser.Parse(json, err => error = err);

        Assert.Null(manifest);
        Assert.Contains("Stressed", error);
    }

    [Fact]
    public void Parse_MoodMissingFrame1_ReturnsNull()
    {
        var json = """
        {
          "displayName": "X",
          "moods": {
            "NoSession": { "frame0": "a.png" },
            "Happy":     { "frame0": "a.png", "frame1": "b.png" },
            "Eating":    { "frame0": "a.png", "frame1": "b.png" },
            "Full":      { "frame0": "a.png", "frame1": "b.png" },
            "Stressed":  { "frame0": "a.png", "frame1": "b.png" }
          },
          "overlays": {
            "working": { "frame0": "a.png", "frame1": "b.png" },
            "worried": { "frame0": "a.png", "frame1": "b.png" }
          }
        }
        """;

        var manifest = SkinManifestParser.Parse(json);

        Assert.Null(manifest);
    }

    [Fact]
    public void Parse_MissingWorkingOverlay_ReturnsNullAndReportsError()
    {
        var json = """
        {
          "displayName": "X",
          "moods": {
            "NoSession": { "frame0": "a.png", "frame1": "b.png" },
            "Happy":     { "frame0": "a.png", "frame1": "b.png" },
            "Eating":    { "frame0": "a.png", "frame1": "b.png" },
            "Full":      { "frame0": "a.png", "frame1": "b.png" },
            "Stressed":  { "frame0": "a.png", "frame1": "b.png" }
          },
          "overlays": {
            "worried": { "frame0": "a.png", "frame1": "b.png" }
          }
        }
        """;
        string? error = null;

        var manifest = SkinManifestParser.Parse(json, err => error = err);

        Assert.Null(manifest);
        Assert.Contains("working", error);
    }

    [Fact]
    public void Parse_MissingWorriedOverlay_ReturnsNullAndReportsError()
    {
        var json = """
        {
          "displayName": "X",
          "moods": {
            "NoSession": { "frame0": "a.png", "frame1": "b.png" },
            "Happy":     { "frame0": "a.png", "frame1": "b.png" },
            "Eating":    { "frame0": "a.png", "frame1": "b.png" },
            "Full":      { "frame0": "a.png", "frame1": "b.png" },
            "Stressed":  { "frame0": "a.png", "frame1": "b.png" }
          },
          "overlays": {
            "working": { "frame0": "a.png", "frame1": "b.png" }
          }
        }
        """;
        string? error = null;

        var manifest = SkinManifestParser.Parse(json, err => error = err);

        Assert.Null(manifest);
        Assert.Contains("worried", error);
    }

    [Fact]
    public void Parse_CaseInsensitiveTopLevelKeys_StillResolves()
    {
        var json = """
        {
          "DisplayName": "X",
          "Moods": {
            "NoSession": { "Frame0": "a.png", "Frame1": "b.png" },
            "Happy":     { "Frame0": "a.png", "Frame1": "b.png" },
            "Eating":    { "Frame0": "a.png", "Frame1": "b.png" },
            "Full":      { "Frame0": "a.png", "Frame1": "b.png" },
            "Stressed":  { "Frame0": "a.png", "Frame1": "b.png" }
          },
          "Overlays": {
            "working": { "Frame0": "a.png", "Frame1": "b.png" },
            "worried": { "Frame0": "a.png", "Frame1": "b.png" }
          }
        }
        """;

        var manifest = SkinManifestParser.Parse(json);

        Assert.NotNull(manifest);
        Assert.Equal("X", manifest!.DisplayName);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~SkinManifestParserTests`
Expected: FAIL — `SkinManifestParser` doesn't exist yet (compile error).

- [ ] **Step 3: Implement `SkinManifestParser`**

Create `src/ClaudePet/Skins/SkinManifestParser.cs`:

```csharp
using System.Text.Json;
using ClaudePet.Models;

namespace ClaudePet.Skins;

public sealed record SkinFramePaths(string Frame0, string Frame1);

public sealed record SkinManifest(
    string DisplayName,
    IReadOnlyDictionary<Mood, SkinFramePaths> Moods,
    SkinFramePaths WorkingOverlay,
    SkinFramePaths WorriedOverlay);

public static class SkinManifestParser
{
    private static readonly (Mood Mood, string Key)[] RequiredMoods =
    {
        (Mood.NoSession, "NoSession"),
        (Mood.Happy, "Happy"),
        (Mood.Eating, "Eating"),
        (Mood.Full, "Full"),
        (Mood.Stressed, "Stressed"),
    };

    public static SkinManifest? Parse(string json, Action<string>? onError = null)
    {
        ManifestDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ManifestDto>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            onError?.Invoke("skin.json is not valid JSON");
            return null;
        }

        if (dto is null)
        {
            onError?.Invoke("skin.json is empty");
            return null;
        }

        if (string.IsNullOrWhiteSpace(dto.DisplayName))
        {
            onError?.Invoke("skin.json missing displayName");
            return null;
        }

        if (dto.Moods is null)
        {
            onError?.Invoke("skin.json missing moods");
            return null;
        }

        var moods = new Dictionary<Mood, SkinFramePaths>();
        foreach (var (mood, key) in RequiredMoods)
        {
            if (!dto.Moods.TryGetValue(key, out var paths) || !TryToFramePaths(paths, out var framePaths))
            {
                onError?.Invoke($"skin.json missing or incomplete moods.{key}");
                return null;
            }
            moods[mood] = framePaths;
        }

        if (dto.Overlays is null || !dto.Overlays.TryGetValue("working", out var workingDto) ||
            !TryToFramePaths(workingDto, out var working))
        {
            onError?.Invoke("skin.json missing or incomplete overlays.working");
            return null;
        }

        if (!dto.Overlays.TryGetValue("worried", out var worriedDto) || !TryToFramePaths(worriedDto, out var worried))
        {
            onError?.Invoke("skin.json missing or incomplete overlays.worried");
            return null;
        }

        return new SkinManifest(dto.DisplayName, moods, working, worried);
    }

    private static bool TryToFramePaths(FramePathsDto? dto, out SkinFramePaths framePaths)
    {
        if (dto is not null && !string.IsNullOrWhiteSpace(dto.Frame0) && !string.IsNullOrWhiteSpace(dto.Frame1))
        {
            framePaths = new SkinFramePaths(dto.Frame0, dto.Frame1);
            return true;
        }
        framePaths = null!;
        return false;
    }

    private sealed class ManifestDto
    {
        public string? DisplayName { get; set; }
        public Dictionary<string, FramePathsDto>? Moods { get; set; }
        public Dictionary<string, FramePathsDto>? Overlays { get; set; }
    }

    private sealed class FramePathsDto
    {
        public string? Frame0 { get; set; }
        public string? Frame1 { get; set; }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~SkinManifestParserTests`
Expected: PASS (all 8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClaudePet/Skins/SkinManifestParser.cs tests/ClaudePet.Tests/SkinManifestParserTests.cs
git commit -m "feat: add SkinManifestParser for skin.json parsing and validation"
```

---

### Task 7: `Skin` model

**Files:**
- Create: `src/ClaudePet/Skins/Skin.cs`
- Test: `tests/ClaudePet.Tests/SkinTests.cs`

**Interfaces:**
- Consumes: `PixelCompositor.CompositeOver` (Task 2), `Mood` (existing), `PixelFrame` (existing).
- Produces: `sealed class Skin` with constructor `Skin(string folderName, string displayName, IReadOnlyDictionary<Mood, (PixelFrame Frame0, PixelFrame Frame1)> moods, (PixelFrame Frame0, PixelFrame Frame1) working, (PixelFrame Frame0, PixelFrame Frame1) worried)`, properties `string FolderName`, `string DisplayName`, and method `IReadOnlyList<PixelFrame> GenerateFrames(Mood mood, bool isWorking = false, bool isWorried = false)` — consumed by Task 8 (`SkinLoader` constructs it), Task 11 (`PetWindow` calls `GenerateFrames`), Task 13 (`App.xaml.cs` reads `FolderName`/`DisplayName`).

- [ ] **Step 1: Write the failing tests**

Create `tests/ClaudePet.Tests/SkinTests.cs`:

```csharp
using ClaudePet.Models;
using ClaudePet.Rendering;
using ClaudePet.Skins;

namespace ClaudePet.Tests;

public class SkinTests
{
    private static PixelFrame SolidFrame(uint color)
    {
        var pixels = new uint[16 * 16];
        Array.Fill(pixels, color);
        return new PixelFrame(16, 16, pixels);
    }

    private static PixelFrame TransparentFrame()
    {
        return new PixelFrame(16, 16, new uint[16 * 16]);
    }

    private static Skin BuildSkin(
        (PixelFrame, PixelFrame)? working = null,
        (PixelFrame, PixelFrame)? worried = null)
    {
        var happyFrames = (SolidFrame(0xFF4CAF50), SolidFrame(0xFF388E3C));
        var moods = new Dictionary<Mood, (PixelFrame, PixelFrame)>
        {
            [Mood.NoSession] = (SolidFrame(0xFF9E9E9E), SolidFrame(0xFF9E9E9E)),
            [Mood.Happy] = happyFrames,
            [Mood.Eating] = (SolidFrame(0xFFFFC107), SolidFrame(0xFFFFC107)),
            [Mood.Full] = (SolidFrame(0xFFFF7043), SolidFrame(0xFFFF7043)),
            [Mood.Stressed] = (SolidFrame(0xFFE53935), SolidFrame(0xFFE53935)),
        };

        return new Skin(
            "my-skin",
            "My Skin",
            moods,
            working ?? (TransparentFrame(), TransparentFrame()),
            worried ?? (TransparentFrame(), TransparentFrame()));
    }

    [Fact]
    public void GenerateFrames_NoOverlays_ReturnsTwoMoodFrames()
    {
        var skin = BuildSkin();

        var frames = skin.GenerateFrames(Mood.Happy);

        Assert.Equal(2, frames.Count);
        Assert.Equal(0xFF4CAF50u, frames[0][0, 0]);
        Assert.Equal(0xFF388E3Cu, frames[1][0, 0]);
    }

    [Fact]
    public void GenerateFrames_IsWorking_CompositesWorkingOverlayOnTop()
    {
        var workingOverlay = (SolidFrame(0xFF29B6F6), SolidFrame(0xFF29B6F6));
        var skin = BuildSkin(working: workingOverlay);

        var frames = skin.GenerateFrames(Mood.Happy, isWorking: true);

        Assert.Equal(0xFF29B6F6u, frames[0][0, 0]);
    }

    [Fact]
    public void GenerateFrames_IsWorried_CompositesWorriedOverlayOnTop()
    {
        var worriedOverlay = (SolidFrame(0xFF81D4FA), SolidFrame(0xFF81D4FA));
        var skin = BuildSkin(worried: worriedOverlay);

        var frames = skin.GenerateFrames(Mood.Happy, isWorried: true);

        Assert.Equal(0xFF81D4FAu, frames[0][0, 0]);
    }

    [Fact]
    public void GenerateFrames_NeitherOverlayFlagSet_IgnoresOverlayFrames()
    {
        var workingOverlay = (SolidFrame(0xFF29B6F6), SolidFrame(0xFF29B6F6));
        var skin = BuildSkin(working: workingOverlay);

        var frames = skin.GenerateFrames(Mood.Happy);

        Assert.Equal(0xFF4CAF50u, frames[0][0, 0]);
    }

    [Fact]
    public void FolderNameAndDisplayName_ExposedAsConstructed()
    {
        var skin = BuildSkin();

        Assert.Equal("my-skin", skin.FolderName);
        Assert.Equal("My Skin", skin.DisplayName);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~SkinTests`
Expected: FAIL — `Skin` doesn't exist yet (compile error).

- [ ] **Step 3: Implement `Skin`**

Create `src/ClaudePet/Skins/Skin.cs`:

```csharp
using ClaudePet.Models;
using ClaudePet.Rendering;

namespace ClaudePet.Skins;

public sealed class Skin
{
    public string FolderName { get; }
    public string DisplayName { get; }

    private readonly IReadOnlyDictionary<Mood, (PixelFrame Frame0, PixelFrame Frame1)> _moods;
    private readonly (PixelFrame Frame0, PixelFrame Frame1) _working;
    private readonly (PixelFrame Frame0, PixelFrame Frame1) _worried;

    // _moods is required to contain all 5 Mood values - SkinManifestParser
    // guarantees this for any manifest it successfully parses, and SkinLoader
    // never constructs a Skin from a manifest that failed to parse or from
    // frame files that failed to load.
    public Skin(
        string folderName,
        string displayName,
        IReadOnlyDictionary<Mood, (PixelFrame Frame0, PixelFrame Frame1)> moods,
        (PixelFrame Frame0, PixelFrame Frame1) working,
        (PixelFrame Frame0, PixelFrame Frame1) worried)
    {
        FolderName = folderName;
        DisplayName = displayName;
        _moods = moods;
        _working = working;
        _worried = worried;
    }

    public IReadOnlyList<PixelFrame> GenerateFrames(Mood mood, bool isWorking = false, bool isWorried = false)
    {
        var (frame0, frame1) = _moods[mood];

        if (isWorking)
        {
            frame0 = PixelCompositor.CompositeOver(frame0, _working.Frame0);
            frame1 = PixelCompositor.CompositeOver(frame1, _working.Frame1);
        }

        if (isWorried)
        {
            frame0 = PixelCompositor.CompositeOver(frame0, _worried.Frame0);
            frame1 = PixelCompositor.CompositeOver(frame1, _worried.Frame1);
        }

        return new[] { frame0, frame1 };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~SkinTests`
Expected: PASS (all 5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClaudePet/Skins/Skin.cs tests/ClaudePet.Tests/SkinTests.cs
git commit -m "feat: add Skin model with GenerateFrames matching PixelArtGenerator's shape"
```

---

### Task 8: `SkinLoader` — discovery and validation

**Files:**
- Create: `src/ClaudePet/Skins/SkinLoader.cs`

**Interfaces:**
- Consumes: `SkinManifestParser.Parse` (Task 6), `PixelGridParser.Parse` (Task 3), `PngFrameCodec.Decode` (Task 5), `Skin` (Task 7), `DebugLog` (`src/ClaudePet/Logging/DebugLog.cs`, existing — constructor `DebugLog(string filePath)`, method `void Write(string message)`).
- Produces: `sealed class SkinLoader` with constructor `SkinLoader(string skinsRoot, DebugLog log)` and method `IReadOnlyList<Skin> DiscoverSkins()` — consumed by Task 13 (`App.xaml.cs`).

Per the design spec's explicit testing decision, `SkinLoader`'s folder-scanning I/O is **not** directly unit tested here (consistent with `UsageReader`/`SubscriptionUsageReader`, which are also untested directly) — it's thin orchestration over the already-tested pure parsers above. It gets exercised indirectly by Task 9's `ExampleSkinGeneratorTests` and directly by the final live `run`-skill verification.

- [ ] **Step 1: Implement `SkinLoader`**

Create `src/ClaudePet/Skins/SkinLoader.cs`:

```csharp
using System.IO;
using ClaudePet.Logging;
using ClaudePet.Models;
using ClaudePet.Rendering;

namespace ClaudePet.Skins;

public sealed class SkinLoader
{
    private readonly string _skinsRoot;
    private readonly DebugLog _log;

    public SkinLoader(string skinsRoot, DebugLog log)
    {
        _skinsRoot = skinsRoot;
        _log = log;
    }

    public IReadOnlyList<Skin> DiscoverSkins()
    {
        var skins = new List<Skin>();
        if (!Directory.Exists(_skinsRoot))
            return skins;

        foreach (var dir in Directory.EnumerateDirectories(_skinsRoot))
        {
            var folderName = Path.GetFileName(dir);
            var skin = TryLoadSkin(dir, folderName);
            if (skin is not null)
                skins.Add(skin);
        }

        return skins;
    }

    private Skin? TryLoadSkin(string dir, string folderName)
    {
        var manifestPath = Path.Combine(dir, "skin.json");
        if (!File.Exists(manifestPath))
        {
            _log.Write($"SkinLoader: skipping '{folderName}' - skin.json not found");
            return null;
        }

        string manifestJson;
        try
        {
            manifestJson = File.ReadAllText(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write($"SkinLoader: skipping '{folderName}' - failed to read skin.json: {ex.Message}");
            return null;
        }

        string? manifestError = null;
        var manifest = SkinManifestParser.Parse(manifestJson, err => manifestError = err);
        if (manifest is null)
        {
            _log.Write($"SkinLoader: skipping '{folderName}' - {manifestError}");
            return null;
        }

        var moods = new Dictionary<Mood, (PixelFrame, PixelFrame)>();
        foreach (var (mood, paths) in manifest.Moods)
        {
            var frame0 = LoadFrame(dir, paths.Frame0, folderName);
            var frame1 = LoadFrame(dir, paths.Frame1, folderName);
            if (frame0 is null || frame1 is null)
                return null;
            moods[mood] = (frame0, frame1);
        }

        var workingFrame0 = LoadFrame(dir, manifest.WorkingOverlay.Frame0, folderName);
        var workingFrame1 = LoadFrame(dir, manifest.WorkingOverlay.Frame1, folderName);
        var worriedFrame0 = LoadFrame(dir, manifest.WorriedOverlay.Frame0, folderName);
        var worriedFrame1 = LoadFrame(dir, manifest.WorriedOverlay.Frame1, folderName);
        if (workingFrame0 is null || workingFrame1 is null || worriedFrame0 is null || worriedFrame1 is null)
            return null;

        return new Skin(
            folderName,
            manifest.DisplayName,
            moods,
            (workingFrame0, workingFrame1),
            (worriedFrame0, worriedFrame1));
    }

    private PixelFrame? LoadFrame(string skinDir, string relativePath, string folderName)
    {
        var path = Path.Combine(skinDir, relativePath);
        if (!File.Exists(path))
        {
            _log.Write($"SkinLoader: skipping '{folderName}' - frame file not found: {relativePath}");
            return null;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (extension == ".png")
            {
                var bytes = File.ReadAllBytes(path);
                string? error = null;
                var frame = PngFrameCodec.Decode(bytes, err => error = err);
                if (frame is null)
                    _log.Write($"SkinLoader: skipping '{folderName}' - {relativePath}: {error}");
                return frame;
            }

            if (extension == ".json")
            {
                var text = File.ReadAllText(path);
                string? error = null;
                var frame = PixelGridParser.Parse(text, err => error = err);
                if (frame is null)
                    _log.Write($"SkinLoader: skipping '{folderName}' - {relativePath}: {error}");
                return frame;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write($"SkinLoader: skipping '{folderName}' - failed to read {relativePath}: {ex.Message}");
            return null;
        }

        _log.Write($"SkinLoader: skipping '{folderName}' - unrecognized frame file extension: {relativePath}");
        return null;
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build src/ClaudePet/ClaudePet.csproj`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add src/ClaudePet/Skins/SkinLoader.cs
git commit -m "feat: add SkinLoader for skin folder discovery and validation"
```

---

### Task 9: `ExampleSkinGenerator` — starter skin seeded from the built-in art

**Files:**
- Create: `src/ClaudePet/Skins/ExampleSkinGenerator.cs`
- Test: `tests/ClaudePet.Tests/ExampleSkinGeneratorTests.cs`

**Interfaces:**
- Consumes: `PixelArtGenerator.GenerateFrames`, `PixelArtGenerator.GenerateWorkingOverlayFrame`, `PixelArtGenerator.GenerateWorriedOverlayFrame` (Task 1), `PngFrameCodec.Encode` (Task 5), `PixelGridWriter.Write` (Task 4), `SkinLoader` (Task 8, test-only, to verify the generated skin actually loads), `DebugLog` (existing).
- Produces: `static class ExampleSkinGenerator` with method `static void EnsureExampleSkin(string skinsRoot, DebugLog log)` — consumed by Task 13 (`App.xaml.cs`).

This is the one place `SkinLoader` gets exercised in a test — the test's subject is `ExampleSkinGenerator`'s own correctness (does it produce a skin that actually loads?), using `SkinLoader` only as the verification oracle, not testing `SkinLoader`'s own edge cases directly (per Task 8's note).

- [ ] **Step 1: Write the failing test**

Create `tests/ClaudePet.Tests/ExampleSkinGeneratorTests.cs`:

```csharp
using ClaudePet.Logging;
using ClaudePet.Models;
using ClaudePet.Skins;

namespace ClaudePet.Tests;

public class ExampleSkinGeneratorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ClaudePetExampleSkinTests_" + Guid.NewGuid());

    public ExampleSkinGeneratorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private DebugLog Log() => new(Path.Combine(_dir, "debug.log"));

    [Fact]
    public void EnsureExampleSkin_CreatesLoadableValidSkin()
    {
        var log = Log();

        ExampleSkinGenerator.EnsureExampleSkin(_dir, log);
        var skins = new SkinLoader(_dir, log).DiscoverSkins();

        Assert.Single(skins);
        Assert.Equal("example", skins[0].FolderName);
        Assert.Equal("Example (copy me!)", skins[0].DisplayName);
    }

    [Fact]
    public void EnsureExampleSkin_GeneratedSkin_ProducesTwo16x16FramesForEveryMood()
    {
        var log = Log();
        ExampleSkinGenerator.EnsureExampleSkin(_dir, log);
        var skin = new SkinLoader(_dir, log).DiscoverSkins()[0];

        foreach (Mood mood in Enum.GetValues<Mood>())
        {
            var frames = skin.GenerateFrames(mood);
            Assert.Equal(2, frames.Count);
            Assert.Equal(16, frames[0].Width);
            Assert.Equal(16, frames[0].Height);
        }
    }

    [Fact]
    public void EnsureExampleSkin_AlreadyExists_DoesNotOverwriteOrThrow()
    {
        var log = Log();
        ExampleSkinGenerator.EnsureExampleSkin(_dir, log);
        var exampleDir = Path.Combine(_dir, "example");
        var manifestPath = Path.Combine(exampleDir, "skin.json");
        var originalWriteTime = File.GetLastWriteTimeUtc(manifestPath);

        ExampleSkinGenerator.EnsureExampleSkin(_dir, log);

        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(manifestPath));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~ExampleSkinGeneratorTests`
Expected: FAIL — `ExampleSkinGenerator` doesn't exist yet (compile error).

- [ ] **Step 3: Implement `ExampleSkinGenerator`**

Create `src/ClaudePet/Skins/ExampleSkinGenerator.cs`:

```csharp
using System.IO;
using System.Text.Json;
using ClaudePet.Logging;
using ClaudePet.Models;
using ClaudePet.Rendering;

namespace ClaudePet.Skins;

public static class ExampleSkinGenerator
{
    // A couple of moods are written as PNG and the rest (plus both overlays)
    // as JSON grids, purely to demonstrate that a skin can freely mix both
    // frame formats - see docs/superpowers/specs/2026-08-13-pet-skin-system-design.md.
    private static readonly Mood[] PngMoods = { Mood.NoSession, Mood.Happy };
    private static readonly Mood[] JsonMoods = { Mood.Eating, Mood.Full, Mood.Stressed };

    public static void EnsureExampleSkin(string skinsRoot, DebugLog log)
    {
        var exampleDir = Path.Combine(skinsRoot, "example");
        if (Directory.Exists(exampleDir))
            return;

        try
        {
            Directory.CreateDirectory(exampleDir);

            var moodPaths = new Dictionary<string, object>();
            foreach (var mood in PngMoods)
                moodPaths[mood.ToString()] = WriteMoodPng(exampleDir, mood);
            foreach (var mood in JsonMoods)
                moodPaths[mood.ToString()] = WriteMoodJson(exampleDir, mood);

            var overlays = new Dictionary<string, object>
            {
                ["working"] = WriteOverlayJson(exampleDir, "working", PixelArtGenerator.GenerateWorkingOverlayFrame),
                ["worried"] = WriteOverlayJson(exampleDir, "worried", PixelArtGenerator.GenerateWorriedOverlayFrame),
            };

            var manifest = new
            {
                displayName = "Example (copy me!)",
                moods = moodPaths,
                overlays,
            };

            File.WriteAllText(
                Path.Combine(exampleDir, "skin.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Write($"ExampleSkinGenerator: failed to write example skin: {ex.Message}");
        }
    }

    private static object WriteMoodPng(string dir, Mood mood)
    {
        var frames = PixelArtGenerator.GenerateFrames(mood);
        var name = mood.ToString().ToLowerInvariant();
        var frame0Name = $"{name}_0.png";
        var frame1Name = $"{name}_1.png";
        File.WriteAllBytes(Path.Combine(dir, frame0Name), PngFrameCodec.Encode(frames[0]));
        File.WriteAllBytes(Path.Combine(dir, frame1Name), PngFrameCodec.Encode(frames[1]));
        return new { frame0 = frame0Name, frame1 = frame1Name };
    }

    private static object WriteMoodJson(string dir, Mood mood)
    {
        var frames = PixelArtGenerator.GenerateFrames(mood);
        var name = mood.ToString().ToLowerInvariant();
        var frame0Name = $"{name}_0.json";
        var frame1Name = $"{name}_1.json";
        File.WriteAllText(Path.Combine(dir, frame0Name), PixelGridWriter.Write(frames[0]));
        File.WriteAllText(Path.Combine(dir, frame1Name), PixelGridWriter.Write(frames[1]));
        return new { frame0 = frame0Name, frame1 = frame1Name };
    }

    private static object WriteOverlayJson(string dir, string name, Func<bool, PixelFrame> generate)
    {
        var frame0Name = $"{name}_0.json";
        var frame1Name = $"{name}_1.json";
        File.WriteAllText(Path.Combine(dir, frame0Name), PixelGridWriter.Write(generate(false)));
        File.WriteAllText(Path.Combine(dir, frame1Name), PixelGridWriter.Write(generate(true)));
        return new { frame0 = frame0Name, frame1 = frame1Name };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~ExampleSkinGeneratorTests`
Expected: PASS (all 3 tests).

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test tests/ClaudePet.Tests`
Expected: PASS — every test in the suite.

- [ ] **Step 6: Commit**

```bash
git add src/ClaudePet/Skins/ExampleSkinGenerator.cs tests/ClaudePet.Tests/ExampleSkinGeneratorTests.cs
git commit -m "feat: add ExampleSkinGenerator to seed a starter skin from built-in art"
```

---

### Task 10: `AppSettings.ActiveSkinName`

**Files:**
- Modify: `src/ClaudePet/Settings/AppSettings.cs`
- Modify: `tests/ClaudePet.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `AppSettings.ActiveSkinName` (`string?`, default `null`) — consumed by Task 12 (`TrayIconManager`) and Task 13 (`App.xaml.cs`).

- [ ] **Step 1: Write the failing tests**

In `tests/ClaudePet.Tests/SettingsStoreTests.cs`, update `Load_FileDoesNotExist_ReturnsDefaults` to also assert the new field's default:

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
        Assert.Null(settings.ActiveSkinName);
    }
```

Add this new test anywhere after `SaveThenLoad_RoundTripsShowSubscriptionUsage`:

```csharp
    [Fact]
    public void SaveThenLoad_RoundTripsActiveSkinName()
    {
        var store = new SettingsStore(FilePath);
        var original = new AppSettings { ActiveSkinName = "my-cool-skin" };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal("my-cool-skin", loaded.ActiveSkinName);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~SettingsStoreTests`
Expected: FAIL — `AppSettings` has no `ActiveSkinName` member (compile error).

- [ ] **Step 3: Add the field**

Replace the full contents of `src/ClaudePet/Settings/AppSettings.cs`:

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

    // Folder name (under %LOCALAPPDATA%\ClaudePet\skins\) of the active custom
    // skin. null always means the built-in Default skin - never persist an
    // empty string as a sentinel. See
    // docs/superpowers/specs/2026-08-13-pet-skin-system-design.md.
    public string? ActiveSkinName { get; init; }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ClaudePet.Tests --filter FullyQualifiedName~SettingsStoreTests`
Expected: PASS (all tests including the 2 touched by this task).

- [ ] **Step 5: Commit**

```bash
git add src/ClaudePet/Settings/AppSettings.cs tests/ClaudePet.Tests/SettingsStoreTests.cs
git commit -m "feat: add ActiveSkinName setting"
```

---

### Task 11: `PetWindow` — fork frame source between the active skin and the built-in generator

**Files:**
- Modify: `src/ClaudePet/PetWindow.xaml.cs`

**Interfaces:**
- Consumes: `Skin.GenerateFrames` (Task 7).
- Produces: `PetWindow.SetSkin(Skin? skin)` — consumed by Task 13 (`App.xaml.cs`).

No new tests — `PetWindow` is a `Window` with no existing direct test coverage (verified via the other `Set*` methods already on this class), consistent with the rest of this file. Verified via build and the final live `run`-skill check.

- [ ] **Step 1: Add the `_activeSkin` field, `SetSkin` method, and update `RegenerateFrames`**

In `src/ClaudePet/PetWindow.xaml.cs`, add `using ClaudePet.Skins;` to the top of the file alongside the existing usings:

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
using ClaudePet.Skins;
```

Add the `_activeSkin` field next to the other mood/overlay fields:

```csharp
    private Mood _currentMood = Mood.NoSession;
    private bool _isWorking;
    private bool _isWorried;
    private Skin? _activeSkin;
```

Add `SetSkin` next to the other `Set*` methods (after `SetWorried`, before `RegenerateFrames`):

```csharp
    public void SetSkin(Skin? skin)
    {
        if (ReferenceEquals(_activeSkin, skin))
            return;
        _activeSkin = skin;
        RegenerateFrames();
    }
```

Replace the `RegenerateFrames` method to consult the active skin first:

```csharp
    private void RegenerateFrames()
    {
        _currentFrames = _activeSkin?.GenerateFrames(_currentMood, _isWorking, _isWorried)
            ?? PixelArtGenerator.GenerateFrames(_currentMood, _isWorking, _isWorried);
        _frameIndex = 0;
        Render();
    }
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build src/ClaudePet/ClaudePet.csproj`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Run the full test suite to confirm no regressions**

Run: `dotnet test tests/ClaudePet.Tests`
Expected: PASS — every test in the suite (this task touches no tested logic directly, but confirms the build-wide change is inert).

- [ ] **Step 4: Commit**

```bash
git add src/ClaudePet/PetWindow.xaml.cs
git commit -m "feat: let PetWindow render frames from an active custom Skin"
```

---

### Task 12: `TrayIconManager` — "Skin" submenu

**Files:**
- Modify: `src/ClaudePet/Tray/TrayIconManager.cs`

**Interfaces:**
- Consumes: nothing new (uses only `(string FolderName, string DisplayName)` tuples and `string?` — deliberately decoupled from `ClaudePet.Skins`; `App.xaml.cs` owns all `Skin` lookups).
- Produces: `TrayIconManager.PopulateSkinMenu(IReadOnlyList<(string FolderName, string DisplayName)> skins, string? activeSkinName)` and `event Action<string?>? SkinSelected` (fires with the selected folder name, or `null` for Default) — consumed by Task 13 (`App.xaml.cs`).

No new tests — `TrayIconManager` has no existing direct test coverage (it wraps `System.Windows.Forms.NotifyIcon`), consistent with the rest of this class. Verified via build and the final live `run`-skill check.

- [ ] **Step 1: Add the `Skin` submenu, `PopulateSkinMenu`, `SelectSkin`, and the `SkinSelected` event**

In `src/ClaudePet/Tray/TrayIconManager.cs`, add the new fields next to the existing ones:

```csharp
    private readonly ToolStripMenuItem _dragItem;
    private readonly ToolStripMenuItem _skinMenu;
    private readonly List<(ToolStripMenuItem Item, string? FolderName)> _skinItems = new();
    private bool _dragMode;
```

Add the `SkinSelected` event next to the existing `SubscriptionUsageToggled` event:

```csharp
    public event Action<bool>? SubscriptionUsageToggled;
    public event Action<string?>? SkinSelected;
```

In the constructor, create `_skinMenu` (empty until `PopulateSkinMenu` is called) and add it to the menu, between `subscriptionUsageItem` and the separator:

```csharp
        _skinMenu = new ToolStripMenuItem("Skin");

        var quitItem = new ToolStripMenuItem("Quit", null, (_, _) => System.Windows.Application.Current.Shutdown());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_dragItem);
        menu.Items.Add(runAtStartupItem);
        menu.Items.Add(subscriptionUsageItem);
        menu.Items.Add(_skinMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitItem);
```

Add the three new methods after `UpdateSubscriptionUsage`:

```csharp
    public void PopulateSkinMenu(IReadOnlyList<(string FolderName, string DisplayName)> skins, string? activeSkinName)
    {
        _skinMenu.DropDownItems.Clear();
        _skinItems.Clear();

        AddSkinItem("Default", null, activeSkinName);
        foreach (var (folderName, displayName) in skins)
            AddSkinItem(displayName, folderName, activeSkinName);
    }

    private void AddSkinItem(string label, string? folderName, string? activeSkinName)
    {
        var item = new ToolStripMenuItem(label, null, (_, _) => SelectSkin(folderName))
        {
            Checked = folderName == activeSkinName
        };
        _skinMenu.DropDownItems.Add(item);
        _skinItems.Add((item, folderName));
    }

    private void SelectSkin(string? folderName)
    {
        foreach (var (item, itemFolderName) in _skinItems)
            item.Checked = itemFolderName == folderName;

        var settings = _settingsStore.Load() with { ActiveSkinName = folderName };
        _settingsStore.Save(settings);

        SkinSelected?.Invoke(folderName);
    }
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build src/ClaudePet/ClaudePet.csproj`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Run the full test suite to confirm no regressions**

Run: `dotnet test tests/ClaudePet.Tests`
Expected: PASS — every test in the suite.

- [ ] **Step 4: Commit**

```bash
git add src/ClaudePet/Tray/TrayIconManager.cs
git commit -m "feat: add Skin submenu to the tray icon"
```

---

### Task 13: Wire skin discovery, selection, and fallback into app startup

**Files:**
- Modify: `src/ClaudePet/App.xaml.cs`

**Interfaces:**
- Consumes: `ExampleSkinGenerator.EnsureExampleSkin` (Task 9), `SkinLoader` (Task 8), `Skin` (Task 7), `AppSettings.ActiveSkinName` (Task 10), `PetWindow.SetSkin` (Task 11), `TrayIconManager.PopulateSkinMenu`/`SkinSelected` (Task 12).
- Produces: nothing consumed by later tasks (this is the final integration point).

No new tests — `App.xaml.cs` has no existing direct test coverage (it's the WPF `Application` startup sequence), consistent with the rest of this file. Verified via build, the full test suite, and the final live `run`-skill check.

- [ ] **Step 1: Add the `using` and the skin-wiring block**

In `src/ClaudePet/App.xaml.cs`, add `using ClaudePet.Skins;` to the top of the file alongside the existing usings:

```csharp
using System.IO;
using System.Windows;
using ClaudePet.Logging;
using ClaudePet.Models;
using ClaudePet.Services;
using ClaudePet.Settings;
using ClaudePet.Skins;
using ClaudePet.Tray;
```

Insert the following block into `OnStartup`, immediately after `_moodStateMachine = new MoodStateMachine();` and before the existing `var projectsRoot = ...` line — i.e. this exact existing snippet:

```csharp
            _trayIconManager = new TrayIconManager(_petWindow, settingsStore, log);
            _moodStateMachine = new MoodStateMachine();

            var projectsRoot = Path.Combine(
```

becomes:

```csharp
            _trayIconManager = new TrayIconManager(_petWindow, settingsStore, log);
            _moodStateMachine = new MoodStateMachine();

            var skinsRoot = Path.Combine(appDataDir, "skins");
            ExampleSkinGenerator.EnsureExampleSkin(skinsRoot, log);

            var discoveredSkins = new SkinLoader(skinsRoot, log).DiscoverSkins();
            var activeSkinName = settingsStore.Load().ActiveSkinName;
            Skin? activeSkin = null;
            if (activeSkinName is not null)
            {
                activeSkin = discoveredSkins.FirstOrDefault(s => s.FolderName == activeSkinName);
                if (activeSkin is null)
                {
                    log.Write($"Persisted ActiveSkinName '{activeSkinName}' is no longer valid; falling back to Default.");
                    activeSkinName = null;
                    settingsStore.Save(settingsStore.Load() with { ActiveSkinName = null });
                }
            }
            _petWindow.SetSkin(activeSkin);

            _trayIconManager.PopulateSkinMenu(
                discoveredSkins.Select(s => (s.FolderName, s.DisplayName)).ToList(),
                activeSkinName);

            _trayIconManager.SkinSelected += folderName =>
            {
                var skin = folderName is null ? null : discoveredSkins.FirstOrDefault(s => s.FolderName == folderName);
                _petWindow.SetSkin(skin);
            };

            var projectsRoot = Path.Combine(
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build src/ClaudePet/ClaudePet.csproj`
Expected: Build succeeds with no errors. If `FirstOrDefault`/`Select`/`ToList` fail to resolve, add `using System.Linq;` to the file's usings (the project has `ImplicitUsings` enabled, which normally covers this, but confirm if the build reports otherwise).

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test tests/ClaudePet.Tests`
Expected: PASS — every test in the suite.

- [ ] **Step 4: Commit**

```bash
git add src/ClaudePet/App.xaml.cs
git commit -m "feat: wire skin discovery, selection, and fallback into app startup"
```

---

## Final verification (after all tasks)

- [ ] Run the full test suite one more time: `dotnet test tests/ClaudePet.Tests` — expect all tests passing.
- [ ] Use the `run` skill to launch the app, confirm `skins\example\` was generated under `%LOCALAPPDATA%\ClaudePet\`, confirm the tray menu's "Skin" submenu lists "Default" and "Example (copy me!)", and confirm selecting "Example (copy me!)" swaps the pet's appearance live (it should look identical to Default, since it's derived from the same art) with no errors in `debug.log`.

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

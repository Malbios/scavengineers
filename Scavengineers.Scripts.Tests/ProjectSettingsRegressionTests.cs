namespace Scavengineers.Scripts.Tests;

/// <summary>Guards against a bug this project already hit once: editing project.godot's display
/// settings through the Godot editor's Project Settings UI (bumping the window resolution) can
/// silently drop an existing, unrelated setting in the same section — here,
/// window/stretch/aspect="expand" vanished the moment window/stretch/scale_mode="integer" was
/// added, desyncing the mouse-to-viewport coordinate transform used for all GUI hit-testing
/// (drag-and-drop, right-click, window-title-bar dragging) while leaving keyboard input
/// completely unaffected — and printing no error at all, making it look like a code regression.
/// Reads the real project.godot file's text directly (no Godot runtime needed), same pattern as
/// WorldSceneRegressionTests.</summary>
public class ProjectSettingsRegressionTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Scavengineers.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException($"Could not locate repo root (Scavengineers.sln) from {AppContext.BaseDirectory}");
    }

    private static string[] DisplaySectionLines()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "project.godot"));
        var sectionIndex = Array.IndexOf(lines, "[display]");
        Assert.True(sectionIndex >= 0, "Could not find [display] section in project.godot");

        return lines.Skip(sectionIndex + 1).TakeWhile(l => !l.StartsWith('[')).ToArray();
    }

    [Fact]
    public void Display_HasBothStretchAspectAndScaleMode_NeitherWasDroppedWhenTheOtherWasAdded()
    {
        var display = DisplaySectionLines();

        // The exact regression: scale_mode was added via the editor's Project Settings UI and
        // aspect silently disappeared from the same edit — assert both are present together
        // (not their specific values) so this catches either one vanishing in the future too.
        Assert.Contains(display, l => l.StartsWith("window/stretch/aspect="));
        Assert.Contains(display, l => l.StartsWith("window/stretch/scale_mode="));
    }

    [Fact]
    public void Display_StretchSettings_MatchTheKnownGoodConfiguration()
    {
        var display = DisplaySectionLines();

        Assert.Contains("window/stretch/mode=\"canvas_items\"", display);
        Assert.Contains("window/stretch/aspect=\"expand\"", display);
        Assert.Contains("window/stretch/scale_mode=\"integer\"", display);
    }
}

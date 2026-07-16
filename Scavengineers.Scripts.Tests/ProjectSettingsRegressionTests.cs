namespace Scavengineers.Scripts.Tests;

/// <summary>Guards against a bug this project already hit once: adding
/// window/stretch/scale_mode="integer" via the Godot editor's Project Settings UI (while bumping
/// the window resolution to 1920x1080) desynced the mouse-to-viewport coordinate transform used
/// for all GUI hit-testing — every clickable rect rendered in the correct visual spot but its
/// actual hit-test area was offset by roughly a third of the screen, so drag-and-drop/right-click/
/// window-title-bar dragging all silently missed their targets. Keyboard input (Tab) was
/// completely unaffected and nothing printed to the console, making it look exactly like a code
/// regression in Player.cs/InventorySlotUI.cs — restoring the separately-dropped
/// window/stretch/aspect="expand" did NOT fix it; only removing scale_mode="integer" entirely did.
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
    public void Display_DoesNotSetStretchScaleMode_IntegerScaleModeCausedAGuiHitTestOffsetOnce()
    {
        var display = DisplaySectionLines();

        Assert.DoesNotContain(display, l => l.StartsWith("window/stretch/scale_mode="));
    }

    [Fact]
    public void Display_StretchSettings_MatchTheKnownGoodConfiguration()
    {
        var display = DisplaySectionLines();

        Assert.Contains("window/stretch/mode=\"canvas_items\"", display);
        Assert.Contains("window/stretch/aspect=\"expand\"", display);
    }
}

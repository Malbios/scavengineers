namespace Scavengineers.Scripts.Tests;

/// <summary>Guards against a bug this project already hit once: window/stretch/scale_mode="integer"
/// desynced the mouse-to-viewport hit-test transform (clicks looked right but landed a third of
/// the screen off) while keyboard input and the console stayed silent, making it look like a code
/// regression rather than a project-settings one. Reads project.godot's text directly, no Godot
/// runtime needed.</summary>
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

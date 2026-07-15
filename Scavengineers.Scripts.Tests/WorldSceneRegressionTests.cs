namespace Scavengineers.Scripts.Tests;

/// <summary>Guards against a bug this project already hit once (commit bdcd15a): a .tscn
/// [node ...] header silently accepts and ignores properties (like collision_layer) that Godot's
/// parser only recognizes as separate property lines below the header, so Ceiling/FloorAimHelper
/// stayed on the default collision layer and kept physically blocking movement. Reads the real
/// scene file's text directly (no Godot runtime needed) rather than a NodeTest, since
/// Scavengineers.NodeTests is its own separate, scene-less Godot project and can't load
/// res://Scenes/World.tscn at all (see PlayerTestHarness's own doc comment for the same
/// constraint).</summary>
public class WorldSceneRegressionTests
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

    [Theory]
    [InlineData("Ceiling")]
    [InlineData("FloorAimHelper")]
    public void HomeShipAimHelperNode_HasCollisionLayerAsARealPropertyLine_NotSilentlyIgnoredInTheHeader(string nodeName)
    {
        var scenePath = Path.Combine(RepoRoot(), "Scenes", "World.tscn");
        var lines = File.ReadAllLines(scenePath);

        var headerIndex = Array.FindIndex(lines, l => l.StartsWith($"[node name=\"{nodeName}\"") && l.Contains("parent=\"HomeShip\""));
        Assert.True(headerIndex >= 0, $"Could not find HomeShip/{nodeName}'s [node] header in World.tscn");
        Assert.DoesNotContain("collision_layer", lines[headerIndex]);

        var propertyLines = lines.Skip(headerIndex + 1).TakeWhile(l => !l.StartsWith("["));
        Assert.Contains(propertyLines, l => l.Trim() == "collision_layer = 2");
    }
}

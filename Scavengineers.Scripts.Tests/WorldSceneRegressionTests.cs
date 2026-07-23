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

    /// <summary>Both Stations instance one Scenes/Station.tscn, so every SaveId in that scene is
    /// authored once and inherited twice. A saveable node added there without a matching override
    /// on the Station2 instance gives two live nodes the same save id, and SaveManager keys purely
    /// by id — the second write silently wins and one Station's state vanishes into the other's.
    /// That fails as a wrong-state bug on load, not as an error, so it needs catching here.</summary>
    [Fact]
    public void EverySaveIdAuthoredInStationScene_IsOverriddenForTheStation2Instance()
    {
        var stationIds = SaveIdsByNode(Path.Combine(RepoRoot(), "Scenes", "Station.tscn"), parent: null);
        Assert.NotEmpty(stationIds);

        var station2Ids = SaveIdsByNode(Path.Combine(RepoRoot(), "Scenes", "World.tscn"), parent: "Station2");

        foreach (var (node, inheritedId) in stationIds)
        {
            Assert.True(
                station2Ids.TryGetValue(node, out var overriddenId),
                $"Station.tscn's '{node}' has SaveId '{inheritedId}' but the Station2 instance in World.tscn never overrides it, so both Stations would save under that one id");
            Assert.NotEqual(inheritedId, overriddenId);
        }
    }

    /// <summary>Maps node name to its authored SaveId, over the blocks whose parent matches — null
    /// meaning "a direct child of the scene root", which is where every saveable Station node
    /// lives.</summary>
    private static Dictionary<string, string> SaveIdsByNode(string scenePath, string? parent)
    {
        var wantedParent = parent is null ? "\"" + "." + "\"" : $"\"{parent}\"";
        var result = new Dictionary<string, string>();
        var lines = File.ReadAllLines(scenePath);
        var currentNode = (string?)null;

        foreach (var line in lines)
        {
            if (line.StartsWith("[node "))
            {
                var name = Between(line, "name=\"", "\"");
                var nodeParent = line.Contains("parent=") ? line[line.IndexOf("parent=", StringComparison.Ordinal)..] : null;
                currentNode = nodeParent is not null && nodeParent.StartsWith($"parent={wantedParent}", StringComparison.Ordinal) ? name : null;
                continue;
            }

            if (currentNode is not null && line.StartsWith("SaveId = "))
            {
                result[currentNode] = line["SaveId = ".Length..].Trim('"');
            }
        }

        return result;
    }

    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal) + start.Length;
        return source[from..source.IndexOf(end, from, StringComparison.Ordinal)];
    }
}

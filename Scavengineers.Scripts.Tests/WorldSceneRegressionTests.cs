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

    /// <summary>Every destination in World.tscn instances a shared scene — 5 Derelicts off
    /// Derelict.tscn, 2 Stations off Station.tscn — so a SaveId authored in the shared scene is
    /// inherited by every instance that doesn't override it. SaveManager keys purely by id, so two
    /// live nodes sharing one means the second capture silently overwrites the first and every
    /// instance loads the same state back. Nothing errors; the wrong ship just has your walls.
    ///
    /// This caught Derelict.tscn's Deck2/Floor2 shipping SaveId "derelict_build_target_1_deck2"
    /// with no per-derelict override, so all five wrecks' second-deck build state collided.</summary>
    [Fact]
    public void NoTwoNodesInTheWorld_ShareASaveId_AcrossInstancedDestinationScenes()
    {
        var world = Path.Combine(RepoRoot(), "Scenes", "World.tscn");
        var owners = new Dictionary<string, string>();

        void Claim(string saveId, string owner)
        {
            Assert.False(
                owners.TryGetValue(saveId, out var existing),
                $"SaveId '{saveId}' is used by both '{existing}' and '{owner}' — one will silently overwrite the other on save");
            owners[saveId] = owner;
        }

        var instances = InstancedScenes(world).ToList();
        var instanceNames = instances.Select(i => i.Instance).ToHashSet();

        // Nodes authored inline in World.tscn itself (HomeShip, the two shared airlocks, ...).
        // An instance's override blocks live in this same file and are counted below instead.
        foreach (var (node, saveId) in SaveIdsByNode(world, instance: null))
        {
            if (!instanceNames.Contains(node.Split('/')[0]))
            {
                Claim(saveId, node);
            }
        }

        foreach (var (instanceName, scenePath) in instances)
        {
            var overrides = SaveIdsByNode(world, instance: instanceName);
            var inherited = SaveIdsByNode(Path.Combine(RepoRoot(), scenePath), instance: null);
            Assert.NotEmpty(inherited);

            foreach (var (node, inheritedId) in inherited)
            {
                Claim(overrides.TryGetValue(node, out var overridden) ? overridden : inheritedId, $"{instanceName}/{node}");
            }
        }
    }

    /// <summary>Maps each root-level instance in World.tscn to the scene file it instances, by
    /// resolving its ExtResource id against the header.</summary>
    private static IEnumerable<(string Instance, string ScenePath)> InstancedScenes(string worldPath)
    {
        var lines = File.ReadAllLines(worldPath);
        var scenesByResourceId = lines
            .Where(l => l.StartsWith("[ext_resource type=\"PackedScene\""))
            .ToDictionary(l => Between(l, "id=\"", "\""), l => Between(l, "path=\"res://", "\""));

        foreach (var line in lines.Where(l => l.StartsWith("[node ") && l.Contains("parent=\".\"") && l.Contains("instance=ExtResource(")))
        {
            var resourceId = Between(line, "instance=ExtResource(\"", "\"");
            if (scenesByResourceId.TryGetValue(resourceId, out var scenePath))
            {
                yield return (Between(line, "name=\"", "\""), scenePath);
            }
        }
    }

    /// <summary>Maps a node's path (relative to the scene root, or to the named instance) to its
    /// authored SaveId. `instance` null means the scene's own nodes rather than an instance's
    /// override blocks.</summary>
    private static Dictionary<string, string> SaveIdsByNode(string scenePath, string? instance)
    {
        var result = new Dictionary<string, string>();
        var currentNode = (string?)null;

        foreach (var line in File.ReadAllLines(scenePath))
        {
            if (line.StartsWith("[node "))
            {
                currentNode = null;

                // An instance's own header carries no SaveId, and root-level nodes of World.tscn
                // that *are* instances are handled by the caller, not here.
                if (!line.Contains("parent=\"") || line.Contains("instance=ExtResource("))
                {
                    continue;
                }

                var parent = Between(line, "parent=\"", "\"");
                var name = Between(line, "name=\"", "\"");

                // "." is the scene root; otherwise parent is a path below it. For an instance's
                // overrides the parent is prefixed with the instance name, which is stripped so
                // both sides key on the same scene-relative path.
                if (instance is null)
                {
                    currentNode = parent == "." ? name : $"{parent}/{name}";
                }
                else if (parent == instance)
                {
                    currentNode = name;
                }
                else if (parent.StartsWith($"{instance}/", StringComparison.Ordinal))
                {
                    currentNode = $"{parent[(instance.Length + 1)..]}/{name}";
                }

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

using System.Text.Json;

using Scavengineers.Scripts.Travel;

namespace Scavengineers.Scripts.Tests;

/// <summary>Guards against a bug this project already hit once: a .tscn [node ...] header
/// silently accepts and ignores properties (like collision_layer) that Godot's parser only
/// recognizes as separate property lines below the header, so Ceiling/FloorAimHelper stayed on
/// the default collision layer and kept physically blocking movement. Reads the scene file's text
/// directly — Scavengineers.NodeTests is its own scene-less Godot project and can't load
/// res://Scenes/World.tscn at all.</summary>
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

    /// <summary>All five Derelicts share one Derelict.tscn, so a SaveId not overridden per-
    /// destination in destinations.json collides across every instance — SaveManager keys purely
    /// by id, so the wrong wreck silently ends up with another wreck's saved state. This caught
    /// Derelict.tscn's Deck2/Floor2 shipping one shared SaveId with no per-derelict override.</summary>
    [Fact]
    public void NoTwoDestinations_ShareASaveId_OnceTheirJsonOverridesAreApplied()
    {
        var owners = new Dictionary<string, string>();

        void Claim(string saveId, string owner)
        {
            Assert.False(
                owners.TryGetValue(saveId, out var existing),
                $"SaveId '{saveId}' is used by both '{existing}' and '{owner}' — one will silently overwrite the other on save");
            owners[saveId] = owner;
        }

        // The Home Ship and the two shared airlocks are still authored inline.
        foreach (var (node, saveId) in SaveIdsByNode(Path.Combine(RepoRoot(), "Scenes", "World.tscn")))
        {
            Claim(saveId, node);
        }

        var destinationsJson = Path.Combine(RepoRoot(), "Data", "destinations.json");
        var destinations = JsonSerializer.Deserialize<List<DestinationCatalog.DestinationDefinition>>(File.ReadAllText(destinationsJson));
        Assert.NotNull(destinations);
        Assert.NotEmpty(destinations);

        foreach (var destination in destinations)
        {
            Assert.False(string.IsNullOrWhiteSpace(destination.Scene), $"'{destination.Id}' names no scene, so nothing would be instantiated for it");

            var scene = ReadScene(ScenePath(destination.Scene));
            var effective = scene.SaveIds;

            foreach (var (nodePath, properties) in destination.Overrides)
            {
                // A node path that doesn't exist is the typo that matters: DestinationManager warns
                // and moves on, leaving every node it meant to configure on its scene default.
                Assert.Contains(nodePath, scene.Paths);

                if (properties.TryGetValue("SaveId", out var overridden))
                {
                    effective[nodePath] = overridden.GetString() ?? "";
                }
            }

            Assert.NotEmpty(effective);

            foreach (var (node, saveId) in effective)
            {
                Claim(saveId, $"{destination.Id}/{node}");
            }
        }
    }

    private static string ScenePath(string resPath) =>
        Path.Combine(RepoRoot(), resPath.Replace("res://", "").Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Every node path in a scene, plus those that author a SaveId. Follows scene
    /// inheritance: Station2.tscn's root instances Station.tscn, so it inherits every node and
    /// SaveId authored there and overrides three of them.</summary>
    private sealed record SceneNodes(HashSet<string> Paths, Dictionary<string, string> SaveIds);

    private static SceneNodes ReadScene(string scenePath)
    {
        var lines = File.ReadAllLines(scenePath);

        // An inherited scene's root node carries `instance=ExtResource(...)` and no parent.
        var root = lines.FirstOrDefault(l => l.StartsWith("[node ") && !l.Contains("parent=\""));
        var result = root is not null && root.Contains("instance=ExtResource(")
            ? ReadScene(ScenePath(BaseSceneOf(lines, Between(root, "instance=ExtResource(\"", "\""))))
            : new SceneNodes([], new Dictionary<string, string>());

        var currentNode = (string?)null;

        foreach (var line in lines)
        {
            if (line.StartsWith("[node "))
            {
                currentNode = null;
                if (line.Contains("parent=\""))
                {
                    var parent = Between(line, "parent=\"", "\"");
                    var name = Between(line, "name=\"", "\"");
                    currentNode = parent == "." ? name : $"{parent}/{name}";
                    result.Paths.Add(currentNode);
                }

                continue;
            }

            if (currentNode is not null && line.StartsWith("SaveId = "))
            {
                result.SaveIds[currentNode] = line["SaveId = ".Length..].Trim('"');
            }
        }

        return result;
    }

    private static Dictionary<string, string> SaveIdsByNode(string scenePath) => ReadScene(scenePath).SaveIds;

    private static string BaseSceneOf(string[] lines, string resourceId)
    {
        var declaration = lines.First(l => l.StartsWith("[ext_resource type=\"PackedScene\"") && l.Contains($"id=\"{resourceId}\""));
        return Between(declaration, "path=\"", "\"");
    }

    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal) + start.Length;
        return source[from..source.IndexOf(end, from, StringComparison.Ordinal)];
    }
}

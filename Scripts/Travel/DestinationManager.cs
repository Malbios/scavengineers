using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;

namespace Scavengineers.Scripts.Travel;

/// <summary>Builds the tactical bubble at startup: instantiates every
/// <see cref="DestinationCatalog"/> entry under <see cref="BubbleRoot"/> and registers it with
/// the travel console. A new destination is one row of Data/destinations.json — no scene edit.
///
/// Every destination is instantiated once, at startup, and kept — presence is still toggled by
/// <c>TravelConsoleVerbTarget.SetShipPresence</c>. Freeing the ones you aren't at is a separate,
/// larger step: <c>ShipSim</c> is a Node that owns its <c>ShipSystems</c>, so freeing it would
/// destroy the very sim the coarse LOD tick exists to keep running, and would drop the build
/// state, layout seed and mission items SaveManager reads off the live tree.</summary>
public partial class DestinationManager : Node
{
    /// <summary>Where instantiated destinations are parented. Its own transform is left alone —
    /// each instance positions itself from its catalog entry.</summary>
    [Export]
    public Node3D? BubbleRoot { get; set; }

    [Export]
    public TravelConsoleVerbTarget? ConsoleRef { get; set; }

    private readonly List<Node3D> _instances = new();

    /// <summary>Every instantiated destination, in catalog order — stations first. Exposed for
    /// tests and for the console's own registration to be checkable.</summary>
    public IReadOnlyList<Node3D> Instances => _instances;

    public override void _Ready()
    {
        if (BubbleRoot is null)
        {
            GD.PushWarning("[DestinationManager] No BubbleRoot — no destination will exist to travel to.");
            return;
        }

        foreach (var destination in DestinationCatalog.All)
        {
            if (Instantiate(destination) is { } instance)
            {
                _instances.Add(instance);
                Register(destination, instance);
            }
        }
    }

    private Node3D? Instantiate(DestinationCatalog.DestinationDefinition destination)
    {
        if (string.IsNullOrEmpty(destination.Scene))
        {
            GD.PushWarning($"[DestinationManager] '{destination.Id}' has no scene — skipping, so it won't be offered on the travel map.");
            return null;
        }

        if (GD.Load<PackedScene>(destination.Scene) is not { } packed || packed.Instantiate() is not Node3D instance)
        {
            GD.PushWarning($"[DestinationManager] '{destination.Id}' scene '{destination.Scene}' could not be instantiated — skipping.");
            return null;
        }

        // Both before AddChild: a node's _Ready fires on entering the tree, and ShipSim reads
        // LayoutId/ProcedurallyGenerate there while ShipBuildTarget reads GenerateLoot.
        instance.Name = destination.Id;
        instance.Position = destination.Position;
        ApplyOverrides(destination, instance);

        BubbleRoot!.AddChild(instance);
        return instance;
    }

    private static void ApplyOverrides(DestinationCatalog.DestinationDefinition destination, Node3D instance)
    {
        foreach (var (nodePath, properties) in destination.Overrides)
        {
            if (instance.GetNodeOrNull(nodePath) is not { } node)
            {
                GD.PushWarning($"[DestinationManager] '{destination.Id}' overrides '{nodePath}', which its scene '{destination.Scene}' has no such node for — ignored.");
                continue;
            }

            var known = node.GetPropertyList().Select(entry => entry["name"].AsString()).ToHashSet();

            foreach (var (property, value) in properties)
            {
                // Node.Set on an unknown property is silent, so a typo here would leave the node
                // on its scene default — which for a SaveId means two destinations quietly
                // sharing one save key.
                if (!known.Contains(property))
                {
                    GD.PushWarning($"[DestinationManager] '{destination.Id}' overrides '{nodePath}.{property}', which doesn't exist on that node — ignored.");
                    continue;
                }

                node.Set(property, ToVariant(value));
            }
        }
    }

    /// <summary>Only the JSON kinds an override actually uses. Anything else falls back to the raw
    /// text, which Godot will coerce or reject visibly rather than silently mis-typing.</summary>
    private static Variant ToVariant(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.GetSingle(),
        _ => value.ToString(),
    };

    /// <summary>Hands the console the nodes it needs per destination. Found by type among the
    /// instance's *direct* children rather than by name: a Derelict has a second ShipSim and a
    /// second ShipBuildTarget, but they live under its Deck2 child, so a non-recursive search
    /// picks the primary deck.</summary>
    private void Register(DestinationCatalog.DestinationDefinition destination, Node3D instance)
    {
        if (ConsoleRef is null)
        {
            return;
        }

        var children = instance.GetChildren();
        var shipSim = children.OfType<ShipSim>().FirstOrDefault();
        var buildTarget = children.OfType<ShipBuildTarget>().FirstOrDefault();

        if (shipSim is null)
        {
            GD.PushWarning($"[DestinationManager] '{destination.Id}' has no ShipSim child — it can't be a travel destination.");
            return;
        }

        // The contract giver reaches back out of its own subtree to read the console's destination
        // list, which no NodePath in the destination scene can express since its depth in the
        // tree is decided at runtime.
        foreach (var contractGiver in children.OfType<ContractGiverVerbTarget>())
        {
            contractGiver.ConsoleRef = ConsoleRef;
        }

        if (destination.IsStation)
        {
            if (children.OfType<AirlockDoorVerbTarget>().FirstOrDefault() is not { } destinationAirlock)
            {
                GD.PushWarning($"[DestinationManager] Station '{destination.Id}' has no destination-side airlock — docking there would leave no way in.");
                return;
            }

            ConsoleRef.RegisterStation(instance, shipSim, destinationAirlock, buildTarget);
            return;
        }

        ConsoleRef.RegisterDerelict(instance, shipSim, buildTarget);
    }
}

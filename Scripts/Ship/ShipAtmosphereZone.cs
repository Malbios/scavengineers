using Godot;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// Marks "the player is currently inside this room" now that both ships (and both of each
/// ship's rooms) are loaded and simulated simultaneously — Player.ShipSimRef used to be a
/// scene-fixed export because only one ship's scene was ever loaded at a time; now it has to
/// follow the player at runtime instead. One zone per room (not per ship) since a room can now
/// be sealed off from the rest of its own ship via InteriorDoorVerbTarget — each zone reports
/// its own representative tile so the O2 reading reflects the room actually being stood in.
/// Also drives a real physics zero-g override for anything else physically in the room (loose
/// pickup items) — see <see cref="_PhysicsProcess"/>.
/// </summary>
public partial class ShipAtmosphereZone : Area3D
{
    /// <summary>Shared with Player.cs's own zero-g movement switch — one definition of "this room
    /// counts as vacuum" for both the Player's custom floating movement and this zone's real
    /// physics gravity override.</summary>
    public const float ZeroGO2Threshold = 0.01f;

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    /// <summary>Any tile within this zone's room — atmosphere is lumped uniformly across a
    /// connected component, so the exact tile only matters once the room is sealed off from
    /// the rest of the ship.</summary>
    [Export]
    public Vector2I Tile { get; set; }

    /// <summary>The ship's floor/ceiling breach-tracking ShipBuildTarget, if this ship has one
    /// (currently just the Home Ship — see ShipBuildTarget.ActiveBreachPositions) — the
    /// decompression-pull hazard's own read of "is there an open hole nearby." Left unset on
    /// ships without floor/ceiling construction; Player treats a null reference as "no pull."</summary>
    [Export]
    public ShipBuildTarget? BuildTargetRef { get; set; }

    public override void _PhysicsProcess(double delta)
    {
        if (ShipSimRef is null)
        {
            return;
        }

        // Real physics zero-g for any RigidBody3D physically inside this room (loose pickups) —
        // independent of, and doesn't replace, the Player's own custom floating-movement switch,
        // which stays exactly as it was.
        var isVacuum = ShipSimRef.VolumeAt(new CellCoord(Tile.X, Tile.Y)).O2Fraction <= ZeroGO2Threshold;
        GravitySpaceOverride = isVacuum ? SpaceOverride.Replace : SpaceOverride.Disabled;
        Gravity = 0f;
    }

    /// <summary>Finds whichever zone's Area3D shape currently contains a world position, via a
    /// direct physics-space point query rather than Area3D's own overlap-monitoring signals
    /// (BodyEntered/GetOverlappingBodies). Those signals only fire on a discrete "entered"
    /// transition, which is easy to miss: Jolt's monitoring silently excludes static/frozen bodies
    /// by default (confirmed Godot/Jolt engine limitation: godotengine/godot#103767), and even a
    /// normally-detected body can cross a thin shared boundary within a single physics tick
    /// without ever registering as "entered." A direct query has no transition to miss — it asks
    /// "what's here right now" — at the cost of running every frame instead of once per
    /// crossing, negligible at this game's scale.</summary>
    public static ShipAtmosphereZone? FindZoneAt(World3D world3D, Vector3 position)
    {
        var spaceState = world3D.DirectSpaceState;
        var query = new PhysicsPointQueryParameters3D
        {
            Position = position,
            CollideWithBodies = false,
            CollideWithAreas = true,
        };

        foreach (var result in spaceState.IntersectPoint(query))
        {
            if (result["collider"].As<GodotObject>() is ShipAtmosphereZone zone)
            {
                return zone;
            }
        }

        return null;
    }

    /// <summary>Called from a loose pickup's own _PhysicsProcess to freeze/unfreeze itself based
    /// on whichever zone it's currently standing in.</summary>
    public static void UpdateFreezeState(RigidBody3D item)
    {
        if (FindZoneAt(item.GetWorld3D(), item.GlobalPosition) is { ShipSimRef: { } shipSim } zone)
        {
            var isVacuum = shipSim.VolumeAt(new CellCoord(zone.Tile.X, zone.Tile.Y)).O2Fraction <= ZeroGO2Threshold;
            item.Freeze = !isVacuum;
        }
    }
}

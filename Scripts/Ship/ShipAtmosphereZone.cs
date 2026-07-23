using System.Linq;

using Godot;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;

namespace Scavengineers.Scripts.Ship;

/// <summary>Marks "the player is currently inside this room." One zone per room, not per ship,
/// since a room can be sealed off from the rest of its own ship via InteriorDoorVerbTarget — each
/// zone reports its own representative tile so the O2 reading reflects the room actually stood
/// in. Also drives a real physics zero-g override for anything else physically in the room (loose
/// pickup items) — see <see cref="_PhysicsProcess"/>.</summary>
public partial class ShipAtmosphereZone : Area3D
{
    /// <summary>Shared with Player.cs's own zero-g movement switch — one definition of "this room
    /// counts as vacuum" for both the player's floating movement and this zone's physics gravity
    /// override.</summary>
    public const float ZeroGO2Threshold = 0.01f;

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    /// <summary>Fallback representative reading for the gravity override below, which is
    /// inherently zone-wide since Godot's Area3D gravity override can't vary per body position.
    /// The player's own O2/zero-g reading uses its actual current tile instead (see
    /// <see cref="TileAt"/>), since per-cell diffusion means tiles in the same room can genuinely
    /// disagree near a fresh breach.</summary>
    [Export]
    public Vector2I Tile { get; set; }

    /// <summary>The ship's floor/ceiling breach-tracking ShipBuildTarget, if this ship has one
    /// (currently just the Home Ship) — the decompression-pull hazard's read of "is there an open
    /// hole nearby." Left unset on ships without floor/ceiling construction; Player treats a null
    /// reference as "no pull."</summary>
    [Export]
    public ShipBuildTarget? BuildTargetRef { get; set; }

    public override void _PhysicsProcess(double delta)
    {
        if (ShipSimRef is null)
        {
            return;
        }

        // Real physics zero-g for any RigidBody3D physically inside this room (loose pickups) —
        // independent of the player's own custom floating-movement switch.
        var isVacuum = ShipSimRef.VolumeAt(new CellCoord(Tile.X, Tile.Y)).O2Fraction <= ZeroGO2Threshold;
        GravitySpaceOverride = isVacuum ? SpaceOverride.Replace : SpaceOverride.Disabled;
        Gravity = 0f;
    }

    /// <summary>Finds whichever zone's Area3D shape currently contains a world position, via a
    /// direct physics-space point query rather than Area3D's overlap-monitoring signals
    /// (BodyEntered/GetOverlappingBodies) — those only fire on a discrete "entered" transition,
    /// and Jolt's monitoring silently excludes static/frozen bodies by default (godotengine/godot
    /// #103767). A direct query has no transition to miss, at the cost of running every frame
    /// instead of once per crossing (negligible at this game's scale).</summary>
    public static ShipAtmosphereZone? FindZoneAt(World3D world3D, Vector3 position)
    {
        var spaceState = world3D.DirectSpaceState;
        var query = new PhysicsPointQueryParameters3D
        {
            Position = position,
            CollideWithBodies = false,
            CollideWithAreas = true,
        };

        ShipAtmosphereZone? best = null;
        var bestMargin = float.NegativeInfinity;

        foreach (var result in spaceState.IntersectPoint(query))
        {
            if (result["collider"].As<GodotObject>() is not ShipAtmosphereZone zone)
            {
                continue;
            }

            var margin = zone.ContainmentMargin(position);
            if (margin > bestMargin)
            {
                bestMargin = margin;
                best = zone;
            }
        }

        return best;
    }

    /// <summary>How deep <paramref name="worldPosition"/> sits inside this zone's box collision
    /// shape, on the HORIZONTAL plane only (local X/Z) — the smaller axis margin, each expressed
    /// as a *fraction* of that axis's half-size. Used by <see cref="FindZoneAt"/> to break ties
    /// when two zones' shapes overlap: the zone the point sits most centrally inside wins, rather
    /// than arbitrary IntersectPoint enumeration order.
    ///
    /// Deliberately ignores the vertical (Y) axis: every room-type zone shares roughly the same
    /// floor-to-ceiling vertical-center convention, so Y barely varies between candidates yet a
    /// real player position (near the floor) sits far enough from that shared center that Y
    /// becomes the binding axis for every candidate almost equally, collapsing the tie-break back
    /// to arbitrary order (confirmed via debug logging — three overlapping zones all reporting
    /// the identical 0.092 margin).
    ///
    /// Normalizing (rather than a raw world-unit margin) matters too: an un-normalized version was
    /// systematically biased toward whichever zone's shape was physically bigger.</summary>
    private float ContainmentMargin(Vector3 worldPosition)
    {
        // Found by type, not a literal "CollisionShape3D" name — nothing guarantees that name.
        if (GetChildren().OfType<CollisionShape3D>().FirstOrDefault() is not { Shape: BoxShape3D box } collisionShape)
        {
            return 0f;
        }

        var local = collisionShape.GlobalTransform.AffineInverse() * worldPosition;
        var halfSize = box.Size / 2f;
        return Mathf.Min(
            (halfSize.X - Mathf.Abs(local.X)) / halfSize.X,
            (halfSize.Z - Mathf.Abs(local.Z)) / halfSize.Z);
    }

    /// <summary>Converts a world position into this zone's ship's grid tile coordinate. Lets the
    /// player read its own actual current cell instead of this zone's fixed representative
    /// <see cref="Tile"/>, since per-cell diffusion means cells in the same room can genuinely
    /// disagree for a while near a fresh breach.</summary>
    public Vector2I TileAt(Vector3 worldPosition) =>
        ShipGeometry.TileAtShipLocal(GetParent<Node3D>().ToLocal(worldPosition));
}

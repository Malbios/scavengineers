using Godot;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;
using PlayerScript = Scavengineers.Scripts.Player.Player;

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

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
    }

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

        // Loose pickups start frozen (see PickupItem/ContainerPickupItem's own default) — only
        // unfrozen here, once their room is actually confirmed to be in vacuum, so they stay
        // completely inert (immovable, no physics response) everywhere else. Read fresh from the
        // Area3D's live overlap list every tick rather than tracked via BodyEntered/Exited, so a
        // body that later drifts out of this zone into another one is never left stale.
        foreach (var body in GetOverlappingBodies())
        {
            if (body is RigidBody3D rigidBody)
            {
                rigidBody.Freeze = !isVacuum;
            }
        }
    }

    // Deliberately no BodyExited handler: the corridor between the two zones isn't its own
    // simulated space, so leaving a zone just holds the last room's reading rather than
    // clearing to nothing — entering the *other* zone is what overwrites it.
    private void OnBodyEntered(Node3D body)
    {
        if (body is PlayerScript player)
        {
            player.SetAmbientShipSim(ShipSimRef, Tile, BuildTargetRef);
        }
    }
}

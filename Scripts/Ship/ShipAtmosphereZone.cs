using Godot;
using Scavengineers.Scripts.Verbs;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// Marks "the player is currently inside this room" now that both ships (and both of each
/// ship's rooms) are loaded and simulated simultaneously — Player.ShipSimRef used to be a
/// scene-fixed export because only one ship's scene was ever loaded at a time; now it has to
/// follow the player at runtime instead. One zone per room (not per ship) since a room can now
/// be sealed off from the rest of its own ship via InteriorDoorVerbTarget — each zone reports
/// its own representative tile so the O2 reading reflects the room actually being stood in.
/// </summary>
public partial class ShipAtmosphereZone : Area3D
{
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

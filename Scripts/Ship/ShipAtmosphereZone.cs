using Godot;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// Marks "the player is currently inside this ship" now that both ships are loaded and
/// simulated simultaneously (see AirlockDoorVerbTarget) — Player.ShipSimRef used to be a
/// scene-fixed export because only one ship's scene was ever loaded at a time; now it has to
/// follow the player at runtime instead.
/// </summary>
public partial class ShipAtmosphereZone : Area3D
{
    [Export]
    public ShipSim? ShipSimRef { get; set; }

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
    }

    // Deliberately no BodyExited handler: the corridor between the two zones isn't its own
    // simulated space, so leaving a zone just holds the last ship's reading rather than
    // clearing to nothing — entering the *other* zone is what overwrites it.
    private void OnBodyEntered(Node3D body)
    {
        if (body is PlayerScript player)
        {
            player.SetAmbientShipSim(ShipSimRef);
        }
    }
}

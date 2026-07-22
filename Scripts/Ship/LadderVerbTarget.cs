using System.Collections.Generic;

using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Verbs;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// A same-ship vertical connector between two decks — "grab on" only; the actual continuous
/// climbing motion lives in Player.BeginClimbing/_PhysicsProcess, not here (unlike
/// AirlockDoorVerbTarget/InteriorDoorVerbTarget, this has no toggleable open/closed state at all —
/// it's not a door). BottomAnchor/TopAnchor are hand-placed Marker3Ds at the two decks' own floor
/// heights, at the same tile ShipBuildTarget already knows to leave a panel gap at (see
/// ShipSim.LadderCell/ShipBuildTarget.GeneratePanelsForCell) — no coordinate math here, matching
/// this codebase's "author positions in the scene" convention.
/// </summary>
public partial class LadderVerbTarget : StaticBody3D, IVerbTarget
{
    private static readonly Verb ClimbVerb = new("climb_ladder", "VERB_CLIMB_LADDER", DurationSeconds: 0.15f);

    [Export]
    public Node3D? BottomAnchor { get; set; }

    [Export]
    public Node3D? TopAnchor { get; set; }

    public IReadOnlyList<Verb> AvailableVerbs { get; } = [ClimbVerb];

    public float? CurrentVerbProgress => null; // instant hand-off into Player's own climb state

    public string? DisplayNameKey => "OBJECT_LADDER";

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != ClimbVerb.Id || BottomAnchor is null || TopAnchor is null)
        {
            return;
        }

        if (GetTree().GetFirstNodeInGroup("player") is PlayerScript player)
        {
            player.BeginClimbing(BottomAnchor.GlobalPosition, TopAnchor.GlobalPosition);
        }
    }

    public void CancelVerb()
    {
        // Grabbing on is instant and immediately hands off to Player's own climb state — nothing
        // here to cancel (see AirlockDoorVerbTarget/InteriorDoorVerbTarget's own CancelVerb for
        // the shape a real in-progress cycle would need instead).
    }
}

using System.Collections.Generic;

using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Verbs;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Ship;

/// <summary>A same-ship vertical connector between two decks — "grab on" only; the actual
/// continuous climbing motion lives in Player.BeginClimbing/_PhysicsProcess. BottomAnchor/
/// TopAnchor are hand-placed Marker3Ds at the two decks' own floor heights, at the same tile
/// ShipBuildTarget leaves a panel gap at.</summary>
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
        // Grabbing on is instant and immediately hands off to Player's own climb state.
    }
}

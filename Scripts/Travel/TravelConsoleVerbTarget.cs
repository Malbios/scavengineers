using System.Collections.Generic;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Verbs;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Travel;

/// <summary>
/// A simplified/abstracted travel step (docs/project-plan.md §4) — not full Newtonian
/// piloting, just a timed wait (reusing Player's existing busy-lock, nothing new needed there)
/// followed by a scene swap via Godot's ChangeSceneToFile.
/// </summary>
public partial class TravelConsoleVerbTarget : StaticBody3D, IVerbTarget
{
    // Placeholder/tunable — dropped to 1s for fast testing; real balancing is later work.
    private static readonly Verb TravelVerb = new("travel", "VERB_TRAVEL", DurationSeconds: 1f);

    [Export]
    public string DestinationScenePath { get; set; } = "";

    private Timer? _travelTimer;
    private bool _traveling;

    public IReadOnlyList<Verb> AvailableVerbs { get; } = [TravelVerb];

    public float? CurrentVerbProgress =>
        _traveling ? 1f - (float)(_travelTimer!.TimeLeft / _travelTimer.WaitTime) : null;

    public override void _Ready()
    {
        _travelTimer = new Timer { OneShot = true, WaitTime = TravelVerb.DurationSeconds };
        AddChild(_travelTimer);
        _travelTimer.Timeout += OnTravelComplete;
    }

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != TravelVerb.Id || _traveling)
        {
            return;
        }

        _traveling = true;
        _travelTimer!.Start();
    }

    private void OnTravelComplete()
    {
        _traveling = false;

        if (GetTree().GetFirstNodeInGroup("player") is PlayerScript player)
        {
            if (TravelState.Instance is not null)
            {
                TravelState.Instance.Pending = player.CaptureTravelPayload();
            }
        }

        GetTree().ChangeSceneToFile(DestinationScenePath);
    }

    public void CancelVerb()
    {
        if (!_traveling)
        {
            return;
        }

        _traveling = false;
        _travelTimer!.Stop();
    }
}

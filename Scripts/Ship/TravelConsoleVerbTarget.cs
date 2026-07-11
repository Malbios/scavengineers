using System.Collections.Generic;
using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Verbs;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// The Home Ship's simplified/abstracted travel step (docs/project-plan.md §4 — not full
/// Newtonian piloting, just a timed wait standing in for undocking, flying, and docking).
/// Owns which of two fixed locations the Home Ship currently occupies, and keeps both
/// AirlockDoorVerbTargets' <see cref="AirlockDoorVerbTarget.Docked"/> flag in sync. Doesn't
/// touch either airlock's open/closed state itself — whatever you left a door doing when you
/// left is still what it's doing when you're back (see AirlockDoorVerbTarget's own handling of
/// an open-but-undocked door venting to vacuum instead of auto-closing).
/// </summary>
public partial class TravelConsoleVerbTarget : StaticBody3D, IVerbTarget, IStateSaveable
{
    private enum Location { Station, Derelict }

    // Placeholder/tunable — dropped to near-instant for testing, was 4s for "a real beat."
    private static readonly Verb TravelVerb = new("travel", "VERB_TRAVEL", DurationSeconds: 0.2f);

    [Export]
    public AirlockDoorVerbTarget? StationAirlock { get; set; }

    [Export]
    public AirlockDoorVerbTarget? DerelictAirlock { get; set; }

    [Export]
    public string SaveId { get; set; } = "";

    private Timer? _travelTimer;
    private bool _traveling;
    private Location _currentLocation = Location.Station;

    public IReadOnlyList<Verb> AvailableVerbs { get; } = [TravelVerb];

    public string? DisplayNameKey => "OBJECT_SHIP_CONSOLE";

    public float? CurrentVerbProgress =>
        _traveling ? 1f - (float)(_travelTimer!.TimeLeft / _travelTimer.WaitTime) : null;

    public override void _Ready()
    {
        _travelTimer = new Timer { OneShot = true, WaitTime = TravelVerb.DurationSeconds };
        AddChild(_travelTimer);
        _travelTimer.Timeout += OnTravelComplete;

        // Deferred: both airlocks' ShipSim refs may belong to sibling branches of the scene
        // tree that haven't built their Deck yet at this exact point in _Ready() order (see
        // ShipSim's own deferred vacuum seeding for the same reason).
        CallDeferred(nameof(ApplyLocationToAirlocks));
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

    public void CancelVerb()
    {
        if (!_traveling)
        {
            return;
        }

        _traveling = false;
        _travelTimer!.Stop();
    }

    private void OnTravelComplete()
    {
        _traveling = false;
        _currentLocation = _currentLocation == Location.Station ? Location.Derelict : Location.Station;
        ApplyLocationToAirlocks();
    }

    public string GetSaveState() => _currentLocation == Location.Station ? "station" : "derelict";

    public void ApplySaveState(string state)
    {
        _currentLocation = state == "derelict" ? Location.Derelict : Location.Station;
        ApplyLocationToAirlocks();
    }

    private void ApplyLocationToAirlocks()
    {
        if (StationAirlock is not null)
        {
            StationAirlock.Docked = _currentLocation == Location.Station;
        }

        if (DerelictAirlock is not null)
        {
            DerelictAirlock.Docked = _currentLocation == Location.Derelict;
        }
    }
}

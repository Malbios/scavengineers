using System.Collections.Generic;

using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Verbs;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// Fixed Home Ship furniture (no build-target removal, like TravelConsoleVerbTarget) that
/// restores the player's Energy need (PlayerNeeds, docs/project-plan.md's time-acceleration
/// note) — a plain timed verb, not a power-gated station like RechargeStationVerbTarget, since
/// a bunk doesn't need power.
/// </summary>
public partial class BunkVerbTarget : StaticBody3D, IVerbTarget
{
    // Placeholder/tunable — near-instant for dev testing, same spirit as every other timed verb.
    private static readonly Verb SleepVerb = new("sleep", "VERB_SLEEP", DurationSeconds: 3f);

    private Timer? _sleepTimer;
    private bool _sleeping;

    public IReadOnlyList<Verb> AvailableVerbs => [SleepVerb];

    public string? DisplayNameKey => "OBJECT_BUNK";

    public float? CurrentVerbProgress =>
        _sleeping ? 1f - (float)(_sleepTimer!.TimeLeft / _sleepTimer.WaitTime) : null;

    public override void _Ready()
    {
        _sleepTimer = new Timer { OneShot = true, WaitTime = SleepVerb.DurationSeconds };
        AddChild(_sleepTimer);
        _sleepTimer.Timeout += OnSleepComplete;
    }

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != SleepVerb.Id || _sleeping)
        {
            return;
        }

        _sleeping = true;
        _sleepTimer!.Start();
    }

    public void CancelVerb()
    {
        if (!_sleeping)
        {
            return;
        }

        _sleeping = false;
        _sleepTimer!.Stop();
    }

    private void OnSleepComplete()
    {
        _sleeping = false;
        if (GetTree().GetFirstNodeInGroup("player") is PlayerScript player)
        {
            player.RestEnergy();
        }
    }
}

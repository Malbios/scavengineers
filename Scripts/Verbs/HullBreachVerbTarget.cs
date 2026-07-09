using System.Collections.Generic;
using Godot;
using Scavengineers.Scripts.Ship;

namespace Scavengineers.Scripts.Verbs;

/// <summary>
/// Represents ShipSim's one demo cell in the scene. Proves the verb system is wired to the
/// real sim, not an isolated demo: on completion, RepairHull is called on the same Deck the
/// ticking AtmosphereSystem reads (see docs/architecture/verbs-and-interaction.md — a verb's
/// duration is a real elapsed-time task, not gated on continued input).
/// </summary>
public partial class HullBreachVerbTarget : StaticBody3D, IVerbTarget
{
    private static readonly Verb RepairVerb = new("repair", "VERB_REPAIR", DurationSeconds: 3f);

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    private Timer? _repairTimer;
    private bool _repairInProgress;

    public IReadOnlyList<Verb> AvailableVerbs { get; } = [RepairVerb];

    public override void _Ready()
    {
        _repairTimer = new Timer { OneShot = true, WaitTime = RepairVerb.DurationSeconds };
        AddChild(_repairTimer);
        _repairTimer.Timeout += OnRepairComplete;
    }

    public void ExecuteVerb(Verb verb)
    {
        if (verb.Id != RepairVerb.Id || _repairInProgress)
        {
            return;
        }

        _repairInProgress = true;
        _repairTimer!.Start();
    }

    private void OnRepairComplete()
    {
        _repairInProgress = false;
        ShipSimRef!.Deck.RepairHull(ShipSim.DemoCell);
    }
}

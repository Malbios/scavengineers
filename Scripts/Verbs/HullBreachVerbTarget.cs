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

    [Export]
    public MeshInstance3D? Mesh { get; set; }

    [Export]
    public CollisionShape3D? Collider { get; set; }

    [Export]
    public Material? RepairingMaterial { get; set; }

    private Timer? _repairTimer;
    private bool _repairInProgress;
    private Material? _idleMaterial;

    public IReadOnlyList<Verb> AvailableVerbs { get; } = [RepairVerb];

    public float? CurrentVerbProgress =>
        _repairInProgress ? 1f - (float)(_repairTimer!.TimeLeft / _repairTimer.WaitTime) : null;

    public override void _Ready()
    {
        _repairTimer = new Timer { OneShot = true, WaitTime = RepairVerb.DurationSeconds };
        AddChild(_repairTimer);
        _repairTimer.Timeout += OnRepairComplete;

        _idleMaterial = Mesh?.GetSurfaceOverrideMaterial(0);
    }

    public void ExecuteVerb(Verb verb)
    {
        if (verb.Id != RepairVerb.Id || _repairInProgress)
        {
            return;
        }

        _repairInProgress = true;
        _repairTimer!.Start();

        if (Mesh is not null && RepairingMaterial is not null)
        {
            Mesh.SetSurfaceOverrideMaterial(0, RepairingMaterial);
        }
    }

    private void OnRepairComplete()
    {
        _repairInProgress = false;
        ShipSimRef!.Deck.RepairHull(ShipSim.DemoCell);

        Visible = false;
        if (Collider is not null)
        {
            Collider.Disabled = true;
        }
    }

    public void CancelVerb()
    {
        if (!_repairInProgress)
        {
            return;
        }

        _repairInProgress = false;
        _repairTimer!.Stop();
        Mesh?.SetSurfaceOverrideMaterial(0, _idleMaterial);
    }
}

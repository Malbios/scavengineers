using System.Collections.Generic;
using System.Linq;
using Godot;
using Scavengineers.Sim.ShipModel;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Ship;

namespace Scavengineers.Scripts.Verbs;

/// <summary>
/// The conduit fire hazard's repair point (docs/project-plan.md Appendix A7 — powered +
/// damaged + O2 present sparks and burns; ShipSim's FireSystem already handles ignition/
/// burning/self-extinguishing). Repairing here just restores the fixture's Condition, which
/// stops FireSystem from ever igniting it again — it doesn't need to touch Deck.Fires itself,
/// since a fixed conduit simply stops meeting the "damaged" precondition on the next tick.
/// </summary>
public partial class DamagedConduitVerbTarget : StaticBody3D, IVerbTarget, ISaveable
{
    // Placeholder/tunable — kept short for fast testing.
    private static readonly Verb RepairVerb = new("repair", "VERB_REPAIR", DurationSeconds: 1f)
    {
        Requirements = [new ItemRequirement("spare_parts", 1)],
    };

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    [Export]
    public MeshInstance3D? Mesh { get; set; }

    [Export]
    public Material? RepairingMaterial { get; set; }

    [Export]
    public Material? RepairedMaterial { get; set; }

    /// <summary>Shown whenever ShipSim's FireSystem has this conduit's tile actively burning —
    /// the only player-visible sign anything is happening, since this pass has no flame VFX.</summary>
    [Export]
    public Material? BurningMaterial { get; set; }

    [Export]
    public string SaveId { get; set; } = "";

    private Timer? _repairTimer;
    private bool _repairInProgress;
    private bool _repaired;
    private bool _wasBurning;
    private Material? _idleMaterial;

    public IReadOnlyList<Verb> AvailableVerbs => _repaired ? [] : [RepairVerb];

    public string? DisplayNameKey => "OBJECT_DAMAGED_CONDUIT";

    public float? CurrentVerbProgress =>
        _repairInProgress ? 1f - (float)(_repairTimer!.TimeLeft / _repairTimer.WaitTime) : null;

    public override void _Ready()
    {
        _repairTimer = new Timer { OneShot = true, WaitTime = RepairVerb.DurationSeconds };
        AddChild(_repairTimer);
        _repairTimer.Timeout += OnRepairComplete;

        _idleMaterial = Mesh?.GetSurfaceOverrideMaterial(0);
    }

    public override void _PhysicsProcess(double delta)
    {
        // Repairing/repaired already own the mesh material for the moment — don't fight them.
        if (_repairInProgress || _repaired)
        {
            return;
        }

        var isBurning = FindConduitFixture() is { } conduit && (ShipSimRef?.Deck.IsOnFire(conduit.Tile) ?? false);
        if (isBurning == _wasBurning)
        {
            return;
        }

        _wasBurning = isBurning;
        Mesh?.SetSurfaceOverrideMaterial(0, isBurning ? BurningMaterial : _idleMaterial);
    }

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != RepairVerb.Id || _repairInProgress || _repaired)
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
        SetRepaired(true);
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

    public bool GetSaveState() => _repaired;

    public void ApplySaveState(bool state) => SetRepaired(state);

    private void SetRepaired(bool repaired)
    {
        _repaired = repaired;

        if (FindConduitFixture() is { } conduit)
        {
            conduit.Condition = repaired ? 1f : 0.1f;

            if (repaired)
            {
                ShipSimRef?.Deck.ExtinguishFire(conduit.Tile);
            }
        }

        Mesh?.SetSurfaceOverrideMaterial(0, repaired ? RepairedMaterial : _idleMaterial);
    }

    private ConduitFixture? FindConduitFixture() =>
        ShipSimRef?.Deck.Fixtures.OfType<ConduitFixture>()
            .FirstOrDefault(f => f.Id == ShipSim.DamagedConduitFixtureId);
}

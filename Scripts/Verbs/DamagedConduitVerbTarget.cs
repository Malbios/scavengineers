using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Sim.ShipModel;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Ship;

namespace Scavengineers.Scripts.Verbs;

/// <summary>The conduit fire hazard's repair point — powered + damaged + O2 present sparks and
/// burns (ShipSim's FireSystem handles ignition/burning/self-extinguishing). Repair restores the
/// fixture's Condition, stopping FireSystem from ever igniting it again. Scrap rips it out of the
/// power circuit entirely instead — a real tradeoff between the two.</summary>
public partial class DamagedConduitVerbTarget : StaticBody3D, IVerbTarget, IStateSaveable
{
    private enum ConduitState { Damaged, Repaired, Scrapped }

    // Placeholder/tunable — kept short for fast testing.
    private static readonly Verb RepairVerb = new("repair", "VERB_REPAIR", DurationSeconds: 0.6f)
    {
        Requirements = [new ItemRequirement("spare_parts", 1)],
    };

    private static readonly Verb ScrapVerb = new("scrap", "VERB_SCRAP", DurationSeconds: 0.6f) { IsDestructive = true };

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    [Export]
    public MeshInstance3D? Mesh { get; set; }

    [Export]
    public CollisionShape3D? Collider { get; set; }

    [Export]
    public Material? RepairingMaterial { get; set; }

    [Export]
    public Material? RepairedMaterial { get; set; }

    /// <summary>Shown whenever ShipSim's FireSystem has this conduit's tile actively burning.</summary>
    [Export]
    public Material? BurningMaterial { get; set; }

    /// <summary>A grey particle puff toggled on/off alongside BurningMaterial — the visible cue
    /// that a burning cell drains the player's O2 faster and impairs their vision (see Player.cs's
    /// inSmoke handling).</summary>
    [Export]
    public GpuParticles3D? SmokeParticles { get; set; }

    [Export]
    public string SaveId { get; set; } = "";

    private Timer? _actionTimer;
    private bool _actionInProgress;
    private ConduitState _pendingState;
    private ConduitState _state = ConduitState.Damaged;
    private bool _wasBurning;
    private Material? _idleMaterial;
    private PlayerInventory? _pendingInventory;

    public IReadOnlyList<Verb> AvailableVerbs => _state == ConduitState.Damaged ? [RepairVerb, ScrapVerb] : [];

    public string? DisplayNameKey => "OBJECT_DAMAGED_CONDUIT";

    public float? Condition => FindConduitFixture()?.Condition;

    public float? CurrentVerbProgress =>
        _actionInProgress ? 1f - (float)(_actionTimer!.TimeLeft / _actionTimer.WaitTime) : null;

    public override void _Ready()
    {
        _actionTimer = new Timer { OneShot = true, WaitTime = RepairVerb.DurationSeconds };
        AddChild(_actionTimer);
        _actionTimer.Timeout += OnActionComplete;

        _idleMaterial = Mesh?.GetSurfaceOverrideMaterial(0);

        // Deferred: ShipSimRef's DamagedConduitCell/HasFireHazard are only final once its own
        // _Ready() has run, which may not have happened yet depending on scene-tree sibling order.
        CallDeferred(nameof(ApplyGeneratedPlacement));
    }

    /// <summary>This node's hand-authored world Transform is fixed regardless of which cell
    /// ShipSim.DamagedConduitCell resolves to — wrong for any layout (catalog-loaded or
    /// procedurally generated) that moves it. A direct child of the ship root, so its Position is
    /// already in ship-root-local space — no ToLocal/ToGlobal needed. Only X/Z move; the
    /// hand-authored Y is left untouched. Hidden and non-interactive for a layout with no fire
    /// hazard, rather than dangling inertly in whatever room cell (0,0) happens to be.</summary>
    private void ApplyGeneratedPlacement()
    {
        if (ShipSimRef is null)
        {
            return;
        }

        var cell = ShipSimRef.DamagedConduitCell;
        Position = new Vector3(cell.X - 3 + 0.5f, Position.Y, cell.Y - 3 + 0.5f);

        if (!ShipSimRef.HasFireHazard)
        {
            Visible = false;
            if (Collider is not null)
            {
                Collider.Disabled = true;
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // An action in progress or already-resolved state owns the mesh material for the
        // moment — don't fight it.
        if (_actionInProgress || _state != ConduitState.Damaged)
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

        if (SmokeParticles is not null)
        {
            SmokeParticles.Emitting = isBurning;
        }
    }

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (_actionInProgress || _state != ConduitState.Damaged || (verb.Id != RepairVerb.Id && verb.Id != ScrapVerb.Id))
        {
            return;
        }

        _pendingState = verb.Id == RepairVerb.Id ? ConduitState.Repaired : ConduitState.Scrapped;
        _pendingInventory = inventory;
        _actionInProgress = true;
        _actionTimer!.Start();

        if (Mesh is not null && RepairingMaterial is not null)
        {
            Mesh.SetSurfaceOverrideMaterial(0, RepairingMaterial);
        }
    }

    private void OnActionComplete()
    {
        _actionInProgress = false;
        var inventory = _pendingInventory;
        _pendingInventory = null;

        SetState(_pendingState);

        if (_pendingState == ConduitState.Scrapped)
        {
            var added = inventory?.Add("scrap_metal", 1) ?? 0;
            if (added < 1)
            {
                InventoryOverflow.DropAt(this, "scrap_metal", 1 - added);
            }
        }
    }

    public void CancelVerb()
    {
        if (!_actionInProgress)
        {
            return;
        }

        _actionInProgress = false;
        _pendingInventory = null;
        _actionTimer!.Stop();
        Mesh?.SetSurfaceOverrideMaterial(0, _idleMaterial);
    }

    public string GetSaveState() => _state switch
    {
        ConduitState.Repaired => "repaired",
        ConduitState.Scrapped => "scrapped",
        _ => "damaged",
    };

    public void ApplySaveState(string state) => SetState(state switch
    {
        "repaired" => ConduitState.Repaired,
        "scrapped" => ConduitState.Scrapped,
        _ => ConduitState.Damaged,
    });

    private void SetState(ConduitState state)
    {
        _state = state;

        switch (state)
        {
            case ConduitState.Repaired:
                if (FindConduitFixture() is { } repairedConduit)
                {
                    repairedConduit.Condition = 1f;
                }

                ExtinguishIfBurning();
                Mesh?.SetSurfaceOverrideMaterial(0, RepairedMaterial);
                if (SmokeParticles is not null)
                {
                    SmokeParticles.Emitting = false;
                }

                break;

            case ConduitState.Scrapped:
                // Extinguish before removing the fixture — ExtinguishIfBurning looks it up by id
                // to find its tile, which no longer resolves once gone.
                ExtinguishIfBurning();
                ShipSimRef?.Deck.RemoveFixture(ShipSim.DamagedConduitFixtureId);
                if (SmokeParticles is not null)
                {
                    SmokeParticles.Emitting = false;
                }

                Visible = false;
                if (Collider is not null)
                {
                    Collider.Disabled = true;
                }

                break;
        }
    }

    private void ExtinguishIfBurning()
    {
        if (FindConduitFixture() is { } conduit)
        {
            ShipSimRef?.Deck.ExtinguishFire(conduit.Tile);
        }
    }

    private ConduitFixture? FindConduitFixture() =>
        ShipSimRef?.Deck.Fixtures.OfType<ConduitFixture>()
            .FirstOrDefault(f => f.Id == ShipSim.DamagedConduitFixtureId);
}

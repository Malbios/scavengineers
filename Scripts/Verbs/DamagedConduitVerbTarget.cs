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
/// burning/self-extinguishing). Repair restores the fixture's Condition, which stops
/// FireSystem from ever igniting it again. Scrap rips it out of the power circuit entirely
/// instead — a real tradeoff between the two, not just one verb.
/// </summary>
public partial class DamagedConduitVerbTarget : StaticBody3D, IVerbTarget, IStateSaveable
{
    private enum ConduitState { Damaged, Repaired, Scrapped }

    // Placeholder/tunable — kept short for fast testing.
    private static readonly Verb RepairVerb = new("repair", "VERB_REPAIR", DurationSeconds: 0.2f)
    {
        Requirements = [new ItemRequirement("spare_parts", 1)],
    };

    private static readonly Verb ScrapVerb = new("scrap", "VERB_SCRAP", DurationSeconds: 0.2f) { IsDestructive = true };

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

    /// <summary>Shown whenever ShipSim's FireSystem has this conduit's tile actively burning —
    /// combined with SmokeParticles below, now that fire is a real hazard worth calling
    /// attention to rather than just a material swap.</summary>
    [Export]
    public Material? BurningMaterial { get; set; }

    /// <summary>A grey particle puff toggled on/off alongside BurningMaterial — the visible cue
    /// that a burning cell now actually drains the player's O2 faster and impairs their vision
    /// (see Player.cs's inSmoke handling), not just a cosmetic material change.</summary>
    [Export]
    public GpuParticles3D? SmokeParticles { get; set; }

    /// <summary>Generic dropped-item visual for the Scrap yield, if it doesn't fully fit in the
    /// player's inventory — see InventoryOverflow.</summary>
    [Export]
    public Mesh? DroppedItemMesh { get; set; }

    [Export]
    public Shape3D? DroppedItemShape { get; set; }

    [Export]
    public Material? DroppedItemMaterial { get; set; }

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

    public float? CurrentVerbProgress =>
        _actionInProgress ? 1f - (float)(_actionTimer!.TimeLeft / _actionTimer.WaitTime) : null;

    public override void _Ready()
    {
        _actionTimer = new Timer { OneShot = true, WaitTime = RepairVerb.DurationSeconds };
        AddChild(_actionTimer);
        _actionTimer.Timeout += OnActionComplete;

        _idleMaterial = Mesh?.GetSurfaceOverrideMaterial(0);
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
            if (added < 1 && DroppedItemMesh is not null && DroppedItemShape is not null)
            {
                InventoryOverflow.DropAt(this, "scrap_metal", 1 - added, DroppedItemMesh, DroppedItemShape, DroppedItemMaterial);
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
                // Extinguish before removing the fixture — ExtinguishIfBurning looks the
                // fixture up by id to find its tile, which no longer resolves once gone.
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

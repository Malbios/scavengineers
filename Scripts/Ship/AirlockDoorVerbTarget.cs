using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Verbs;

namespace Scavengineers.Scripts.Ship;

/// <summary>A real door between the Home Ship and one of its possible docking targets. The
/// Station gets its own dedicated instance; the "away mission" instance (see
/// <see cref="RebindFarSide"/>) is shared across every Derelict, its far side repointed by
/// TravelConsoleVerbTarget at the current travel target. While docked, opening it links the two
/// ships' independent <see cref="AtmosphereSystem"/>s via an <see cref="AirlockBridge"/>. While
/// undocked (see <see cref="Docked"/>), opening it instead breaches both adjacent cells straight
/// to vacuum — a real consequence for forcing the wrong door instead of using the travel console.
/// This node is a persistent frame at the doorway threshold; <see cref="SlabMesh"/> is the part
/// that toggles visible/collidable on open/close, while the frame stays put.</summary>
public partial class AirlockDoorVerbTarget : StaticBody3D, IVerbTarget, ISaveable
{
    private static readonly Verb OpenVerb = new("open_airlock", "VERB_OPEN_AIRLOCK", DurationSeconds: 0.6f);
    private static readonly Verb CloseVerb = new("close_airlock", "VERB_CLOSE_AIRLOCK", DurationSeconds: 0.6f);

    // Placeholder/tunable — longer than the powered open/close to feel like real manual effort.
    // Reuses VERB_PRY_DOOR's text rather than a new key — an airlock is a door.
    private static readonly Verb PryVerb = new("pry_airlock", "VERB_PRY_DOOR", DurationSeconds: 1.5f)
    {
        Requirements = [new ItemRequirement("crowbar", 1) { Consumed = false }],
    };

    // Placeholder/tunable, matching SuitResources's drain-constant convention.
    private const float BatteryDrainPerSecond = 0.05f;

    // Same two-tier upkeep as everything else with a Deck-tracked Condition (see MaintenanceTier).
    private static readonly ItemRequirement WrenchRequirement = new("wrench", 1) { Consumed = false };
    private static readonly ItemRequirement SparePartsRequirement = new("spare_parts", 1);

    private static readonly Verb MaintainAirlockVerb = new("maintain_airlock", "VERB_MAINTAIN_AIRLOCK", DurationSeconds: 0.6f)
    {
        Requirements = [WrenchRequirement],
    };

    private static readonly Verb RepairAirlockVerb = new("repair_airlock", "VERB_REPAIR_AIRLOCK", DurationSeconds: 0.6f)
    {
        Requirements = [WrenchRequirement, SparePartsRequirement],
    };

    [Export]
    public ShipSim? ShipARef { get; set; }

    [Export]
    public ShipSim? ShipBRef { get; set; }

    /// <summary>Which of ShipARef's power fixtures this instance is wired to — always checked
    /// against ShipARef, since that's the Home Ship on both sides (ShipBRef never has a battery
    /// of its own).</summary>
    [Export]
    public string PowerFixtureId { get; set; } = "";

    /// <summary>Which tile on each ship's grid the airlock connects at.</summary>
    [Export]
    public Vector2I TileA { get; set; }

    [Export]
    public Vector2I TileB { get; set; }

    [Export]
    public MeshInstance3D? SlabMesh { get; set; }

    [Export]
    public CollisionShape3D? SlabCollision { get; set; }

    [Export]
    public string SaveId { get; set; } = "";

    /// <summary>False for a destination-side door (e.g. a Station's own DestinationAirlock) — it
    /// shares the connection's single cross-ship <see cref="AirlockBridge"/>/vacuum-breach pair
    /// via whichever door has this true, rather than creating a redundant second one. Defaults
    /// true so a single-door connection (Derelict) is unaffected.</summary>
    [Export]
    public bool OwnsBridge { get; set; } = true;

    /// <summary>True for a per-side door that also seals/unseals a real edge within its OWN
    /// ship's Deck (see <see cref="LocalEdgeColumnNear"/>/<see cref="LocalEdgeColumnFar"/>),
    /// generalizing InteriorDoorVerbTarget's room-to-room seal to a configurable column boundary.
    /// False (default) preserves the single-door Derelict behavior, which never seals locally.</summary>
    [Export]
    public bool SealsLocalEdge { get; set; }

    /// <summary>The two columns (paired with every row in ShipSim.DoorwayRows) sealed/unsealed
    /// on ShipARef.Deck when SealsLocalEdge is true. Meaningless otherwise.</summary>
    [Export]
    public int LocalEdgeColumnNear { get; set; }

    [Export]
    public int LocalEdgeColumnFar { get; set; }

    /// <summary>The other side's door for this same connection. Null (default, and every
    /// single-door connection like Derelict) means "always treat the partner as open" — see
    /// ApplyPhysicalState's bridge-engagement check.</summary>
    [Export]
    public AirlockDoorVerbTarget? PartnerDoorRef { get; set; }

    /// <summary>False for a Station's own destination-side door — Stations have no power grid, so
    /// gating on ShipARef.IsPowered would make that door permanently pry-only.</summary>
    [Export]
    public bool RequiresPower { get; set; } = true;

    private bool _docked = true;

    /// <summary>True only for the airlock at the Home Ship's current location. Not persisted
    /// directly — re-derived from the owning console's saved state on load. Changing this
    /// re-derives the door's physical effect immediately rather than waiting for open/close.</summary>
    public bool Docked
    {
        get => _docked;
        set
        {
            _docked = value;
            ApplyPhysicalState();
        }
    }

    private AirlockBridge? _bridge;
    private Timer? _cycleTimer;
    private bool _cycling;
    private bool _cyclingIsPry;
    private bool _pendingOpenState;
    private bool _isOpen;
    private Material? _doorMaterial;

    // A second, independent timer/bool pair — upkeep is an unrelated timed action on top of the
    // existing Open/Close/Pry cycle.
    private Timer? _maintenanceTimer;
    private bool _maintaining;

    public bool IsOpen => _isOpen;

    /// <summary>Unpowered: no motor to drive the mechanism, so <see cref="PryVerb"/> is the only
    /// option, prying open if closed or forcing shut if already open (see
    /// <see cref="ExecuteVerb"/>, which decides direction from <see cref="IsOpen"/> rather than
    /// which verb was picked). Maintain/Repair is offered regardless of powered state.</summary>
    public IReadOnlyList<Verb> AvailableVerbs
    {
        get
        {
            if (ShipARef is null || (OwnsBridge && _bridge is null))
            {
                return [];
            }

            var powered = !RequiresPower || ShipARef.IsPowered(PowerFixtureId);
            var verbs = new List<Verb> { powered ? (IsOpen ? CloseVerb : OpenVerb) : PryVerb };

            if (AirlockFixture is { } fixture && MaintenanceTier.PickVerb(fixture.Condition, MaintainAirlockVerb, RepairAirlockVerb) is { } upkeepVerb)
            {
                verbs.Add(upkeepVerb);
            }

            return verbs;
        }
    }

    public string? DisplayNameKey => "OBJECT_AIRLOCK";

    private Fixture? AirlockFixture => ShipARef?.Deck.Fixtures.FirstOrDefault(f => f.Id == PowerFixtureId);

    public float? Condition => AirlockFixture?.Condition;

    public float? CurrentVerbProgress =>
        _cycling ? 1f - (float)(_cycleTimer!.TimeLeft / _cycleTimer.WaitTime)
        : _maintaining ? 1f - (float)(_maintenanceTimer!.TimeLeft / _maintenanceTimer.WaitTime)
        : null;

    public override void _Ready()
    {
        if (OwnsBridge && ShipARef?.Atmosphere is { } atmosphereA && ShipBRef?.Atmosphere is { } atmosphereB)
        {
            _bridge = new AirlockBridge(
                atmosphereA, new CellCoord(TileA.X, TileA.Y),
                atmosphereB, new CellCoord(TileB.X, TileB.Y));
        }

        _cycleTimer = new Timer { OneShot = true, WaitTime = OpenVerb.DurationSeconds };
        AddChild(_cycleTimer);
        _cycleTimer.Timeout += OnCycleComplete;

        _maintenanceTimer = new Timer { OneShot = true, WaitTime = MaintainAirlockVerb.DurationSeconds };
        AddChild(_maintenanceTimer);
        _maintenanceTimer.Timeout += OnMaintenanceComplete;

        _doorMaterial = SlabMesh?.GetSurfaceOverrideMaterial(0);
    }

    public override void _PhysicsProcess(double delta)
    {
        _bridge?.Tick(delta);

        // A pry is manual force, not motor-driven — no ship battery involved.
        if (_cycling && !_cyclingIsPry)
        {
            ShipARef?.DrainBattery(BatteryDrainPerSecond * (float)delta);
        }
    }

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id == MaintainAirlockVerb.Id || verb.Id == RepairAirlockVerb.Id)
        {
            if (!_maintaining)
            {
                _maintaining = true;
                _maintenanceTimer!.Start();
            }

            return;
        }

        if (ShipARef is null || (OwnsBridge && _bridge is null) || _cycling || (verb.Id != OpenVerb.Id && verb.Id != CloseVerb.Id && verb.Id != PryVerb.Id))
        {
            return;
        }

        _cyclingIsPry = verb.Id == PryVerb.Id;
        _pendingOpenState = _cyclingIsPry ? !IsOpen : verb.Id != CloseVerb.Id;
        _cycling = true;
        _cycleTimer!.WaitTime = verb.DurationSeconds;
        _cycleTimer.Start();
    }

    public void CancelVerb()
    {
        if (_maintaining)
        {
            _maintaining = false;
            _maintenanceTimer!.Stop();
        }

        if (!_cycling)
        {
            return;
        }

        _cycling = false;
        _cycleTimer!.Stop();
    }

    private void OnCycleComplete()
    {
        _cycling = false;
        SetOpen(_pendingOpenState);
    }

    private void OnMaintenanceComplete()
    {
        _maintaining = false;
        if (AirlockFixture is { } fixture)
        {
            fixture.Condition = 1f;
        }
    }

    public bool GetSaveState() => IsOpen;

    /// <summary>Jumps straight to the saved open/closed state without replaying the timer.</summary>
    public void ApplySaveState(bool state) => SetOpen(state);

    private void SetOpen(bool open)
    {
        _isOpen = open;
        ApplyPhysicalState();
    }

    /// <summary>Reassigns this airlock's far side to a different ShipSim — the one physical
    /// away-mission airlock is shared across every travel destination. A no-op if the far side
    /// isn't actually changing (repeat travel to the same destination must not reset anything).
    /// When it does change, the door is forced shut first: an "open" state left over from the
    /// previous destination has no physical meaning at the new one.</summary>
    public void RebindFarSide(ShipSim newShipB, AirlockDoorVerbTarget? newPartnerDoor = null)
    {
        if (ReferenceEquals(ShipBRef, newShipB))
        {
            return;
        }

        SetOpen(false);
        ShipBRef = newShipB;

        // Bidirectional: the old partner (if any) no longer refers back to this door, and the
        // new one does.
        if (PartnerDoorRef is not null)
        {
            PartnerDoorRef.PartnerDoorRef = null;
        }

        PartnerDoorRef = newPartnerDoor;

        if (newPartnerDoor is not null)
        {
            newPartnerDoor.PartnerDoorRef = this;
        }

        _bridge = OwnsBridge && ShipARef?.Atmosphere is { } atmosphereA && newShipB.Atmosphere is { } atmosphereB
            ? new AirlockBridge(atmosphereA, new CellCoord(TileA.X, TileA.Y), atmosphereB, new CellCoord(TileB.X, TileB.Y))
            : null;
    }

    /// <summary>Re-derives the door's entire physical effect — slab visibility, passability,
    /// atmosphere bridging, vacuum breaching — from open/closed and docked/undocked together, so
    /// an airlock left open through a docking-state change (e.g. undocking without closing it)
    /// immediately switches between "walk through to the other ship" and "float into open space."</summary>
    private void ApplyPhysicalState()
    {
        var passable = _isOpen;
        var ventingToSpace = _isOpen && !_docked;

        if (SlabMesh is not null)
        {
            SlabMesh.Visible = !passable;
            SlabMesh.SetSurfaceOverrideMaterial(0, _doorMaterial);
        }

        if (SlabCollision is not null)
        {
            SlabCollision.Disabled = passable;
        }

        if (SealsLocalEdge)
        {
            ApplyLocalEdgeSeal(passable);
        }

        // Also re-derives the partner's own bridge engagement, so opening or closing EITHER side
        // of a connection re-evaluates the bridge rather than leaving a stale snapshot on the other.
        RefreshBridgeEngagement();
        PartnerDoorRef?.RefreshBridgeEngagement();

        SetBreached(ShipARef, TileA, ventingToSpace);
        SetBreached(ShipBRef, TileB, ventingToSpace);
    }

    /// <summary>Only actually bridges/vents once BOTH this connection's doors report open — a
    /// null PartnerDoorRef (a single-door connection, e.g. Derelict) means "always treat the
    /// partner as open."</summary>
    private void RefreshBridgeEngagement()
    {
        if (_bridge is not null)
        {
            _bridge.IsOpen = _isOpen && (PartnerDoorRef?.IsOpen ?? true);
        }
    }

    /// <summary>Seals/unseals this door's own ship's internal edge (LocalEdgeColumnNear <->
    /// LocalEdgeColumnFar, across every ShipSim.DoorwayRows row) — what actually contains a
    /// breach/vent to this door's own side when closed, regardless of the far door/ship.</summary>
    private void ApplyLocalEdgeSeal(bool open)
    {
        if (ShipARef is null)
        {
            return;
        }

        foreach (var row in ShipSim.DoorwayRows)
        {
            var near = new CellCoord(LocalEdgeColumnNear, row);
            var far = new CellCoord(LocalEdgeColumnFar, row);

            if (open)
            {
                ShipARef.Deck.UnsealEdge(near, far);
            }
            else
            {
                ShipARef.Deck.SealEdge(near, far);
            }
        }
    }

    private static void SetBreached(ShipSim? shipSim, Vector2I tile, bool breached)
    {
        if (shipSim is null)
        {
            return;
        }

        var cell = new CellCoord(tile.X, tile.Y);
        if (breached)
        {
            shipSim.Deck.BreachHull(cell);
        }
        else
        {
            shipSim.Deck.RepairHull(cell);
        }
    }
}

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

/// <summary>
/// A real door between the Home Ship and one of its possible docking targets — replaces the old
/// instant-teleport travel console. The Station gets its own dedicated instance of this script;
/// the "away mission" instance (see <see cref="RebindFarSide"/>) is shared across every Derelict,
/// its far side repointed by TravelConsoleVerbTarget at whichever one is the current travel
/// target. While the Home Ship is actually docked here, opening it links the two ships'
/// independent <see cref="AtmosphereSystem"/>s via an <see cref="AirlockBridge"/>
/// (docs/architecture/atmosphere-power-sim.md): a breached Derelict really does start pulling
/// down the Home Ship's air while this is open. While undocked (the Home Ship is elsewhere — see
/// <see cref="Docked"/>), there's nothing coupled on the other side, so opening it instead
/// breaches both adjacent cells straight to vacuum, reusing the same hull-breach venting
/// <see cref="ShipSim"/> already seeds for a Derelict — a real consequence for forcing the wrong
/// door instead of using the travel console.
///
/// This node itself is a persistent, always-visible/collidable frame sitting right at the
/// doorway threshold — reachable by a raycast from either side without needing a mirrored
/// copy. <see cref="SlabMesh"/> is the part that actually toggles: covers most of the opening
/// and disappears when open, while the frame stays put as both the "door not fully open" visual
/// and the thing you click to close it again.
/// </summary>
public partial class AirlockDoorVerbTarget : StaticBody3D, IVerbTarget, ISaveable
{
    private static readonly Verb OpenVerb = new("open_airlock", "VERB_OPEN_AIRLOCK", DurationSeconds: 0.6f);
    private static readonly Verb CloseVerb = new("close_airlock", "VERB_CLOSE_AIRLOCK", DurationSeconds: 0.6f);

    // Placeholder/tunable — longer than the powered open/close to feel like real manual effort,
    // matching InteriorDoorVerbTarget's own PryVerb. Reuses VERB_PRY_DOOR's text rather than a
    // new key — an airlock is a door.
    private static readonly Verb PryVerb = new("pry_airlock", "VERB_PRY_DOOR", DurationSeconds: 1.5f)
    {
        Requirements = [new ItemRequirement("crowbar", 1) { Consumed = false }],
    };

    // Placeholder/tunable, matching SuitResources's drain-constant convention.
    private const float BatteryDrainPerSecond = 0.05f;

    // Same two-tier upkeep as everything else with a Deck-tracked Condition (see
    // MaintenanceTier) — this airlock's own fixture (PowerFixtureId) has been passively decaying
    // since Stage 1 with no way to repair it until now.
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

    /// <summary>Which of ShipARef's power fixtures this specific airlock instance is wired to —
    /// distinct per instance (the Station and Derelict airlocks share this one script). Always
    /// checked against ShipARef, since that's the Home Ship for both — ShipBRef (Station/
    /// Derelict) never has a battery of its own.</summary>
    [Export]
    public string PowerFixtureId { get; set; } = "";

    /// <summary>Which tile on each ship's grid the airlock connects at — the tile nearest the
    /// corridor on that ship's side (now that each ship is a real grid, not a single cell).</summary>
    [Export]
    public Vector2I TileA { get; set; }

    [Export]
    public Vector2I TileB { get; set; }

    /// <summary>The togglable body of the door — covers most of the opening, toggles
    /// visible/collidable on open/close while docked. The frame (this node) never toggles.</summary>
    [Export]
    public MeshInstance3D? SlabMesh { get; set; }

    [Export]
    public CollisionShape3D? SlabCollision { get; set; }

    [Export]
    public string SaveId { get; set; } = "";

    /// <summary>False for a destination-side door (e.g. a Station's own <c>DestinationAirlock</c>)
    /// — it shares the connection's single cross-ship <see cref="AirlockBridge"/>/vacuum-breach
    /// pair via whichever door has <see cref="OwnsBridge"/> true, rather than creating a second,
    /// redundant one. Defaults true so every existing single-door connection (Derelict) is
    /// unaffected.</summary>
    [Export]
    public bool OwnsBridge { get; set; } = true;

    /// <summary>True for a per-side door that also seals/unseals a real edge within its OWN
    /// ship's Deck (see <see cref="LocalEdgeColumnNear"/>/<see cref="LocalEdgeColumnFar"/>) —
    /// the same mechanism InteriorDoorVerbTarget.ApplyEdgeSeal already uses for room-to-room
    /// doors, generalized to a configurable column boundary. False (the default) preserves
    /// DerelictAirlock's exact existing behavior, which never seals anything locally.</summary>
    [Export]
    public bool SealsLocalEdge { get; set; }

    /// <summary>The two columns (paired with every row in ShipSim.DoorwayRows) sealed/unsealed on
    /// ShipARef.Deck when SealsLocalEdge is true — e.g. (-1, 0) for the Home Ship's own airlock
    /// door, (5, 6) for a Station's own destination-side door (its Room1/Room2-style threshold).
    /// Meaningless when SealsLocalEdge is false.</summary>
    [Export]
    public int LocalEdgeColumnNear { get; set; }

    [Export]
    public int LocalEdgeColumnFar { get; set; }

    /// <summary>The other side's door for this same connection — e.g. the Home Ship's
    /// StationAirlock and a Station's own DestinationAirlock point at each other. Null (the
    /// default, and every existing single-door connection like Derelict) means "always treat the
    /// partner as open" — see ApplyPhysicalState's own bridge-engagement check — so the cross-ship
    /// bridge behaves exactly as it always has when there's no real partner door.</summary>
    [Export]
    public AirlockDoorVerbTarget? PartnerDoorRef { get; set; }

    /// <summary>False for a Station's own destination-side door — Stations have no power grid at
    /// all today (ShipSim.HasPowerGrid unset), so gating on ShipARef.IsPowered would make that
    /// door permanently pry-only, which reads as broken rather than a deliberately unpowered
    /// station. True (the default) preserves every existing door's behavior.</summary>
    [Export]
    public bool RequiresPower { get; set; } = true;

    private bool _docked = true;

    /// <summary>Set by whichever console owns "current docked location" (e.g.
    /// TravelConsoleVerbTarget) — true only for the airlock at the Home Ship's current location.
    /// Not persisted directly; it's re-derived from the owning console's own saved state on
    /// load. Changing this re-derives the door's physical effect immediately (see
    /// <see cref="ApplyPhysicalState"/>) rather than waiting for the next open/close.</summary>
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

    // A second, independent timer/bool pair — upkeep is a third, unrelated timed action on top
    // of the existing Open/Close/Pry cycle (already juggled via _cyclingIsPry), not worth folding
    // into that one flag at this scale.
    private Timer? _maintenanceTimer;
    private bool _maintaining;

    public bool IsOpen => _isOpen;

    /// <summary>Powered: the normal instant Open/Close, same as ever. Unpowered: no motor to
    /// drive the mechanism either way, so <see cref="PryVerb"/> (a crowbar, by hand) is the only
    /// option regardless of current state — it prys the airlock open if closed, or forces it shut
    /// again if already open (see <see cref="ExecuteVerb"/>, which decides the direction from the
    /// current <see cref="IsOpen"/> state rather than from which verb was picked). Maintain/Repair
    /// is offered regardless of powered state — a console you can't currently open should still
    /// be repairable.</summary>
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

    // No HighlightVisual override needed: IVerbTarget's default (all direct VisualInstance3D
    // children) already covers both FrameMesh (the static frame, present open or closed) and
    // SlabMesh (the part that actually toggles) — FrameCollision/SlabCollision aren't
    // VisualInstance3D, so they're excluded automatically.

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

    /// <summary>Jumps straight to the saved open/closed state without replaying the timer —
    /// same "already-settled" pattern the old HullBreachVerbTarget used.</summary>
    public void ApplySaveState(bool state) => SetOpen(state);

    private void SetOpen(bool open)
    {
        _isOpen = open;
        ApplyPhysicalState();
    }

    /// <summary>Reassigns this airlock's far side to a different ShipSim — the one physical
    /// away-mission airlock is shared across every travel destination rather than each getting
    /// its own always-wired door (see TravelConsoleVerbTarget's own doc comment). A no-op if the
    /// far side isn't actually changing (repeat calls — e.g. every subsequent travel back to the
    /// SAME destination — must not reset anything). When it really does change, the door is
    /// forced shut first: an "open" state left over from the previous destination has no physical
    /// meaning at a different one, and leaving it open would bridge/vent atmosphere against the
    /// new ship before the caller sets Docked.</summary>
    public void RebindFarSide(ShipSim newShipB, AirlockDoorVerbTarget? newPartnerDoor = null)
    {
        if (ReferenceEquals(ShipBRef, newShipB))
        {
            return;
        }

        SetOpen(false);
        ShipBRef = newShipB;

        // Bidirectional: the old partner (if any) no longer refers back to this door, and the
        // new one does — see RefreshBridgeEngagement's own doc comment for why both directions
        // matter, not just this door's own PartnerDoorRef field.
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
    /// atmosphere bridging, vacuum breaching — from the current open/closed and docked/undocked
    /// state together. Called whenever either changes, so an airlock left open through a
    /// docking-state change (e.g. undocking without closing it first) immediately switches from
    /// "walk through to the other ship" to "walk/float straight out into open space," or back,
    /// without waiting for the next open/close. Open is always passable either way now — there's
    /// nothing physically in the way undocked either, just open space (see the docked/undocked
    /// distinction's other effect below: whether that's actually vacuum).</summary>
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

        // Also re-derives the partner's own bridge engagement (if it owns one) — PartnerDoorRef
        // is set bidirectionally by RebindFarSide/scene wiring specifically so that opening or
        // closing EITHER side of a connection immediately re-evaluates the bridge, rather than
        // only whichever side happens to change last leaving a stale snapshot on the other.
        RefreshBridgeEngagement();
        PartnerDoorRef?.RefreshBridgeEngagement();

        SetBreached(ShipARef, TileA, ventingToSpace);
        SetBreached(ShipBRef, TileB, ventingToSpace);
    }

    /// <summary>Only actually bridges/vents once BOTH this connection's doors report open — a
    /// null PartnerDoorRef (every existing single-door connection, e.g. Derelict) means "always
    /// treat the partner as open," preserving today's exact behavior.</summary>
    private void RefreshBridgeEngagement()
    {
        if (_bridge is not null)
        {
            _bridge.IsOpen = _isOpen && (PartnerDoorRef?.IsOpen ?? true);
        }
    }

    /// <summary>Seals/unseals this door's own ship's internal edge (LocalEdgeColumnNear <->
    /// LocalEdgeColumnFar, across every ShipSim.DoorwayRows row) — the same mechanism
    /// InteriorDoorVerbTarget.ApplyEdgeSeal uses for room-to-room doors, generalized to a
    /// configurable column boundary so this same class can represent a per-side airlock door
    /// instead of only the cross-ship bridge. This is what actually contains a breach/vent to
    /// this door's own side of the connection when closed, regardless of what the far door or
    /// far ship is doing.</summary>
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

using System.Collections.Generic;

using Godot;
using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Verbs;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// A real door between the Home Ship and one of its two possible docking targets (Station or
/// Derelict) — replaces the old instant-teleport travel console. While the Home Ship is
/// actually docked here, opening it links the two ships' independent <see cref="AtmosphereSystem"/>s
/// via an <see cref="AirlockBridge"/> (docs/architecture/atmosphere-power-sim.md): a breached
/// Derelict really does start pulling down the Home Ship's air while this is open. While
/// undocked (the Home Ship is elsewhere — see <see cref="Docked"/>), there's nothing coupled on
/// the other side, so opening it instead breaches both adjacent cells straight to vacuum,
/// reusing the same hull-breach venting <see cref="ShipSim"/> already seeds for the Derelict — a
/// real consequence for forcing the wrong door instead of using the travel console.
///
/// This node itself is a persistent, always-visible/collidable frame sitting right at the
/// doorway threshold — reachable by a raycast from either side without needing a mirrored
/// copy. <see cref="SlabMesh"/> is the part that actually toggles: covers most of the opening
/// and disappears when open, while the frame stays put as both the "door not fully open" visual
/// and the thing you click to close it again.
/// </summary>
public partial class AirlockDoorVerbTarget : StaticBody3D, IVerbTarget, ISaveable
{
    private static readonly Verb OpenVerb = new("open_airlock", "VERB_OPEN_AIRLOCK", DurationSeconds: 0.2f);
    private static readonly Verb CloseVerb = new("close_airlock", "VERB_CLOSE_AIRLOCK", DurationSeconds: 0.2f);

    // Placeholder/tunable — longer than the powered open/close to feel like real manual effort,
    // matching InteriorDoorVerbTarget's own PryVerb. Reuses VERB_PRY_DOOR's text rather than a
    // new key — an airlock is a door.
    private static readonly Verb PryVerb = new("pry_airlock", "VERB_PRY_DOOR", DurationSeconds: 1.5f)
    {
        Requirements = [new ItemRequirement("crowbar", 1) { Consumed = false }],
    };

    // Placeholder/tunable, matching SuitResources's drain-constant convention.
    private const float BatteryDrainPerSecond = 0.05f;

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

    public bool IsOpen => _isOpen;

    /// <summary>Powered: the normal instant Open/Close, same as ever. Unpowered: no motor to
    /// drive the mechanism, so the only way through a closed airlock is <see cref="PryVerb"/> (a
    /// crowbar, by hand) — and an unpowered airlock that's already open can't be closed either,
    /// matching InteriorDoorVerbTarget's own "blocks both directions" behavior.</summary>
    public IReadOnlyList<Verb> AvailableVerbs
    {
        get
        {
            if (_bridge is null || ShipARef is null)
            {
                return [];
            }

            if (ShipARef.IsPowered(PowerFixtureId))
            {
                return [IsOpen ? CloseVerb : OpenVerb];
            }

            return IsOpen ? [] : [PryVerb];
        }
    }

    public string? DisplayNameKey => "OBJECT_AIRLOCK";

    public float? CurrentVerbProgress =>
        _cycling ? 1f - (float)(_cycleTimer!.TimeLeft / _cycleTimer.WaitTime) : null;

    public override void _Ready()
    {
        if (ShipARef?.Atmosphere is { } atmosphereA && ShipBRef?.Atmosphere is { } atmosphereB)
        {
            _bridge = new AirlockBridge(
                atmosphereA, new CellCoord(TileA.X, TileA.Y),
                atmosphereB, new CellCoord(TileB.X, TileB.Y));
        }

        _cycleTimer = new Timer { OneShot = true, WaitTime = OpenVerb.DurationSeconds };
        AddChild(_cycleTimer);
        _cycleTimer.Timeout += OnCycleComplete;

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
        if (_bridge is null || _cycling || (verb.Id != OpenVerb.Id && verb.Id != CloseVerb.Id && verb.Id != PryVerb.Id))
        {
            return;
        }

        _pendingOpenState = verb.Id != CloseVerb.Id;
        _cyclingIsPry = verb.Id == PryVerb.Id;
        _cycling = true;
        _cycleTimer!.WaitTime = verb.DurationSeconds;
        _cycleTimer.Start();
    }

    public void CancelVerb()
    {
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

    public bool GetSaveState() => IsOpen;

    /// <summary>Jumps straight to the saved open/closed state without replaying the timer —
    /// same "already-settled" pattern the old HullBreachVerbTarget used.</summary>
    public void ApplySaveState(bool state) => SetOpen(state);

    private void SetOpen(bool open)
    {
        _isOpen = open;
        ApplyPhysicalState();
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

        if (_bridge is not null)
        {
            _bridge.IsOpen = passable;
        }

        SetBreached(ShipARef, TileA, ventingToSpace);
        SetBreached(ShipBRef, TileB, ventingToSpace);
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

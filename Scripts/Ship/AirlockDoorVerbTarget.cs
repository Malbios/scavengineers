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

    [Export]
    public ShipSim? ShipARef { get; set; }

    [Export]
    public ShipSim? ShipBRef { get; set; }

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

    /// <summary>Swapped onto <see cref="SlabMesh"/> instead of hiding it when the door is opened
    /// while undocked — nothing is physically there on the other side (the Home Ship hasn't
    /// travelled there), so revealing the still-present room through the frame would look wrong.
    /// The slab stays up, opaque and impassable, standing in for "open to space."</summary>
    [Export]
    public Material? SpaceMaterial { get; set; }

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
    private bool _pendingOpenState;
    private bool _isOpen;
    private Material? _doorMaterial;

    public bool IsOpen => _isOpen;

    public IReadOnlyList<Verb> AvailableVerbs => _bridge is null ? [] : [IsOpen ? CloseVerb : OpenVerb];

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

    public override void _PhysicsProcess(double delta) => _bridge?.Tick(delta);

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (_bridge is null || _cycling || (verb.Id != OpenVerb.Id && verb.Id != CloseVerb.Id))
        {
            return;
        }

        _pendingOpenState = verb.Id == OpenVerb.Id;
        _cycling = true;
        _cycleTimer!.Start();
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

    /// <summary>Re-derives the door's entire physical effect — slab visibility/material,
    /// passability, atmosphere bridging, vacuum breaching — from the current open/closed and
    /// docked/undocked state together. Called whenever either changes, so an airlock left open
    /// through a docking-state change (e.g. undocking without closing it first) immediately
    /// switches from "see/walk through to the other ship" to "opaque, impassable, venting to
    /// space," or back, without waiting for the next open/close.</summary>
    private void ApplyPhysicalState()
    {
        var passable = _isOpen && _docked;
        var ventingToSpace = _isOpen && !_docked;

        if (SlabMesh is not null)
        {
            SlabMesh.Visible = !passable;
            SlabMesh.SetSurfaceOverrideMaterial(0, ventingToSpace ? SpaceMaterial : _doorMaterial);
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

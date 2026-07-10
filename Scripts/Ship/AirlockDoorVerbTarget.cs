using System.Collections.Generic;
using Godot;
using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Verbs;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// A real door between the Home Ship and the Derelict — replaces the old instant-teleport
/// travel console. Opening it links the two ships' independent <see cref="AtmosphereSystem"/>s
/// via an <see cref="AirlockBridge"/> (docs/architecture/atmosphere-power-sim.md): a breached
/// Derelict really does start pulling down the Home Ship's air while this is open.
///
/// This node itself is a persistent, always-visible/collidable frame sitting right at the
/// doorway threshold — reachable by a raycast from either side without needing a mirrored
/// copy. <see cref="Slab"/> is the part that actually toggles: covers most of the opening and
/// disappears when open, while the frame stays put as both the "door not fully open" visual
/// and the thing you click to close it again.
/// </summary>
public partial class AirlockDoorVerbTarget : StaticBody3D, IVerbTarget, ISaveable
{
    private static readonly Verb OpenVerb = new("open_airlock", "VERB_OPEN_AIRLOCK", DurationSeconds: 1.5f);
    private static readonly Verb CloseVerb = new("close_airlock", "VERB_CLOSE_AIRLOCK", DurationSeconds: 1.5f);

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
    /// visible/collidable on open/close. The frame (this node) never toggles.</summary>
    [Export]
    public Node3D? SlabMesh { get; set; }

    [Export]
    public CollisionShape3D? SlabCollision { get; set; }

    [Export]
    public string SaveId { get; set; } = "";

    private AirlockBridge? _bridge;
    private Timer? _cycleTimer;
    private bool _cycling;
    private bool _pendingOpenState;

    public bool IsOpen => _bridge?.IsOpen ?? false;

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

        if (_bridge is not null)
        {
            _bridge.IsOpen = _pendingOpenState;
        }

        if (SlabMesh is not null)
        {
            SlabMesh.Visible = !_pendingOpenState;
        }

        if (SlabCollision is not null)
        {
            SlabCollision.Disabled = _pendingOpenState;
        }
    }

    public bool GetSaveState() => IsOpen;

    /// <summary>Jumps straight to the saved open/closed state without replaying the timer —
    /// same "already-settled" pattern the old HullBreachVerbTarget used.</summary>
    public void ApplySaveState(bool state)
    {
        if (_bridge is not null)
        {
            _bridge.IsOpen = state;
        }

        if (SlabMesh is not null)
        {
            SlabMesh.Visible = !state;
        }

        if (SlabCollision is not null)
        {
            SlabCollision.Disabled = state;
        }
    }
}

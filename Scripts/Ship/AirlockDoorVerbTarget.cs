using System.Collections.Generic;
using Godot;
using Scavengineers.Sim.Atmosphere;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Verbs;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// A real door between the Home Ship and the Derelict — replaces the old instant-teleport
/// travel console. Opening it links the two ships' independent <see cref="AtmosphereSystem"/>s
/// via an <see cref="AirlockBridge"/> (docs/architecture/atmosphere-power-sim.md): a breached
/// Derelict really does start pulling down the Home Ship's air while this is open.
///
/// The door itself only has one shared open/closed state, but needs a control panel reachable
/// from both sides of it (once closed, the barrier blocks a raycast from reaching a panel on
/// the far side). Rather than each panel owning its own independent bridge, set
/// <see cref="MirrorOf"/> on a second panel to make it a thin front end that forwards every
/// verb-target call to the primary instance instead of managing its own state.
/// </summary>
public partial class AirlockDoorVerbTarget : StaticBody3D, IVerbTarget
{
    private static readonly Verb OpenVerb = new("open_airlock", "VERB_OPEN_AIRLOCK", DurationSeconds: 1.5f);
    private static readonly Verb CloseVerb = new("close_airlock", "VERB_CLOSE_AIRLOCK", DurationSeconds: 1.5f);

    [Export]
    public ShipSim? ShipARef { get; set; }

    [Export]
    public ShipSim? ShipBRef { get; set; }

    /// <summary>The physical door slab blocking the doorway — a separate, script-less node so
    /// this control panel (the actual verb target/raycast hit) can stay mounted safely beside
    /// the opening rather than disappearing along with the barrier it operates.</summary>
    [Export]
    public Node3D? DoorBarrierMesh { get; set; }

    [Export]
    public CollisionShape3D? DoorBarrierCollision { get; set; }

    /// <summary>When set, this panel owns no state of its own — it's a second physical control
    /// point (e.g. on the Derelict side of the door) that forwards everything to the panel that
    /// actually owns the bridge/timer/barrier.</summary>
    [Export]
    public AirlockDoorVerbTarget? MirrorOf { get; set; }

    private AirlockBridge? _bridge;
    private Timer? _cycleTimer;
    private bool _cycling;
    private bool _pendingOpenState;

    public bool IsOpen => MirrorOf is not null ? MirrorOf.IsOpen : _bridge?.IsOpen ?? false;

    public IReadOnlyList<Verb> AvailableVerbs
    {
        get
        {
            if (MirrorOf is not null)
            {
                return MirrorOf.AvailableVerbs;
            }

            return _bridge is null ? [] : [IsOpen ? CloseVerb : OpenVerb];
        }
    }

    public string? DisplayNameKey => "OBJECT_AIRLOCK";

    public float? CurrentVerbProgress =>
        MirrorOf is not null
            ? MirrorOf.CurrentVerbProgress
            : _cycling ? 1f - (float)(_cycleTimer!.TimeLeft / _cycleTimer.WaitTime) : null;

    public override void _Ready()
    {
        if (MirrorOf is not null)
        {
            return; // Mirrors don't own a bridge/timer/barrier — the primary panel does.
        }

        if (ShipARef?.Atmosphere is { } atmosphereA && ShipBRef?.Atmosphere is { } atmosphereB)
        {
            _bridge = new AirlockBridge(atmosphereA, ShipSim.DemoCell, atmosphereB, ShipSim.DemoCell);
        }

        _cycleTimer = new Timer { OneShot = true, WaitTime = OpenVerb.DurationSeconds };
        AddChild(_cycleTimer);
        _cycleTimer.Timeout += OnCycleComplete;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (MirrorOf is null)
        {
            _bridge?.Tick(delta);
        }
    }

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (MirrorOf is not null)
        {
            MirrorOf.ExecuteVerb(verb, inventory);
            return;
        }

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
        if (MirrorOf is not null)
        {
            MirrorOf.CancelVerb();
            return;
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

        if (_bridge is not null)
        {
            _bridge.IsOpen = _pendingOpenState;
        }

        if (DoorBarrierMesh is not null)
        {
            DoorBarrierMesh.Visible = !_pendingOpenState;
        }

        if (DoorBarrierCollision is not null)
        {
            DoorBarrierCollision.Disabled = _pendingOpenState;
        }
    }
}

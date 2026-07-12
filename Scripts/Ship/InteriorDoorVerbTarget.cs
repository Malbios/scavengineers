using System.Collections.Generic;

using Godot;
using Scavengineers.Sim.Grid;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Verbs;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// A real door between a ship's own two rooms — unlike AirlockDoorVerbTarget (which links
/// two independent ships' atmosphere via a bridge), this seals/unseals two edges within the
/// SAME ship's Deck directly (Deck.SealEdge/UnsealEdge — the exact mechanic
/// AtmosphereSystemTests.TwoSealedRoomsJoinedByOpenDoor_... already covers).
///
/// This node itself is a persistent, always-visible/collidable frame at the doorway threshold
/// — reachable from either room without a mirrored copy. <see cref="Slab"/> is the part that
/// actually toggles: covers most of the opening and disappears when open, while the frame
/// stays put as both the "door not fully open" visual and the thing you click to close it.
/// </summary>
public partial class InteriorDoorVerbTarget : StaticBody3D, IVerbTarget, ISaveable
{
    private static readonly Verb OpenVerb = new("open_door", "VERB_OPEN_DOOR", DurationSeconds: 0.2f);
    private static readonly Verb CloseVerb = new("close_door", "VERB_CLOSE_DOOR", DurationSeconds: 0.2f);

    // Placeholder/tunable, matching SuitResources's drain-constant convention.
    private const float BatteryDrainPerSecond = 0.03f;

    // The room-1/room-2 doorway is always these 4 tiles, per ShipSim's fixed grid (Room 1 =
    // i 0-5, Room 2 = i 6-11, doorway at j=2,3).
    private static readonly (CellCoord A, CellCoord B) Edge1 = (new CellCoord(5, 2), new CellCoord(6, 2));
    private static readonly (CellCoord A, CellCoord B) Edge2 = (new CellCoord(5, 3), new CellCoord(6, 3));

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    /// <summary>Whether opening/closing this door needs <see cref="ShipSim.InteriorDoorFixtureId"/>
    /// powered — true for the Home Ship (a real "keep your systems running" gate). The Derelict
    /// has no working power grid of its own (see <see cref="ShipSim.HasPowerGrid"/>, never set for
    /// it), so its own interior door sets this false — otherwise <see cref="AvailableVerbs"/>
    /// would always be empty and the door could never be opened or closed again.</summary>
    [Export]
    public bool RequiresPower { get; set; } = true;

    /// <summary>The togglable body of the door — covers most of the opening, toggles
    /// visible/collidable on open/close. The frame (this node) never toggles.</summary>
    [Export]
    public Node3D? SlabMesh { get; set; }

    [Export]
    public CollisionShape3D? SlabCollision { get; set; }

    [Export]
    public string SaveId { get; set; } = "";

    private Timer? _cycleTimer;
    private bool _cycling;
    private bool _pendingOpenState;

    // Starts open, matching the doorway's permanently-unsealed default before this door existed.
    private bool _isOpen = true;

    public bool IsOpen => _isOpen;

    public IReadOnlyList<Verb> AvailableVerbs =>
        ShipSimRef is not null && (!RequiresPower || ShipSimRef.IsPowered(ShipSim.InteriorDoorFixtureId))
            ? [IsOpen ? CloseVerb : OpenVerb]
            : [];

    public string? DisplayNameKey => "OBJECT_DOOR";

    public float? CurrentVerbProgress =>
        _cycling ? 1f - (float)(_cycleTimer!.TimeLeft / _cycleTimer.WaitTime) : null;

    public override void _Ready()
    {
        _cycleTimer = new Timer { OneShot = true, WaitTime = OpenVerb.DurationSeconds };
        AddChild(_cycleTimer);
        _cycleTimer.Timeout += OnCycleComplete;

        // Sync the slab's visibility/collision to the starting _isOpen = true — the scene
        // file's own initial state for these nodes must agree, but this makes it correct even
        // if it doesn't. Deliberately not calling ApplyEdgeSeal here: ShipSimRef's own Deck may
        // not be built yet at this exact point in _Ready() order, and the doorway already starts
        // unsealed by default, matching _isOpen's own starting value.
        ApplySlabVisual();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_cycling)
        {
            ShipSimRef?.DrainBattery(BatteryDrainPerSecond * (float)delta);
        }
    }

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (ShipSimRef is null || _cycling || (verb.Id != OpenVerb.Id && verb.Id != CloseVerb.Id))
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
        _isOpen = _pendingOpenState;
        ApplyEdgeSeal();
        ApplySlabVisual();
    }

    public bool GetSaveState() => _isOpen;

    /// <summary>Jumps straight to the saved open/closed state without replaying the timer —
    /// same "already-settled" pattern the old HullBreachVerbTarget used.</summary>
    public void ApplySaveState(bool state)
    {
        _isOpen = state;
        ApplyEdgeSeal();
        ApplySlabVisual();
    }

    private void ApplyEdgeSeal()
    {
        if (_isOpen)
        {
            ShipSimRef?.Deck.UnsealEdge(Edge1.A, Edge1.B);
            ShipSimRef?.Deck.UnsealEdge(Edge2.A, Edge2.B);
        }
        else
        {
            ShipSimRef?.Deck.SealEdge(Edge1.A, Edge1.B);
            ShipSimRef?.Deck.SealEdge(Edge2.A, Edge2.B);
        }
    }

    private void ApplySlabVisual()
    {
        if (SlabMesh is not null)
        {
            SlabMesh.Visible = !_isOpen;
        }

        if (SlabCollision is not null)
        {
            SlabCollision.Disabled = _isOpen;
        }
    }
}

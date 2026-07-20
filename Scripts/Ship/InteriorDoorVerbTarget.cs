using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;
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
    private static readonly Verb OpenVerb = new("open_door", "VERB_OPEN_DOOR", DurationSeconds: 0.6f);
    private static readonly Verb CloseVerb = new("close_door", "VERB_CLOSE_DOOR", DurationSeconds: 0.6f);

    // Placeholder/tunable — longer than the powered open/close to feel like real manual effort.
    private static readonly Verb PryVerb = new("pry_door", "VERB_PRY_DOOR", DurationSeconds: 1.5f)
    {
        Requirements = [new ItemRequirement("crowbar", 1) { Consumed = false }],
    };

    // Placeholder/tunable, matching SuitResources's drain-constant convention.
    private const float BatteryDrainPerSecond = 0.03f;

    // The room-1/room-2 doorway is always these 4 tiles, per ShipSim's fixed grid (Room 1 =
    // i 0-5, Room 2 = i 6-11, doorway at j=2,3).
    private static readonly (CellCoord A, CellCoord B) Edge1 = (new CellCoord(5, 2), new CellCoord(6, 2));
    private static readonly (CellCoord A, CellCoord B) Edge2 = (new CellCoord(5, 3), new CellCoord(6, 3));

    // Same two-tier upkeep as everything else with a Deck-tracked Condition (see
    // MaintenanceTier) — this door's own fixture (ShipSim.InteriorDoorFixtureId) has been
    // passively decaying since it started existing unconditionally, with no way to repair it
    // until now.
    private static readonly ItemRequirement WrenchRequirement = new("wrench", 1) { Consumed = false };
    private static readonly ItemRequirement SparePartsRequirement = new("spare_parts", 1);

    private static readonly Verb MaintainInteriorDoorVerb = new("maintain_interior_door", "VERB_MAINTAIN_INTERIOR_DOOR", DurationSeconds: 0.6f)
    {
        Requirements = [WrenchRequirement],
    };

    private static readonly Verb RepairInteriorDoorVerb = new("repair_interior_door", "VERB_REPAIR_INTERIOR_DOOR", DurationSeconds: 0.6f)
    {
        Requirements = [WrenchRequirement, SparePartsRequirement],
    };

    [Export]
    public ShipSim? ShipSimRef { get; set; }

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
    private bool _cyclingIsPry;
    private bool _pendingOpenState;
    private bool _isOpen;

    // A second, independent timer/bool pair — upkeep is a third, unrelated timed action on top
    // of the existing Open/Close/Pry cycle (already juggled via _cyclingIsPry), not worth folding
    // into that one flag at this scale.
    private Timer? _maintenanceTimer;
    private bool _maintaining;

    public bool IsOpen => _isOpen;

    /// <summary>Powered: the normal instant Open/Close, same as ever. Unpowered: no ship motor to
    /// drive the mechanism either way, so <see cref="PryVerb"/> (a crowbar, by hand) is the only
    /// option regardless of current state — it prys the door open if closed, or forces it shut
    /// again if already open (see <see cref="ExecuteVerb"/>, which decides the direction from the
    /// current <see cref="IsOpen"/> state rather than from which verb was picked). Maintain/Repair
    /// is offered regardless of powered state — a door you can't currently open should still be
    /// repairable.</summary>
    public IReadOnlyList<Verb> AvailableVerbs
    {
        get
        {
            if (ShipSimRef is null)
            {
                return [];
            }

            var verbs = new List<Verb> { ShipSimRef.IsPowered(ShipSim.InteriorDoorFixtureId) ? (IsOpen ? CloseVerb : OpenVerb) : PryVerb };

            if (DoorFixture is { } fixture && MaintenanceTier.PickVerb(fixture.Condition, MaintainInteriorDoorVerb, RepairInteriorDoorVerb) is { } upkeepVerb)
            {
                verbs.Add(upkeepVerb);
            }

            return verbs;
        }
    }

    public string? DisplayNameKey => "OBJECT_DOOR";

    private Fixture? DoorFixture => ShipSimRef?.Deck.Fixtures.FirstOrDefault(f => f.Id == ShipSim.InteriorDoorFixtureId);

    public float? Condition => DoorFixture?.Condition;

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
        _cycleTimer = new Timer { OneShot = true };
        AddChild(_cycleTimer);
        _cycleTimer.Timeout += OnCycleComplete;

        _maintenanceTimer = new Timer { OneShot = true, WaitTime = MaintainInteriorDoorVerb.DurationSeconds };
        AddChild(_maintenanceTimer);
        _maintenanceTimer.Timeout += OnMaintenanceComplete;

        // Deferred for two reasons: ShipSimRef's own Deck/power may not be built yet at this
        // exact point in _Ready() order (needed to decide the starting _isOpen below), and a
        // CollisionShape3D's very first Disabled toggle in the same frame it enters the tree can
        // race the physics server's own "shape added" registration for that frame (Jolt in
        // particular) — the write is silently lost and the shape stays solid until some later
        // toggle forces a real update. One frame lands after both.
        CallDeferred(nameof(ApplyInitialState));
    }

    /// <summary>A fresh, unpowered ship starts with its interior doors closed (no motor to hold
    /// them open) rather than the old hardcoded "always starts open" — matching PryVerb's own
    /// "closed is the state you need a crowbar for" framing. A loaded save overrides this via
    /// ApplySaveState regardless, same as before.</summary>
    private void ApplyInitialState()
    {
        _isOpen = ShipSimRef?.IsPowered(ShipSim.InteriorDoorFixtureId) ?? false;
        ApplyEdgeSeal();
        ApplySlabVisual();
    }

    public override void _PhysicsProcess(double delta)
    {
        // A pry is manual force, not motor-driven — no ship battery involved.
        if (_cycling && !_cyclingIsPry)
        {
            ShipSimRef?.DrainBattery(BatteryDrainPerSecond * (float)delta);
        }
    }

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id == MaintainInteriorDoorVerb.Id || verb.Id == RepairInteriorDoorVerb.Id)
        {
            if (!_maintaining)
            {
                _maintaining = true;
                _maintenanceTimer!.Start();
            }

            return;
        }

        if (ShipSimRef is null || _cycling || (verb.Id != OpenVerb.Id && verb.Id != CloseVerb.Id && verb.Id != PryVerb.Id))
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
        _isOpen = _pendingOpenState;
        ApplyEdgeSeal();
        ApplySlabVisual();
    }

    private void OnMaintenanceComplete()
    {
        _maintaining = false;
        if (DoorFixture is { } fixture)
        {
            fixture.Condition = 1f;
        }
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

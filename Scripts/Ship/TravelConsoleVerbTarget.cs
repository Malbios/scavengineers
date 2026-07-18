using System;
using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Travel;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.ShipModel;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// The Home Ship's simplified/abstracted travel step (docs/project-plan.md §4 — not full
/// Newtonian piloting, just a timed wait standing in for undocking, flying, and docking).
/// Owns which of the Home Ship's possible destinations (the Station plus a data-authored list of
/// Derelicts) it currently occupies, and keeps both AirlockDoorVerbTargets' <see
/// cref="AirlockDoorVerbTarget.Docked"/> flag in sync. Doesn't touch either airlock's open/closed
/// state itself — whatever you left a door doing when you left is still what it's doing when
/// you're back (see AirlockDoorVerbTarget's own handling of an open-but-undocked door venting to
/// vacuum instead of auto-closing).
///
/// A destination is a single unified int: 0 = Station, 1..DerelictCount = Derelict N. Executing
/// the Travel verb doesn't start traveling directly anymore — it opens the travel map (see
/// Player.OpenTravelMap), which calls back into <see cref="BeginTravel"/> once the player picks
/// and confirms a destination.
/// </summary>
public partial class TravelConsoleVerbTarget : StaticBody3D, IVerbTarget, IStateSaveable
{
    // TravelVerb.DurationSeconds only seeds _travelTimer's default before the first real
    // BeginTravel call recomputes WaitTime from the formula below.
    private static readonly Verb TravelVerb = new("travel", "VERB_TRAVEL", DurationSeconds: 0.2f);

    // Placeholder/tunable — base travel duration before thruster reduction, restoring the
    // original "a real beat" value (previously dropped to near-instant for testing) now that
    // installed+fueled thrusters make the duration mean something (see BeginTravel).
    private const float BaseTravelSeconds = 4f;
    private const float ReductionPerThruster = 1f;
    private const float MinTravelSeconds = 1f;

    // Placeholder/tunable, matching SuitResources's drain-constant convention.
    private const float BatteryDrainPerSecond = 0.05f;

    // Placeholder/tunable — independent per thruster, so N fueled thrusters drain N times as
    // fast in total (no shared pool).
    private const float ThrusterDrainPerSecond = 0.02f;

    // Same two-tier upkeep as everything else with a Deck-tracked Condition (see
    // MaintenanceTier) — this console's own fixture (ShipSim.TravelConsoleFixtureId) has been
    // passively decaying since Stage 1 with no way to repair it until now.
    private static readonly ItemRequirement WrenchRequirement = new("wrench", 1) { Consumed = false };
    private static readonly ItemRequirement SparePartsRequirement = new("spare_parts", 1);

    private static readonly Verb MaintainTravelConsoleVerb = new("maintain_travel_console", "VERB_MAINTAIN_TRAVEL_CONSOLE", DurationSeconds: 0.2f)
    {
        Requirements = [WrenchRequirement],
    };

    private static readonly Verb RepairTravelConsoleVerb = new("repair_travel_console", "VERB_REPAIR_TRAVEL_CONSOLE", DurationSeconds: 0.2f)
    {
        Requirements = [WrenchRequirement, SparePartsRequirement],
    };

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    [Export]
    public AirlockDoorVerbTarget? StationAirlock { get; set; }

    /// <summary>The one shared "away mission" airlock — its far side gets repointed at whichever
    /// Derelict is the current travel target (see AirlockDoorVerbTarget.RebindFarSide), rather
    /// than each Derelict getting its own always-wired door.</summary>
    [Export]
    public AirlockDoorVerbTarget? DerelictAirlock { get; set; }

    /// <summary>The Station structure itself (not its connecting corridor — that's the Home
    /// Ship's own boarding-tube apparatus, always present) — hidden and decollided while the Home
    /// Ship isn't docked there, so it and whichever Derelict is current aren't both spatially
    /// present at once. The airlock alone only fakes this from ground level (see
    /// AirlockDoorVerbTarget's own "opens to space while undocked" doc) — flying outside in
    /// zero-g would otherwise reveal both ships floating right next to each other regardless of
    /// which one's actually docked.</summary>
    [Export]
    public Node3D? StationGroup { get; set; }

    // Placeholder/tunable — roughly centered among the derelicts below, for the travel map's
    // spatial layout only; has no gameplay effect (no travel-time/distance mechanic yet).
    [Export]
    public Vector2 StationMapPosition { get; set; } = new(220, 180);

    /// <summary>Parallel arrays, index i describing Derelict i+1 across all three — the same
    /// per-instance-override mechanism ShipSim's own GridWidth/RoomSplitColumns already use,
    /// just three arrays instead of one field per ship. Not a data-driven (JSON/.tres) ship list
    /// — that's aspirational per CLAUDE.md and unimplemented everywhere else in this codebase
    /// too; these stay scene-authored exported references, matching Station/Derelict/HomeShip.
    /// NodePath arrays rather than Node3D/ShipSim arrays — a raw Node reference can't itself be
    /// serialized as a plain Variant in a .tscn (unlike a single [Export] Node3D field, which
    /// Godot resolves through its own node_paths mechanism, there's no equivalent auto-resolved
    /// array-of-Node export), so each path is resolved once via GetNode in _Ready instead.</summary>
    [Export]
    public Godot.Collections.Array<NodePath> DerelictGroupPaths { get; set; } = new();

    [Export]
    public Godot.Collections.Array<NodePath> DerelictShipSimPaths { get; set; } = new();

    // Placeholder/tunable — arbitrary spatial layout for the travel map, no gameplay effect.
    [Export]
    public Godot.Collections.Array<Vector2> DerelictMapPositions { get; set; } = new();

    [Export]
    public string SaveId { get; set; } = "";

    private Timer? _travelTimer;
    private bool _traveling;
    private int _currentDestination; // 0 = Station, 1..DerelictCount = Derelict N
    private int _pendingDestination;
    private readonly List<Node3D> _derelictGroups = new();
    private readonly List<ShipSim> _derelictShipSims = new();

    // A second, independent timer/bool pair — travel and upkeep are two unrelated timed actions
    // on the same object, not worth folding into one PendingAction-style dispatch at this scale.
    private Timer? _maintenanceTimer;
    private bool _maintaining;

    /// <summary>Bounds every loop/lookup below defensively against the three parallel arrays
    /// above being resized inconsistently by hand in the inspector.</summary>
    private int DerelictCount => Math.Min(_derelictGroups.Count, Math.Min(_derelictShipSims.Count, DerelictMapPositions.Count));

    public IReadOnlyList<Verb> AvailableVerbs =>
        [
            .. ShipSimRef is not null && ShipSimRef.IsPowered(ShipSim.TravelConsoleFixtureId) ? new[] { TravelVerb } : [],
            // Offered regardless of current power state — a console you can't currently use to
            // travel should still be repairable.
            .. ConsoleFixture is { } fixture && MaintenanceTier.PickVerb(fixture.Condition, MaintainTravelConsoleVerb, RepairTravelConsoleVerb) is { } upkeepVerb ? new[] { upkeepVerb } : [],
        ];

    public string? DisplayNameKey => "OBJECT_SHIP_CONSOLE";

    /// <summary>The Deck fixture backing this console's own passive wear — reused from
    /// ShipSim.TravelConsoleFixtureId (added at startup whenever HasPowerGrid is true) rather
    /// than a new one, since it already exists purely for power routing.</summary>
    private Fixture? ConsoleFixture => ShipSimRef?.Deck.Fixtures.FirstOrDefault(f => f.Id == ShipSim.TravelConsoleFixtureId);

    public float? Condition => ConsoleFixture?.Condition;

    public float? CurrentVerbProgress =>
        _traveling ? 1f - (float)(_travelTimer!.TimeLeft / _travelTimer.WaitTime)
        : _maintaining ? 1f - (float)(_maintenanceTimer!.TimeLeft / _maintenanceTimer.WaitTime)
        : null;

    public override void _Ready()
    {
        if (DerelictGroupPaths.Count != DerelictShipSimPaths.Count || DerelictGroupPaths.Count != DerelictMapPositions.Count)
        {
            GD.PushWarning("[TravelConsoleVerbTarget] Mismatched derelict array lengths — extra entries ignored.");
        }

        foreach (var path in DerelictGroupPaths)
        {
            _derelictGroups.Add(GetNode<Node3D>(path));
        }

        foreach (var path in DerelictShipSimPaths)
        {
            _derelictShipSims.Add(GetNode<ShipSim>(path));
        }

        _travelTimer = new Timer { OneShot = true, WaitTime = TravelVerb.DurationSeconds };
        AddChild(_travelTimer);
        _travelTimer.Timeout += OnTravelComplete;

        _maintenanceTimer = new Timer { OneShot = true, WaitTime = MaintainTravelConsoleVerb.DurationSeconds };
        AddChild(_maintenanceTimer);
        _maintenanceTimer.Timeout += OnMaintenanceComplete;

        // Deferred TWICE, not once: every Derelict's own Floor.SeedDefaultShipLayout (which
        // spawns the actual wall/conduit/machine CollisionShape3D children) is ALSO deferred,
        // from that Floor's own _Ready() — and since HomeShip (this node's ancestor) readies
        // before Derelict1..5 as an earlier scene-tree sibling (see World.tscn), a single
        // CallDeferred here would run BEFORE those walls exist, leaving them permanently
        // un-decollided for whichever derelict isn't the starting location (SetShipPresence can
        // only disable colliders that already exist at the moment it runs). Deferring a second
        // time lands this call at the back of the same deferred-call flush, after every already-
        // queued SeedDefaultShipLayout call, guaranteeing the walls exist first.
        CallDeferred(nameof(DeferApplyCurrentLocationOnceMore));
    }

    private void DeferApplyCurrentLocationOnceMore() => CallDeferred(nameof(ApplyCurrentLocation));

    public override void _PhysicsProcess(double delta)
    {
        if (_traveling)
        {
            ShipSimRef?.DrainBattery(BatteryDrainPerSecond * (float)delta);

            if (ShipSimRef is not null)
            {
                foreach (var thruster in ShipSimRef.Deck.Fixtures.OfType<ThrusterFixture>())
                {
                    if (thruster.Condition > 0f && ShipSimRef.IsPowered(thruster.Id))
                    {
                        thruster.Condition = Math.Max(0f, thruster.Condition - ThrusterDrainPerSecond * (float)delta);
                    }
                }
            }
        }
    }

    /// <summary>Opens the travel map instead of starting travel directly — resolves the player
    /// via the "player" group (same lookup ContainerPickupItem.GetPlayer already uses to reach
    /// the other direction), matching the verb system's own "no bespoke per-object input path"
    /// rule: Player still just calls ExecuteVerb generically, and it's this console's own
    /// business to decide the verb opens a UI panel it doesn't own a direct scene reference to.</summary>
    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id == MaintainTravelConsoleVerb.Id || verb.Id == RepairTravelConsoleVerb.Id)
        {
            if (!_maintaining)
            {
                _maintaining = true;
                _maintenanceTimer!.Start();
            }

            return;
        }

        if (verb.Id != TravelVerb.Id || _traveling)
        {
            return;
        }

        if (GetTree().GetFirstNodeInGroup("player") is PlayerScript player)
        {
            player.OpenTravelMap(this);
        }
    }

    public void CancelVerb()
    {
        if (_maintaining)
        {
            _maintaining = false;
            _maintenanceTimer!.Stop();
        }

        if (!_traveling)
        {
            return;
        }

        _traveling = false;
        _travelTimer!.Stop();
    }

    private void OnMaintenanceComplete()
    {
        _maintaining = false;
        if (ConsoleFixture is { } fixture)
        {
            fixture.Condition = 1f;
        }
    }

    /// <summary>Called back from the travel map (via Player.ConfirmTravel) once a destination is
    /// picked and confirmed — the actual timed-travel trigger. A no-op for the destination
    /// already occupied, an out-of-range id, or while a travel is already in flight.</summary>
    public void BeginTravel(int destinationId)
    {
        if (_traveling || destinationId == _currentDestination || destinationId < 0 || destinationId > DerelictCount)
        {
            return;
        }

        _pendingDestination = destinationId;
        _traveling = true;

        // Computed once at the start of the trip, not re-evaluated mid-flight — matches
        // DrainBattery's own existing behavior of having zero effect on an already-running timer
        // if the battery empties mid-trip.
        var fueledCount = ShipSimRef?.Deck.Fixtures.OfType<ThrusterFixture>().Count(f => f.Condition > 0f && ShipSimRef!.IsPowered(f.Id)) ?? 0;
        _travelTimer!.WaitTime = Math.Max(MinTravelSeconds, BaseTravelSeconds - fueledCount * ReductionPerThruster);
        _travelTimer.Start();
    }

    public int CurrentDestinationId => _currentDestination;

    /// <summary>Station first, then every Derelict — for the travel map to render uniformly
    /// without special-casing Station vs. Derelict itself.</summary>
    public IReadOnlyList<TravelMapEntry> BuildMapEntries()
    {
        var entries = new List<TravelMapEntry> { new(0, "OBJECT_STATION", StationMapPosition, _currentDestination == 0) };
        for (var i = 0; i < DerelictCount; i++)
        {
            entries.Add(new(i + 1, $"OBJECT_DERELICT_{i + 1}", DerelictMapPositions[i], _currentDestination == i + 1));
        }

        return entries;
    }

    private void OnTravelComplete()
    {
        _traveling = false;
        _currentDestination = _pendingDestination;
        ApplyCurrentLocation();
    }

    public string GetSaveState() => _currentDestination == 0 ? "station" : $"derelict_{_currentDestination}";

    public void ApplySaveState(string state)
    {
        _currentDestination = state switch
        {
            "station" => 0,
            "derelict" => 1, // legacy pre-map save (bare "derelict") -> Derelict 1 specifically,
                              // preserving "I was away from the station" intent rather than a
                              // generic reset back to Station.
            _ when state.StartsWith("derelict_") && int.TryParse(state.AsSpan("derelict_".Length), out var n)
                && n >= 1 && n <= DerelictCount => n,
            _ => 0, // unrecognized -> Station, matching this codebase's existing fallback shape.
        };
        ApplyCurrentLocation();
    }

    private void ApplyCurrentLocation()
    {
        var atStation = _currentDestination == 0;
        var derelictIndex = _currentDestination - 1;

        if (StationAirlock is not null)
        {
            StationAirlock.Docked = atStation;
        }

        if (DerelictAirlock is not null)
        {
            if (!atStation && derelictIndex >= 0 && derelictIndex < _derelictShipSims.Count)
            {
                DerelictAirlock.RebindFarSide(_derelictShipSims[derelictIndex]);
            }

            DerelictAirlock.Docked = !atStation;
        }

        SetShipPresence(StationGroup, atStation);
        for (var i = 0; i < _derelictGroups.Count; i++)
        {
            SetShipPresence(_derelictGroups[i], !atStation && i == derelictIndex);
        }
    }

    /// <summary>Toggles all three halves of "is this ship actually here": Node3D.Visible for
    /// rendering, every descendant CollisionShape3D's own Disabled flag for physics — Visible
    /// alone doesn't stop the player from walking/floating into a hidden ship's geometry — and
    /// every descendant IPhysicsPresenceAware (loose PickupItem/ContainerPickupItem pickups),
    /// since a live RigidBody3D has nothing to rest on once this group's own collision is
    /// disabled and would otherwise fall through the now-decollided floor forever.</summary>
    private static void SetShipPresence(Node3D? group, bool present)
    {
        if (group is null)
        {
            return;
        }

        group.Visible = present;

        foreach (var node in group.FindChildren("*", nameof(CollisionShape3D), recursive: true, owned: false))
        {
            if (node is CollisionShape3D shape)
            {
                shape.Disabled = !present;
            }
        }

        foreach (var node in group.FindChildren("*", recursive: true, owned: false))
        {
            if (node is IPhysicsPresenceAware presenceAware)
            {
                presenceAware.SetPhysicsPresence(present);
            }
        }
    }
}

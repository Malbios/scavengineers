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
/// Owns which of the Home Ship's possible destinations (a data-authored list of Stations plus a
/// data-authored list of Derelicts) it currently occupies, and keeps every AirlockDoorVerbTarget's
/// <see cref="AirlockDoorVerbTarget.Docked"/> flag in sync. Doesn't touch any airlock's own
/// open/closed state itself — whatever you left a door doing when you left is still what it's
/// doing when you're back (see AirlockDoorVerbTarget's own handling of an open-but-undocked door
/// venting to vacuum instead of auto-closing).
///
/// A destination is a single unified int: 0..StationCount-1 = Station N, StationCount..
/// StationCount+DerelictCount-1 = Derelict N. Stations now use the exact same rebind pattern
/// Derelicts already prove out: one shared Home-Ship-side airlock (StationAirlock) whose far side
/// gets repointed at whichever Station is current (AirlockDoorVerbTarget.RebindFarSide) — plus,
/// unlike Derelicts, each Station also has its own destination-side door
/// (StationDestinationAirlockPaths) living in its own subtree, only ever reachable when that
/// Station is the current destination (see SetShipPresence). The two doors of a connection only
/// actually bridge atmosphere when both report open (see AirlockDoorVerbTarget.PartnerDoorRef) —
/// this is what replaced the old one-dedicated-door-per-Station model, which let two always-
/// present adjacent doors flood the whole ship when the wrong one was opened. Executing the
/// Travel verb doesn't start traveling directly anymore — it opens the travel map (see
/// Player.OpenTravelMap), which calls back into <see cref="BeginTravel"/> once the player picks
/// and confirms a destination.
/// </summary>
public partial class TravelConsoleVerbTarget : StaticBody3D, IVerbTarget, IStateSaveable
{
    // TravelVerb.DurationSeconds only seeds _travelTimer's default before the first real
    // BeginTravel call recomputes WaitTime from the formula below.
    private static readonly Verb TravelVerb = new("travel", "VERB_TRAVEL", DurationSeconds: 0.6f);

    /// <summary>Offered instead of TravelVerb whenever _docking is true — how the player gets
    /// back into a docking attempt that didn't auto-open (see OnTravelComplete's own doc
    /// comment).</summary>
    private static readonly Verb ResumeDockingVerb = new("resume_docking", "VERB_RESUME_DOCKING", DurationSeconds: 0.6f);

    // Placeholder/tunable — dialed down for the current testing phase so transit doesn't cost
    // real waiting time while iterating on other mechanics; still long enough to see the verb
    // progress bar actually sweep rather than jump-cutting. Public settable (not const), matching
    // this codebase's own SaveManager.AutosaveIntervalSeconds-style testability convention, so
    // tests can dial the wait down further instead of really waiting out a pacing-only duration.
    // Revisit upward for a real sense of transit taking a while once pacing is being tuned for
    // release rather than for fast iteration.
    public float BaseTravelSeconds { get; set; } = 3f;
    public float ReductionPerThruster { get; set; } = 0.3f;
    public float MinTravelSeconds { get; set; } = 1f;

    // Placeholder/tunable, matching SuitResources's drain-constant convention. Only applied
    // during the bounded _traveling phase (see the drain block in _PhysicsProcess) — the
    // open-ended _docking phase that follows costs nothing per-second, or a slow/careful docking
    // attempt could drain the whole battery regardless of how short the actual trip was.
    private const float BatteryDrainPerSecond = 0.01f;

    // Placeholder/tunable — independent per thruster, so N fueled thrusters drain N times as
    // fast in total (no shared pool). Same "_traveling only" bound as BatteryDrainPerSecond.
    private const float ThrusterDrainPerSecond = 0.02f;

    // Same two-tier upkeep as everything else with a Deck-tracked Condition (see
    // MaintenanceTier) — this console's own fixture (ShipSim.TravelConsoleFixtureId) has been
    // passively decaying since Stage 1 with no way to repair it until now.
    private static readonly ItemRequirement WrenchRequirement = new("wrench", 1) { Consumed = false };
    private static readonly ItemRequirement SparePartsRequirement = new("spare_parts", 1);

    private static readonly Verb MaintainTravelConsoleVerb = new("maintain_travel_console", "VERB_MAINTAIN_TRAVEL_CONSOLE", DurationSeconds: 0.6f)
    {
        Requirements = [WrenchRequirement],
    };

    private static readonly Verb RepairTravelConsoleVerb = new("repair_travel_console", "VERB_REPAIR_TRAVEL_CONSOLE", DurationSeconds: 0.6f)
    {
        Requirements = [WrenchRequirement, SparePartsRequirement],
    };

    [Export]
    public ShipSim? ShipSimRef { get; set; }

    /// <summary>The one shared "away mission" airlock — its far side gets repointed at whichever
    /// Derelict is the current travel target (see AirlockDoorVerbTarget.RebindFarSide), rather
    /// than each Derelict getting its own always-wired door.</summary>
    [Export]
    public AirlockDoorVerbTarget? DerelictAirlock { get; set; }

    /// <summary>The one shared Home-Ship-side Station airlock — its far side (and partner door,
    /// see RebindFarSide's second parameter) gets repointed at whichever Station is the current
    /// travel target, mirroring DerelictAirlock exactly.</summary>
    [Export]
    public AirlockDoorVerbTarget? StationAirlock { get; set; }

    /// <summary>Parallel arrays, index i describing Station i across all four — same mechanism as
    /// the Derelict arrays below. Each group is hidden and decollided while the Home Ship isn't
    /// docked there, so it and whichever other destination is current aren't both spatially
    /// present at once; flying outside in zero-g would otherwise reveal every ship floating right
    /// next to each other regardless of which one's actually docked.</summary>
    [Export]
    public Godot.Collections.Array<NodePath> StationGroupPaths { get; set; } = new();

    [Export]
    public Godot.Collections.Array<NodePath> StationShipSimPaths { get; set; } = new();

    /// <summary>Each Station's own destination-side door — StationAirlock's RebindFarSide
    /// partner, only ever physically reachable while that Station is the current destination
    /// (see SetShipPresence). Distinct per Station, unlike StationAirlock itself.</summary>
    [Export]
    public Godot.Collections.Array<NodePath> StationDestinationAirlockPaths { get; set; } = new();

    /// <summary>Each Station's own Floor (ShipBuildTarget) — resolves a destination id to a spawn
    /// point for ContractGiverVerbTarget's CargoDelivery contracts (see GetStationBuildTarget),
    /// mirroring DerelictBuildTargetPaths/GetDerelictBuildTarget below. Deliberately NOT folded
    /// into StationCount's own Math.Min: a scene that hasn't wired this array yet keeps a fully
    /// working travel map regardless, since GetStationBuildTarget just returns null.</summary>
    [Export]
    public Godot.Collections.Array<NodePath> StationBuildTargetPaths { get; set; } = new();

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

    /// <summary>Each Derelict's own Floor (ShipBuildTarget) — resolves a destination id to a
    /// spawn point for ContractGiverVerbTarget's RetrieveItem contracts (see
    /// GetDerelictBuildTarget). Deliberately NOT folded into DerelictCount's own Math.Min: a scene
    /// that hasn't wired this array yet keeps a fully working travel map regardless, since
    /// GetDerelictBuildTarget just returns null (contract still gets accepted, nothing to
    /// physically spawn).</summary>
    [Export]
    public Godot.Collections.Array<NodePath> DerelictBuildTargetPaths { get; set; } = new();

    [Export]
    public string SaveId { get; set; } = "";

    private Timer? _travelTimer;
    private bool _traveling;

    /// <summary>True once the travel timer elapses but before the docking minigame's own Dock
    /// button succeeds — the ship has "arrived in open space" near the target but isn't there
    /// yet. See CompleteDocking, called by Player once the minigame's Dock button succeeds.</summary>
    private bool _docking;

    private int _currentDestination; // 0..StationCount-1 = Station N, StationCount.. = Derelict N
    private int _pendingDestination;
    private readonly List<Node3D> _stationGroups = new();
    private readonly List<ShipSim> _stationShipSims = new();
    private readonly List<AirlockDoorVerbTarget> _stationDestinationAirlocks = new();
    private readonly List<ShipBuildTarget> _stationBuildTargets = new();
    private readonly List<Node3D> _derelictGroups = new();
    private readonly List<ShipSim> _derelictShipSims = new();
    private readonly List<ShipBuildTarget> _derelictBuildTargets = new();

    // A second, independent timer/bool pair — travel and upkeep are two unrelated timed actions
    // on the same object, not worth folding into one PendingAction-style dispatch at this scale.
    private Timer? _maintenanceTimer;
    private bool _maintaining;

    /// <summary>Bounds every loop/lookup below defensively against the parallel Station arrays
    /// being resized inconsistently by hand in the inspector. Map positions dropped out of this
    /// bound when they moved to DestinationCatalog — one fewer array to keep in step.</summary>
    private int StationCount => Math.Min(
        Math.Min(_stationGroups.Count, _stationShipSims.Count),
        _stationDestinationAirlocks.Count);

    /// <summary>Same, for the Derelict arrays.</summary>
    private int DerelictCount => Math.Min(_derelictGroups.Count, _derelictShipSims.Count);

    public IReadOnlyList<Verb> AvailableVerbs =>
        [
            .. _docking ? new[] { ResumeDockingVerb }
                : ShipSimRef is not null && ShipSimRef.IsPowered(ShipSim.TravelConsoleFixtureId) ? new[] { TravelVerb } : [],
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

    // Docking (see _docking) deliberately reports no progress here — there's no single 0-1
    // fraction for an open-ended alignment attempt; the docking panel has its own distance/speed
    // readout instead.
    public float? CurrentVerbProgress =>
        _traveling ? 1f - (float)(_travelTimer!.TimeLeft / _travelTimer.WaitTime)
        : _maintaining ? 1f - (float)(_maintenanceTimer!.TimeLeft / _maintenanceTimer.WaitTime)
        : null;

    /// <summary>Still actively drawing power through the docking phase, not idle — covers both
    /// halves of "in flight" for the PowerDraw sync only (see _PhysicsProcess). Deliberately NOT
    /// used to gate actual battery/N2 consumption below: _docking is open-ended (waits on player
    /// skill in the minigame), so tying real drain to it would let a slow/careful attempt burn an
    /// unbounded amount of battery regardless of how short the timed trip itself was.</summary>
    private bool IsActivelyFlying => _traveling || _docking;

    public override void _Ready()
    {
        if (StationGroupPaths.Count != StationShipSimPaths.Count
            || StationGroupPaths.Count != StationDestinationAirlockPaths.Count)
        {
            GD.PushWarning("[TravelConsoleVerbTarget] Mismatched station array lengths — extra entries ignored.");
        }

        if (DerelictGroupPaths.Count != DerelictShipSimPaths.Count)
        {
            GD.PushWarning("[TravelConsoleVerbTarget] Mismatched derelict array lengths — extra entries ignored.");
        }

        // The data list and the scene wiring are separate halves of the same thing, so a mismatch
        // means either an unreachable destination (described but not wired) or an unnamed one
        // (wired but absent from the catalog, which BuildMapEntries then can't label).
        if (DestinationCatalog.All.Count > 0
            && (DestinationCatalog.StationCount != StationGroupPaths.Count || DestinationCatalog.DerelictCount != DerelictGroupPaths.Count))
        {
            GD.PushWarning(
                $"[TravelConsoleVerbTarget] destinations.json describes {DestinationCatalog.StationCount} station(s)/{DestinationCatalog.DerelictCount} derelict(s) " +
                $"but the scene wires {StationGroupPaths.Count}/{DerelictGroupPaths.Count} — only wired destinations are offered.");
        }

        foreach (var path in StationGroupPaths)
        {
            _stationGroups.Add(GetNode<Node3D>(path));
        }

        foreach (var path in StationShipSimPaths)
        {
            _stationShipSims.Add(GetNode<ShipSim>(path));
        }

        foreach (var path in StationDestinationAirlockPaths)
        {
            _stationDestinationAirlocks.Add(GetNode<AirlockDoorVerbTarget>(path));
        }

        foreach (var path in StationBuildTargetPaths)
        {
            _stationBuildTargets.Add(GetNode<ShipBuildTarget>(path));
        }

        foreach (var path in DerelictGroupPaths)
        {
            _derelictGroups.Add(GetNode<Node3D>(path));
        }

        foreach (var path in DerelictShipSimPaths)
        {
            _derelictShipSims.Add(GetNode<ShipSim>(path));
        }

        foreach (var path in DerelictBuildTargetPaths)
        {
            _derelictBuildTargets.Add(GetNode<ShipBuildTarget>(path));
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
        if (ShipSimRef is null)
        {
            return;
        }

        // Every fixture's draw is synced FIRST, in its own pass, before any drain/overload
        // decision reads it this tick — otherwise the first thruster processed in the loop below
        // would see a partially-updated (understated) total demand and briefly drain despite an
        // overload that only becomes visible once its later siblings' draw also updates.
        if (ConsoleFixture is { } consoleFixture)
        {
            consoleFixture.PowerDraw = IsActivelyFlying ? ShipSim.TravelConsoleActiveDraw : ShipSim.IdleDraw;
        }

        foreach (var thruster in ShipSimRef.Deck.Fixtures.OfType<ThrusterFixture>())
        {
            thruster.PowerDraw = IsActivelyFlying ? ShipSim.ThrusterActiveDraw : ShipSim.IdleDraw;
        }

        if (_traveling)
        {
            ShipSimRef.DrainBattery(BatteryDrainPerSecond * (float)delta);

            foreach (var thruster in ShipSimRef.Deck.Fixtures.OfType<ThrusterFixture>())
            {
                // IsPowered alone already implies charge (see PowerSystem.IsConductive) — no
                // separate Condition check needed. Re-checked live every tick: a brownout
                // triggered by this very draw spike (or anything else on the grid) simply pauses
                // N2 consumption for as long as it persists, rather than crashing or derailing
                // the trip's own already-fixed duration.
                if (ShipSimRef.IsPowered(thruster.Id))
                {
                    thruster.Condition = Math.Max(0f, thruster.Condition - ThrusterDrainPerSecond * (float)delta);
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

        if (verb.Id == ResumeDockingVerb.Id)
        {
            if (_docking && GetTree().GetFirstNodeInGroup("player") is PlayerScript dockingPlayer)
            {
                dockingPlayer.OpenDockingMinigame(this);
            }

            return;
        }

        if (verb.Id != TravelVerb.Id || _traveling || _docking)
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
    /// already occupied, an out-of-range id, while a travel is already in flight, or (see
    /// fueledCount below) with no working thruster at all — the ship physically can't fly without
    /// at least one.</summary>
    public void BeginTravel(int destinationId)
    {
        if (_traveling || _docking || destinationId == _currentDestination || destinationId < 0 || destinationId >= StationCount + DerelictCount)
        {
            return;
        }

        // Computed once at the start of the trip, not re-evaluated mid-flight — matches
        // DrainBattery's own existing behavior of having zero effect on an already-running timer
        // if the battery empties mid-trip. IsPowered alone already implies charge (see
        // PowerSystem.IsConductive's own charge-gating for ThrusterFixture), so no separate
        // Condition check is needed here.
        var fueledCount = ShipSimRef?.Deck.Fixtures.OfType<ThrusterFixture>().Count(f => ShipSimRef!.IsPowered(f.Id)) ?? 0;
        if (fueledCount == 0)
        {
            return;
        }

        _pendingDestination = destinationId;
        _traveling = true;
        _travelTimer!.WaitTime = Math.Max(MinTravelSeconds, BaseTravelSeconds - fueledCount * ReductionPerThruster);
        _travelTimer.Start();
    }

    public int CurrentDestinationId => _currentDestination;

    /// <summary>Resolves a unified destination id to that Derelict's own ShipBuildTarget, for
    /// ContractGiverVerbTarget to spawn a RetrieveItem contract's target item onto. Null for a
    /// Station id, an out-of-range id, or a scene that hasn't wired DerelictBuildTargetPaths yet
    /// — all safe no-ops for the caller (see SpawnMissionItem's own caller).</summary>
    public ShipBuildTarget? GetDerelictBuildTarget(int destinationId)
    {
        var derelictIndex = destinationId - StationCount;
        return derelictIndex >= 0 && derelictIndex < _derelictBuildTargets.Count ? _derelictBuildTargets[derelictIndex] : null;
    }

    /// <summary>Resolves a unified destination id to that Station's own ShipBuildTarget, for
    /// ContractGiverVerbTarget to spawn a CargoDelivery contract's cargo item onto — mirrors
    /// GetDerelictBuildTarget, indexed directly since Stations occupy 0..StationCount-1.</summary>
    public ShipBuildTarget? GetStationBuildTarget(int destinationId) =>
        destinationId >= 0 && destinationId < StationCount && destinationId < _stationBuildTargets.Count ? _stationBuildTargets[destinationId] : null;

    /// <summary>Every Station first, then every Derelict — for the travel map to render uniformly
    /// without special-casing individual destinations itself. Station 0 keeps the original
    /// "OBJECT_STATION" key (no "_1" suffix) so the existing, already-localized single-station
    /// save doesn't need a fresh translation just to keep reading the same on screen.</summary>
    /// <summary>The travel map's rows, straight from <see cref="DestinationCatalog"/> — name and
    /// map position are data now, rather than an inspector array paired with a label built by index
    /// (<c>$"OBJECT_DERELICT_{i + 1}"</c>). Bounded by the *wired* counts, not the catalog's own:
    /// a destination described in data but with no scene subtree behind it yet would be selectable
    /// and then travel nowhere, so it's better omitted than offered.</summary>
    public IReadOnlyList<TravelMapEntry> BuildMapEntries()
    {
        var entries = new List<TravelMapEntry>();

        for (var i = 0; i < DestinationCatalog.All.Count; i++)
        {
            var destination = DestinationCatalog.All[i];
            var wired = destination.IsStation
                ? i < StationCount
                : i - DestinationCatalog.StationCount < DerelictCount;

            if (!wired)
            {
                continue;
            }

            entries.Add(new(i, destination.NameKey, destination.MapPosition, _currentDestination == i));
        }

        return entries;
    }

    /// <summary>The travel timer elapsing no longer resolves arrival directly — the ship has
    /// "arrived in open space" near the target, and needs the docking minigame's own Dock button
    /// to succeed (see CompleteDocking) before anything about _currentDestination actually
    /// changes. _docking becomes true either way (the ship has arrived regardless of where the
    /// player is), but the panel only pops open automatically if the player happens to be looking
    /// at this console right now — the travel map closes the instant BeginTravel fires, so the
    /// player is free to wander off during the wait, and yanking a modal panel open on top of
    /// whatever else they're doing would be jarring. If it doesn't auto-open here,
    /// ResumeDockingVerb (see AvailableVerbs/ExecuteVerb) is how the player gets back into it.</summary>
    private void OnTravelComplete()
    {
        _traveling = false;
        _docking = true;

        if (GetTree().GetFirstNodeInGroup("player") is PlayerScript player && player.IsLookingAt(this))
        {
            player.OpenDockingMinigame(this);
        }
    }

    /// <summary>Called by Player once the docking minigame's Dock button succeeds — exactly what
    /// OnTravelComplete used to do unconditionally before docking existed. Also notifies Player of
    /// the real arrival (not ApplySaveState's own call to ApplyCurrentLocation, which restores
    /// existing state rather than a fresh arrival) — see Player.OnArrivedAtDestination for the
    /// contract-completion/debt-settlement side effects this triggers.</summary>
    public void CompleteDocking()
    {
        _docking = false;
        _currentDestination = _pendingDestination;
        ApplyCurrentLocation();

        if (GetTree().GetFirstNodeInGroup("player") is PlayerScript player)
        {
            player.OnArrivedAtDestination(_currentDestination, isStation: _currentDestination < StationCount);
        }
    }

    /// <summary>Emits "station_{i}" going forward — legacy bare "station" (predating multi-station
    /// support) is still accepted on load (see ApplySaveState) and always means Station 0, the
    /// original single Station. Derelict strings are unaffected: "derelict_{n}" already encodes
    /// semantic identity, not the raw destination int, so it stays correct even though that int
    /// now shifts by StationCount-1 once more than one Station exists.</summary>
    public string GetSaveState() => _currentDestination < StationCount
        ? $"station_{_currentDestination}"
        : $"derelict_{_currentDestination - StationCount + 1}";

    public void ApplySaveState(string state)
    {
        _currentDestination = state switch
        {
            "station" => 0, // legacy pre-multi-station save (bare "station") -> Station 0 specifically.
            "derelict" => StationCount, // legacy pre-map save (bare "derelict") -> Derelict 1
                                        // specifically, preserving "I was away from the station"
                                        // intent rather than a generic reset back to a Station.
            _ when state.StartsWith("station_") && int.TryParse(state.AsSpan("station_".Length), out var s)
                && s >= 0 && s < StationCount => s,
            _ when state.StartsWith("derelict_") && int.TryParse(state.AsSpan("derelict_".Length), out var n)
                && n >= 1 && n <= DerelictCount => StationCount + n - 1,
            _ => 0, // unrecognized -> Station 0, matching this codebase's existing fallback shape.
        };
        ApplyCurrentLocation();
    }

    private void ApplyCurrentLocation()
    {
        // -1 (not a valid station index) whenever the current destination is actually a Derelict.
        var stationIndex = _currentDestination < StationCount ? _currentDestination : -1;
        var derelictIndex = _currentDestination - StationCount;

        for (var i = 0; i < _stationDestinationAirlocks.Count; i++)
        {
            _stationDestinationAirlocks[i].Docked = i == stationIndex;
        }

        if (StationAirlock is not null)
        {
            if (stationIndex >= 0 && stationIndex < _stationShipSims.Count && stationIndex < _stationDestinationAirlocks.Count)
            {
                StationAirlock.RebindFarSide(_stationShipSims[stationIndex], _stationDestinationAirlocks[stationIndex]);
            }

            StationAirlock.Docked = stationIndex >= 0;
        }

        if (DerelictAirlock is not null)
        {
            if (stationIndex < 0 && derelictIndex >= 0 && derelictIndex < _derelictShipSims.Count)
            {
                DerelictAirlock.RebindFarSide(_derelictShipSims[derelictIndex]);
            }

            DerelictAirlock.Docked = stationIndex < 0;
        }

        for (var i = 0; i < _stationGroups.Count; i++)
        {
            SetShipPresence(_stationGroups[i], i == stationIndex);
        }

        for (var i = 0; i < _derelictGroups.Count; i++)
        {
            SetShipPresence(_derelictGroups[i], stationIndex < 0 && i == derelictIndex);
        }
    }

    /// <summary>Toggles all four halves of "is this ship actually here": Node3D.Visible for
    /// rendering, every descendant CollisionShape3D's own Disabled flag for physics — Visible
    /// alone doesn't stop the player from walking/floating into a hidden ship's geometry — every
    /// descendant IPhysicsPresenceAware (loose PickupItem/ContainerPickupItem pickups), since a
    /// live RigidBody3D has nothing to rest on once this group's own collision is disabled and
    /// would otherwise fall through the now-decollided floor forever, and every descendant
    /// ShipSim's own simulation level of detail.
    ///
    /// <para>That last one is deliberately NOT "stop simulating": an absent ship drops to a coarse
    /// tick (see ShipSim.IsPresent / ShipSystems.TickCoarse) rather than freezing, so a derelict
    /// you left venting is still vented when you return and one you left intact has still worn a
    /// little. Freezing it would make time pass only where the player is looking, which is exactly
    /// the "presentation skip is not a cost skip" rule docs/project-plan.md §5 rejects.</para></summary>
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

            if (node is ShipSim shipSim)
            {
                shipSim.IsPresent = present;
            }
        }
    }
}

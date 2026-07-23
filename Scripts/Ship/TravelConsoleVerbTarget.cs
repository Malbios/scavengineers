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

/// <summary>The Home Ship's simplified/abstracted travel step — not full Newtonian piloting, just
/// a timed wait standing in for undocking, flying, and docking. Owns which of the Home Ship's
/// possible destinations (Stations plus Derelicts) it currently occupies, and keeps every
/// AirlockDoorVerbTarget's <see cref="AirlockDoorVerbTarget.Docked"/> flag in sync. Doesn't touch
/// any airlock's own open/closed state — whatever a door was doing when you left is still what
/// it's doing when you're back.
///
/// A destination is a single unified int: 0..StationCount-1 = Station N, StationCount..
/// StationCount+DerelictCount-1 = Derelict N. Stations and Derelicts share one rebind pattern: one
/// shared Home-Ship-side airlock whose far side gets repointed at the current target
/// (AirlockDoorVerbTarget.RebindFarSide); a Station additionally has its own destination-side
/// door, only reachable when that Station is current (see SetShipPresence). The two doors of a
/// connection only bridge atmosphere when both report open (see AirlockDoorVerbTarget.PartnerDoorRef).
/// Executing the Travel verb opens the travel map (see Player.OpenTravelMap), which calls back
/// into <see cref="BeginTravel"/> once a destination is picked and confirmed.
/// </summary>
public partial class TravelConsoleVerbTarget : StaticBody3D, IVerbTarget, IStateSaveable
{
    // TravelVerb.DurationSeconds only seeds _travelTimer's default before the first real
    // BeginTravel call recomputes WaitTime from the formula below.
    private static readonly Verb TravelVerb = new("travel", "VERB_TRAVEL", DurationSeconds: 0.6f);

    /// <summary>Offered instead of TravelVerb whenever _docking is true — how the player gets
    /// back into a docking attempt that didn't auto-open (see OnTravelComplete).</summary>
    private static readonly Verb ResumeDockingVerb = new("resume_docking", "VERB_RESUME_DOCKING", DurationSeconds: 0.6f);

    // Placeholder/tunable — dialed down for the current testing/iteration phase; still long
    // enough to see the verb progress bar sweep. Public settable so tests can dial it down further.
    public float BaseTravelSeconds { get; set; } = 3f;
    public float ReductionPerThruster { get; set; } = 0.3f;
    public float MinTravelSeconds { get; set; } = 1f;

    // Placeholder/tunable, matching SuitResources's drain-constant convention. Only applied
    // during the bounded _traveling phase — the open-ended _docking phase that follows costs
    // nothing per-second, or a slow/careful docking attempt could drain the whole battery.
    private const float BatteryDrainPerSecond = 0.01f;

    // Placeholder/tunable — independent per thruster, so N fueled thrusters drain N times as
    // fast in total (no shared pool). Same "_traveling only" bound as BatteryDrainPerSecond.
    private const float ThrusterDrainPerSecond = 0.02f;

    // Same two-tier upkeep as everything else with a Deck-tracked Condition (see MaintenanceTier).
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
    /// Derelict is the current travel target, rather than each Derelict getting its own
    /// always-wired door.</summary>
    [Export]
    public AirlockDoorVerbTarget? DerelictAirlock { get; set; }

    /// <summary>The one shared Home-Ship-side Station airlock — its far side (and partner door)
    /// gets repointed at whichever Station is current, mirroring DerelictAirlock.</summary>
    [Export]
    public AirlockDoorVerbTarget? StationAirlock { get; set; }

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
    // Populated by DestinationManager.Register* at startup, in catalog order (every Station, then
    // every Derelict). Lists are only ever appended to as a set, so index i describes destination
    // i throughout, and a destination can no longer be half-wired.
    private readonly List<Node3D> _stationGroups = new();
    private readonly List<ShipSim> _stationShipSims = new();
    private readonly List<AirlockDoorVerbTarget> _stationDestinationAirlocks = new();
    private readonly List<ShipBuildTarget?> _stationBuildTargets = new();
    private readonly List<Node3D> _derelictGroups = new();
    private readonly List<ShipSim> _derelictShipSims = new();
    private readonly List<ShipBuildTarget?> _derelictBuildTargets = new();

    /// <summary>Called once per Station by DestinationManager, before this node's own deferred
    /// ApplyCurrentLocation runs. A null buildTarget only costs CargoDelivery contracts a physical
    /// spawn point, not the ability to travel there.</summary>
    public void RegisterStation(Node3D group, ShipSim shipSim, AirlockDoorVerbTarget destinationAirlock, ShipBuildTarget? buildTarget)
    {
        _stationGroups.Add(group);
        _stationShipSims.Add(shipSim);
        _stationDestinationAirlocks.Add(destinationAirlock);
        _stationBuildTargets.Add(buildTarget);
    }

    /// <summary>Called once per Derelict by DestinationManager. Derelicts have no destination-side
    /// door of their own — the one shared DerelictAirlock is repointed at whichever is current.</summary>
    public void RegisterDerelict(Node3D group, ShipSim shipSim, ShipBuildTarget? buildTarget)
    {
        _derelictGroups.Add(group);
        _derelictShipSims.Add(shipSim);
        _derelictBuildTargets.Add(buildTarget);
    }

    // A second, independent timer/bool pair — travel and upkeep are unrelated timed actions.
    private Timer? _maintenanceTimer;
    private bool _maintaining;

    private int StationCount => _stationGroups.Count;

    private int DerelictCount => _derelictGroups.Count;

    public IReadOnlyList<Verb> AvailableVerbs =>
        [
            .. _docking ? new[] { ResumeDockingVerb }
                : ShipSimRef is not null && ShipSimRef.IsPowered(ShipSim.TravelConsoleFixtureId) ? new[] { TravelVerb } : [],
            // Offered regardless of current power state — a console you can't currently use to
            // travel should still be repairable.
            .. ConsoleFixture is { } fixture && MaintenanceTier.PickVerb(fixture.Condition, MaintainTravelConsoleVerb, RepairTravelConsoleVerb) is { } upkeepVerb ? new[] { upkeepVerb } : [],
        ];

    public string? DisplayNameKey => "OBJECT_SHIP_CONSOLE";

    private Fixture? ConsoleFixture => ShipSimRef?.Deck.Fixtures.FirstOrDefault(f => f.Id == ShipSim.TravelConsoleFixtureId);

    public float? Condition => ConsoleFixture?.Condition;

    // Docking (see _docking) deliberately reports no progress here — there's no single 0-1
    // fraction for an open-ended alignment attempt; the docking panel has its own distance/speed
    // readout instead.
    public float? CurrentVerbProgress =>
        _traveling ? 1f - (float)(_travelTimer!.TimeLeft / _travelTimer.WaitTime)
        : _maintaining ? 1f - (float)(_maintenanceTimer!.TimeLeft / _maintenanceTimer.WaitTime)
        : null;

    /// <summary>Covers both halves of "in flight" for the PowerDraw sync only. Deliberately NOT
    /// used to gate actual battery/N2 consumption below: _docking is open-ended (waits on player
    /// skill in the minigame), so tying real drain to it would let a slow attempt burn an
    /// unbounded amount of battery.</summary>
    private bool IsActivelyFlying => _traveling || _docking;

    public override void _Ready()
    {
        _travelTimer = new Timer { OneShot = true, WaitTime = TravelVerb.DurationSeconds };
        AddChild(_travelTimer);
        _travelTimer.Timeout += OnTravelComplete;

        _maintenanceTimer = new Timer { OneShot = true, WaitTime = MaintainTravelConsoleVerb.DurationSeconds };
        AddChild(_maintenanceTimer);
        _maintenanceTimer.Timeout += OnMaintenanceComplete;

        // Deferred TWICE, not once: every destination's own Floor.SeedDefaultShipLayout (which
        // spawns the wall/conduit/machine CollisionShape3D children) is ALSO deferred, from that
        // Floor's own _Ready(). This node readies before DestinationManager has instantiated
        // anything, so a single CallDeferred here would run before those walls exist, leaving
        // them permanently un-decollided. Deferring a second time lands this call after every
        // SeedDefaultShipLayout queued in between, guaranteeing the walls exist first.
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
                // Re-checked live every tick: a brownout simply pauses N2 consumption for as long
                // as it persists, rather than crashing or derailing the trip's fixed duration.
                if (ShipSimRef.IsPowered(thruster.Id))
                {
                    thruster.Condition = Math.Max(0f, thruster.Condition - ThrusterDrainPerSecond * (float)delta);
                }
            }
        }
    }

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

    /// <summary>Called back from the travel map once a destination is picked and confirmed. A
    /// no-op for the destination already occupied, an out-of-range id, a travel already in
    /// flight, or no working thruster at all.</summary>
    public void BeginTravel(int destinationId)
    {
        if (_traveling || _docking || destinationId == _currentDestination || destinationId < 0 || destinationId >= StationCount + DerelictCount)
        {
            return;
        }

        // Computed once at the start of the trip, not re-evaluated mid-flight.
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

    /// <summary>Resolves a unified destination id to that Derelict's ShipBuildTarget, for a
    /// RetrieveItem contract's target item. Null for a Station id or an out-of-range id.</summary>
    public ShipBuildTarget? GetDerelictBuildTarget(int destinationId)
    {
        var derelictIndex = destinationId - StationCount;
        return derelictIndex >= 0 && derelictIndex < _derelictBuildTargets.Count ? _derelictBuildTargets[derelictIndex] : null;
    }

    /// <summary>Mirrors <see cref="GetDerelictBuildTarget"/>, for a CargoDelivery contract's cargo
    /// item, indexed directly since Stations occupy 0..StationCount-1.</summary>
    public ShipBuildTarget? GetStationBuildTarget(int destinationId) =>
        destinationId >= 0 && destinationId < StationCount && destinationId < _stationBuildTargets.Count ? _stationBuildTargets[destinationId] : null;

    /// <summary>The travel map's rows, straight from <see cref="DestinationCatalog"/> — every
    /// Station first, then every Derelict. Bounded by the *wired* counts, not the catalog's own: a
    /// destination described in data but with no scene subtree behind it yet would be selectable
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

    /// <summary>The ship has "arrived in open space" near the target, and needs the docking
    /// minigame's Dock button to succeed (see CompleteDocking) before _currentDestination
    /// actually changes. The panel only pops open automatically if the player is looking at this
    /// console right now — they're free to wander off during the wait, so yanking a modal panel
    /// open on top of whatever else they're doing would be jarring. If it doesn't auto-open,
    /// ResumeDockingVerb is how the player gets back into it.</summary>
    private void OnTravelComplete()
    {
        _traveling = false;
        _docking = true;

        if (GetTree().GetFirstNodeInGroup("player") is PlayerScript player && player.IsLookingAt(this))
        {
            player.OpenDockingMinigame(this);
        }
    }

    /// <summary>Called by Player once the docking minigame's Dock button succeeds. Also notifies
    /// Player of the real arrival (not ApplySaveState's own call to ApplyCurrentLocation, which
    /// restores existing state rather than a fresh arrival) — see Player.OnArrivedAtDestination
    /// for the contract-completion/debt-settlement side effects this triggers.</summary>
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

    /// <summary>Emits "station_{i}" going forward — legacy bare "station" is still accepted on
    /// load (see ApplySaveState) and always means Station 0.</summary>
    public string GetSaveState() => _currentDestination < StationCount
        ? $"station_{_currentDestination}"
        : $"derelict_{_currentDestination - StationCount + 1}";

    public void ApplySaveState(string state)
    {
        _currentDestination = state switch
        {
            "station" => 0, // legacy pre-multi-station save -> Station 0.
            "derelict" => StationCount, // legacy pre-map save -> Derelict 1, preserving "I was
                                        // away from the station" rather than resetting to a Station.
            _ when state.StartsWith("station_") && int.TryParse(state.AsSpan("station_".Length), out var s)
                && s >= 0 && s < StationCount => s,
            _ when state.StartsWith("derelict_") && int.TryParse(state.AsSpan("derelict_".Length), out var n)
                && n >= 1 && n <= DerelictCount => StationCount + n - 1,
            _ => 0, // unrecognized -> Station 0.
        };
        ApplyCurrentLocation();
    }

    private void ApplyCurrentLocation()
    {
        // -1 whenever the current destination is actually a Derelict.
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

    /// <summary>Toggles three things: Node3D.Visible for rendering; every descendant
    /// CollisionShape3D's Disabled flag, since Visible alone doesn't stop the player walking into
    /// a hidden ship's geometry; and every descendant IPhysicsPresenceAware pickup, since a live
    /// RigidBody3D would otherwise fall through the now-decollided floor forever.
    ///
    /// Also sets every descendant ShipSim's IsPresent — deliberately NOT "stop simulating": an
    /// absent ship drops to a coarse tick (see ShipSystems.TickCoarse) rather than freezing, so a
    /// derelict left venting is still vented on return.</summary>
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

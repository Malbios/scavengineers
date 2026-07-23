using Scavengineers.Sim.Connectivity;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Sim.Power;

/// <summary>
/// A pure reader of a <see cref="Deck"/> (the shared structural tier — see
/// docs/architecture/ship-model.md): graph nodes are the deck's conductive fixtures
/// (<see cref="ConduitFixture"/>/<see cref="SwitchFixture"/>/<see cref="MachineFixture"/>/
/// <see cref="BatteryFixture"/>/<see cref="ThrusterFixture"/>),
/// connected by adjacent-cell connectivity (the option the ship-model doc recommends for
/// MVP over explicit port-to-port routing) — a switch on the connecting tile-pair that
/// is open conducts nowhere. Reuses <see cref="ConnectivitySolver"/> — the same solver
/// <see cref="Scavengineers.Sim.Atmosphere.AtmosphereSystem"/> reuses for its own network.
///
/// <para><b>Cost.</b> <see cref="PoweredNodes"/> is called every physics frame by several nodes at
/// once (powered-device indicators, lights, doors, airlocks) and once per conduit by
/// <c>FireSystem.Tick</c>, so it can't afford to be the naive version it used to be: a
/// <see cref="Neighbors"/> that scanned every fixture made <see cref="ConnectivitySolver.FindComponents"/>
/// O(F²), and re-running it per query made <c>ShipSim.DemandedPower</c> O(F³) — over a fixture count
/// the player can grow without bound by placing conduits. Two changes fix that: a tile index makes
/// <see cref="Neighbors"/> O(1), and the component result is cached (see <see cref="CacheIsValid"/>).
/// Net O(F) per query.</para>
/// </summary>
public sealed class PowerSystem : IConnectivityGraph<PowerNodeId>
{
    private readonly Deck _deck;
    private readonly HashSet<PowerNodeId> _sources = [];

    /// <summary>Conductive fixtures bucketed by tile — rebuilt only alongside the cache below (see
    /// <see cref="RebuildTileIndex"/>), so <see cref="Neighbors"/> can look at the ≤5 relevant tiles
    /// instead of every fixture on the deck. Null means "not built yet / invalidated."</summary>
    private Dictionary<CellCoord, List<Fixture>>? _tileIndex;

    /// <summary>Conductive fixtures by id, built in the same pass as <see cref="_tileIndex"/>.
    /// <see cref="Neighbors"/> runs once per node, so resolving its own node id by scanning
    /// <see cref="Deck.Fixtures"/> would put back the O(F²) the tile index just removed.
    ///
    /// <para>Holds only conductive fixtures, so <see cref="Neighbors"/> of a non-conductive id is
    /// empty rather than that fixture's neighbor list. That's unreachable from the solver — <see
    /// cref="Nodes"/> only ever offers conductive fixtures, and everything reached from there came
    /// out of this same index — and it's the more defensible answer anyway: something that doesn't
    /// conduct shouldn't relay power between its neighbors.</para></summary>
    private Dictionary<string, Fixture>? _idIndex;

    private HashSet<PowerNodeId>? _cachedPowered;

    /// <summary>The exact fixture state <see cref="_cachedPowered"/> was computed from, in
    /// <see cref="Deck.Fixtures"/> order. Compared field-by-field rather than hashed: an O(F) walk
    /// either way, but an exact comparison has no collision case to reason about, and a stale power
    /// reading would be a genuinely confusing bug to chase.</summary>
    private readonly List<FixtureState> _cachedState = [];

    public PowerSystem(Deck deck) => _deck = deck;

    /// <summary>Everything about a fixture that can change what <see cref="PoweredNodes"/> returns:
    /// which fixtures exist, where they sit, whether they conduct at all, and whether they're an
    /// open switch. Deliberately not <see cref="Fixture.Condition"/> in general — wear on a conduit
    /// or machine doesn't affect conductivity, and including it would invalidate the cache every
    /// single frame (WearSystem decays continuously), defeating the point. Thruster charge *is*
    /// covered, via <see cref="IsConductive"/>.</summary>
    private readonly record struct FixtureState(string Id, CellCoord Tile, bool Conductive, bool OpenSwitch);

    public IEnumerable<PowerNodeId> Nodes =>
        _deck.Fixtures.Where(IsConductive).Select(f => new PowerNodeId(f.Id));

    public IEnumerable<PowerNodeId> Neighbors(PowerNodeId node)
    {
        EnsureTileIndex();

        var fixture = FindFixture(node);
        if (fixture is null || IsOpenSwitch(fixture))
        {
            yield break;
        }

        // Own tile first (two fixtures on one tile touch), then the four orthogonal ones — the
        // exact same adjacency rule the old all-fixtures scan applied via AreConnected, just
        // reached by lookup instead of by filtering everything.
        foreach (var tile in Adjacency(fixture.Tile))
        {
            if (!_tileIndex!.TryGetValue(tile, out var candidates))
            {
                continue;
            }

            foreach (var other in candidates)
            {
                if (other.Id == fixture.Id || IsOpenSwitch(other))
                {
                    continue;
                }

                yield return new PowerNodeId(other.Id);
            }
        }
    }

    private static IEnumerable<CellCoord> Adjacency(CellCoord tile) =>
        tile.OrthogonalNeighbors().Prepend(tile);

    public void MarkSource(PowerNodeId source)
    {
        _sources.Add(source);
        Invalidate();
    }

    /// <summary>Every node in a component that contains at least one source. Cached — see this
    /// class's own doc comment for why that matters, and <see cref="CacheIsValid"/> for what makes
    /// the cache safe without any caller having to remember to invalidate it.</summary>
    public IReadOnlySet<PowerNodeId> PoweredNodes()
    {
        if (CacheIsValid())
        {
            return _cachedPowered!;
        }

        RebuildTileIndex();

        var powered = new HashSet<PowerNodeId>();
        foreach (var component in ConnectivitySolver.FindComponents(this))
        {
            if (component.Overlaps(_sources))
            {
                powered.UnionWith(component);
            }
        }

        CaptureState();
        _cachedPowered = powered;
        return powered;
    }

    public bool IsPowered(PowerNodeId node) => PoweredNodes().Contains(node);

    /// <summary>
    /// Whether <see cref="_cachedPowered"/> still describes the deck as it is right now. Deliberately
    /// a self-checked comparison rather than an <c>Invalidate()</c> that every mutator must remember
    /// to call: fixture state is mutated from outside this class in places that have no reference to
    /// it at all (most notably <c>TravelConsoleVerbTarget</c>'s in-flight thruster drain, which writes
    /// <see cref="Fixture.Condition"/> straight onto <see cref="Deck.Fixtures"/>), so a
    /// forget-to-invalidate bug would be both easy to introduce and hard to spot — it would surface
    /// as a device that stays lit one frame too long. This cannot go stale by construction.
    /// </summary>
    private bool CacheIsValid()
    {
        if (_cachedPowered is null)
        {
            return false;
        }

        var fixtures = _deck.Fixtures;
        if (_cachedState.Count != fixtures.Count)
        {
            return false;
        }

        for (var i = 0; i < fixtures.Count; i++)
        {
            var fixture = fixtures[i];
            var cached = _cachedState[i];

            if (cached.Id != fixture.Id
                || cached.Tile != fixture.Tile
                || cached.Conductive != IsConductive(fixture)
                || cached.OpenSwitch != IsOpenSwitch(fixture))
            {
                return false;
            }
        }

        return true;
    }

    private void CaptureState()
    {
        _cachedState.Clear();
        foreach (var fixture in _deck.Fixtures)
        {
            _cachedState.Add(new FixtureState(fixture.Id, fixture.Tile, IsConductive(fixture), IsOpenSwitch(fixture)));
        }
    }

    private void Invalidate()
    {
        _cachedPowered = null;
        _tileIndex = null;
        _idIndex = null;
    }

    /// <summary>Builds the indexes if some caller reached <see cref="Neighbors"/> without going
    /// through <see cref="PoweredNodes"/> first. In practice that never happens — the solver only
    /// ever walks this graph from inside PoweredNodes, which rebuilds them itself — but
    /// <see cref="Neighbors"/> is a public interface member, so it can't assume it.</summary>
    private void EnsureTileIndex()
    {
        if (_tileIndex is null)
        {
            RebuildTileIndex();
        }
    }

    private void RebuildTileIndex()
    {
        _tileIndex = new Dictionary<CellCoord, List<Fixture>>();
        _idIndex = new Dictionary<string, Fixture>();

        foreach (var fixture in _deck.Fixtures)
        {
            if (!IsConductive(fixture))
            {
                continue;
            }

            if (!_tileIndex.TryGetValue(fixture.Tile, out var bucket))
            {
                bucket = [];
                _tileIndex[fixture.Tile] = bucket;
            }

            bucket.Add(fixture);
            _idIndex[fixture.Id] = fixture;
        }
    }

    private Fixture? FindFixture(PowerNodeId node) =>
        _idIndex!.GetValueOrDefault(node.Value);

    // A Thruster's own conductivity is charge-gated, unlike every other type here — an empty
    // thruster isn't being used, so it neither draws power itself nor relays it to anything
    // wired only through it (see ShipSim.IsPowered's own callers in TravelConsoleVerbTarget).
    private static bool IsConductive(Fixture fixture) =>
        fixture is ConduitFixture or SwitchFixture or MachineFixture or BatteryFixture ||
        fixture is ThrusterFixture { Condition: > 0f };

    private static bool IsOpenSwitch(Fixture fixture) =>
        fixture is SwitchFixture { IsOpen: true };
}

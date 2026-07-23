using Scavengineers.Sim.Connectivity;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Sim.Power;

/// <summary>A pure reader of a <see cref="Deck"/>: graph nodes are the deck's conductive fixtures,
/// connected by adjacent-cell connectivity — a switch on the connecting tile-pair that is open
/// conducts nowhere. Reuses <see cref="ConnectivitySolver"/>, the same solver
/// <see cref="Scavengineers.Sim.Atmosphere.AtmosphereSystem"/> reuses.
///
/// <b>Cost.</b> <see cref="PoweredNodes"/> is called every physics frame by several nodes at once,
/// so a naive <see cref="Neighbors"/> scanning every fixture made
/// <see cref="ConnectivitySolver.FindComponents"/> O(F²), and re-running it per query made
/// <c>ShipSim.DemandedPower</c> O(F³). A tile index makes <see cref="Neighbors"/> O(1), and the
/// component result is cached (see <see cref="CacheIsValid"/>) — net O(F) per query.</summary>
public sealed class PowerSystem : IConnectivityGraph<PowerNodeId>
{
    private readonly Deck _deck;
    private readonly HashSet<PowerNodeId> _sources = [];

    /// <summary>Conductive fixtures bucketed by tile — rebuilt only alongside the cache below, so
    /// <see cref="Neighbors"/> can look at the ≤5 relevant tiles instead of every fixture on the
    /// deck. Null means "not built yet / invalidated."</summary>
    private Dictionary<CellCoord, List<Fixture>>? _tileIndex;

    /// <summary>Conductive fixtures by id, built in the same pass as <see cref="_tileIndex"/> —
    /// holds only conductive fixtures, so <see cref="Neighbors"/> of a non-conductive id is empty
    /// rather than that fixture's neighbor list.</summary>
    private Dictionary<string, Fixture>? _idIndex;

    private HashSet<PowerNodeId>? _cachedPowered;

    /// <summary>The exact fixture state <see cref="_cachedPowered"/> was computed from, in
    /// <see cref="Deck.Fixtures"/> order. Compared field-by-field rather than hashed — an O(F)
    /// walk either way, but an exact comparison has no collision case to reason about.</summary>
    private readonly List<FixtureState> _cachedState = [];

    public PowerSystem(Deck deck) => _deck = deck;

    /// <summary>Everything about a fixture that can change what <see cref="PoweredNodes"/>
    /// returns. Deliberately not <see cref="Fixture.Condition"/> in general — wear on a conduit
    /// or machine doesn't affect conductivity, and including it would invalidate the cache every
    /// frame. Thruster charge *is* covered, via <see cref="IsConductive"/>.</summary>
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

    /// <summary>Every node in a component that contains at least one source. Cached — see
    /// <see cref="CacheIsValid"/> for what makes the cache safe without any caller having to
    /// remember to invalidate it.</summary>
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

    /// <summary>Whether <see cref="_cachedPowered"/> still describes the deck as it is right now.
    /// Deliberately a self-checked comparison rather than an <c>Invalidate()</c> that every
    /// mutator must remember to call: fixture state is mutated from outside this class in places
    /// with no reference to it at all (e.g. <c>TravelConsoleVerbTarget</c>'s in-flight thruster
    /// drain), so a forget-to-invalidate bug would be easy to introduce and hard to spot. This
    /// cannot go stale by construction.</summary>
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
    /// through <see cref="PoweredNodes"/> first — <see cref="Neighbors"/> is a public interface
    /// member, so it can't assume the solver always calls it first.</summary>
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

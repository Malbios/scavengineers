using System.Linq;

using Scavengineers.Sim.Grid;

namespace Scavengineers.Sim.ShipModel;

/// <summary>Which structural surface of a cell a breach came from — a cell can be open from
/// more than one independent cause at once (e.g. floor AND ceiling both missing), so this is
/// tracked per-reason rather than as a single flag; repairing one must not clear the others.</summary>
public enum StructuralSurface
{
    Floor,
    Ceiling,
    Wall,
}

/// <summary>
/// The shared structural tier (Tier 1 + Tier 2 per docs/architecture/ship-model.md) that
/// atmosphere, power, and (later) rooms/data all read — one canonical source instead of
/// each subsystem owning its own private copy of the ship's layout.
/// </summary>
public sealed class Deck
{
    private readonly HashSet<CellCoord> _cells = [];
    private readonly HashSet<(CellCoord, CellCoord)> _sealedEdges = [];
    private readonly HashSet<(CellCoord Cell, StructuralSurface Surface)> _breaches = [];
    private readonly HashSet<(CellCoord, CellCoord)> _wallEdgeBreaches = [];
    private readonly HashSet<CellCoord> _fires = [];
    private readonly List<Fixture> _fixtures = [];

    public IReadOnlySet<CellCoord> Cells => _cells;

    public IReadOnlyList<Fixture> Fixtures => _fixtures;

    public IReadOnlySet<CellCoord> HullBreaches =>
        _breaches.Select(b => b.Cell)
            .Concat(_wallEdgeBreaches.SelectMany(e => new[] { e.Item1, e.Item2 }))
            .ToHashSet();

    /// <summary>The raw breached edge pairs — <see cref="HullBreaches"/> flattens these to just
    /// the cells they touch, losing which specific edge is open; a consumer that needs the actual
    /// edge (e.g. to compute a wall's own world position for a hazard) needs this instead.</summary>
    public IReadOnlySet<(CellCoord, CellCoord)> WallEdgeBreaches => _wallEdgeBreaches;

    public IReadOnlySet<CellCoord> Fires => _fires;

    public void AddCell(CellCoord cell) => _cells.Add(cell);

    /// <summary>Also purges every other set's entries for this coordinate (breaches, wall-edge
    /// breaches, sealed edges, fixtures) — leaving them behind would let a removed cell's stale
    /// data resurface as a phantom breach/fixture for any future consumer that doesn't separately
    /// filter through <see cref="Cells"/> first, and would grow these sets unbounded across
    /// repeated dynamic-expansion extend/remove cycles.</summary>
    public void RemoveCell(CellCoord cell)
    {
        _cells.Remove(cell);
        _breaches.RemoveWhere(b => b.Cell == cell);
        _wallEdgeBreaches.RemoveWhere(e => e.Item1 == cell || e.Item2 == cell);
        _sealedEdges.RemoveWhere(e => e.Item1 == cell || e.Item2 == cell);
        _fixtures.RemoveAll(f => f.Tile == cell);
    }

    public void SealEdge(CellCoord a, CellCoord b) => _sealedEdges.Add(Normalize(a, b));

    public void UnsealEdge(CellCoord a, CellCoord b) => _sealedEdges.Remove(Normalize(a, b));

    public bool IsEdgeSealed(CellCoord a, CellCoord b) => _sealedEdges.Contains(Normalize(a, b));

    /// <summary>Defaults to <see cref="StructuralSurface.Wall"/> so every pre-existing caller
    /// (hull breaches, airlock venting) keeps compiling unchanged — only floor/ceiling callers
    /// need to name their reason explicitly. For Wall specifically, this is a per-*cell* flag —
    /// correct for a direct "this whole room is exposed to vacuum" event (an airlock venting),
    /// but not for a specific wall segment (see <see cref="BreachWallEdge"/> for that; a cell can
    /// have several independently open wall directions at once, which a single per-cell flag
    /// can't distinguish).</summary>
    public void BreachHull(CellCoord cell, StructuralSurface surface = StructuralSurface.Wall) =>
        _breaches.Add((cell, surface));

    public void RepairHull(CellCoord cell, StructuralSurface surface = StructuralSurface.Wall) =>
        _breaches.Remove((cell, surface));

    public bool IsHullBreached(CellCoord cell) =>
        _breaches.Any(b => b.Cell == cell) || _wallEdgeBreaches.Any(e => e.Item1 == cell || e.Item2 == cell);

    public bool IsHullBreached(CellCoord cell, StructuralSurface surface) => _breaches.Contains((cell, surface));

    /// <summary>A specific hull-boundary wall segment (an edge to a cell that doesn't exist),
    /// tracked per edge rather than per cell — the piece <see cref="BreachHull"/>/<see cref="RepairHull"/>
    /// can't represent, since a cell can have more than one independently open wall direction at
    /// once (most visibly a freshly extended floor tile, open on every side but the one it was
    /// extended from).</summary>
    public void BreachWallEdge(CellCoord a, CellCoord b) => _wallEdgeBreaches.Add(Normalize(a, b));

    public void RepairWallEdge(CellCoord a, CellCoord b) => _wallEdgeBreaches.Remove(Normalize(a, b));

    public bool IsWallEdgeBreached(CellCoord a, CellCoord b) => _wallEdgeBreaches.Contains(Normalize(a, b));

    public void IgniteFire(CellCoord cell) => _fires.Add(cell);

    public void ExtinguishFire(CellCoord cell) => _fires.Remove(cell);

    public bool IsOnFire(CellCoord cell) => _fires.Contains(cell);

    public void AddFixture(Fixture fixture) => _fixtures.Add(fixture);

    public void RemoveFixture(string id) => _fixtures.RemoveAll(f => f.Id == id);

    public static (CellCoord, CellCoord) Normalize(CellCoord a, CellCoord b)
    {
        if (a.X != b.X)
        {
            return a.X < b.X ? (a, b) : (b, a);
        }

        return a.Y <= b.Y ? (a, b) : (b, a);
    }
}

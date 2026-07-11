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
    private readonly HashSet<CellCoord> _fires = [];
    private readonly List<Fixture> _fixtures = [];

    public IReadOnlySet<CellCoord> Cells => _cells;

    public IReadOnlyList<Fixture> Fixtures => _fixtures;

    public IReadOnlySet<CellCoord> HullBreaches => _breaches.Select(b => b.Cell).ToHashSet();

    public IReadOnlySet<CellCoord> Fires => _fires;

    public void AddCell(CellCoord cell) => _cells.Add(cell);

    public void SealEdge(CellCoord a, CellCoord b) => _sealedEdges.Add(Normalize(a, b));

    public void UnsealEdge(CellCoord a, CellCoord b) => _sealedEdges.Remove(Normalize(a, b));

    public bool IsEdgeSealed(CellCoord a, CellCoord b) => _sealedEdges.Contains(Normalize(a, b));

    /// <summary>Defaults to <see cref="StructuralSurface.Wall"/> so every pre-existing caller
    /// (hull breaches, airlock venting) keeps compiling unchanged — only floor/ceiling callers
    /// need to name their reason explicitly.</summary>
    public void BreachHull(CellCoord cell, StructuralSurface surface = StructuralSurface.Wall) =>
        _breaches.Add((cell, surface));

    public void RepairHull(CellCoord cell, StructuralSurface surface = StructuralSurface.Wall) =>
        _breaches.Remove((cell, surface));

    public bool IsHullBreached(CellCoord cell) => _breaches.Any(b => b.Cell == cell);

    public bool IsHullBreached(CellCoord cell, StructuralSurface surface) => _breaches.Contains((cell, surface));

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

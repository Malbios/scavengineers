using Scavengineers.Sim.Grid;

namespace Scavengineers.Sim.ShipModel;

/// <summary>
/// The shared structural tier (Tier 1 + Tier 2 per docs/architecture/ship-model.md) that
/// atmosphere, power, and (later) rooms/data all read — one canonical source instead of
/// each subsystem owning its own private copy of the ship's layout.
/// </summary>
public sealed class Deck
{
    private readonly HashSet<CellCoord> _cells = [];
    private readonly HashSet<(CellCoord, CellCoord)> _sealedEdges = [];
    private readonly HashSet<CellCoord> _hullBreaches = [];
    private readonly HashSet<CellCoord> _fires = [];
    private readonly List<Fixture> _fixtures = [];

    public IReadOnlySet<CellCoord> Cells => _cells;

    public IReadOnlyList<Fixture> Fixtures => _fixtures;

    public IReadOnlySet<CellCoord> HullBreaches => _hullBreaches;

    public IReadOnlySet<CellCoord> Fires => _fires;

    public void AddCell(CellCoord cell) => _cells.Add(cell);

    public void SealEdge(CellCoord a, CellCoord b) => _sealedEdges.Add(Normalize(a, b));

    public void UnsealEdge(CellCoord a, CellCoord b) => _sealedEdges.Remove(Normalize(a, b));

    public bool IsEdgeSealed(CellCoord a, CellCoord b) => _sealedEdges.Contains(Normalize(a, b));

    public void BreachHull(CellCoord cell) => _hullBreaches.Add(cell);

    public void RepairHull(CellCoord cell) => _hullBreaches.Remove(cell);

    public bool IsHullBreached(CellCoord cell) => _hullBreaches.Contains(cell);

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

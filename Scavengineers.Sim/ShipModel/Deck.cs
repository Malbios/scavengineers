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
    private readonly Dictionary<CellCoord, float> _floorHealth = new();
    private readonly Dictionary<CellCoord, float> _ceilingHealth = new();
    private readonly Dictionary<(CellCoord, CellCoord), float> _wallHealth = new();

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
        _floorHealth.Remove(cell);
        _ceilingHealth.Remove(cell);
        foreach (var edge in _wallHealth.Keys.Where(e => e.Item1 == cell || e.Item2 == cell).ToList())
        {
            _wallHealth.Remove(edge);
        }
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

    /// <summary>Structural health, 0-1 per cell/edge — missing means full health (1.0), same
    /// "absence is the default" convention <see cref="_sealedEdges"/>/<see cref="_breaches"/>
    /// already use, so every existing cell needs no explicit seeding. Decayed by
    /// <see cref="Hazards.WearSystem"/>'s passive tick; repaired via
    /// <see cref="RepairFloor"/>/<see cref="RepairCeiling"/>/<see cref="RepairWall"/> (see
    /// ShipBuildTarget's own Maintain/Repair verbs).</summary>
    public float FloorHealth(CellCoord cell) => _floorHealth.GetValueOrDefault(cell, 1f);

    public float CeilingHealth(CellCoord cell) => _ceilingHealth.GetValueOrDefault(cell, 1f);

    public float WallHealth(CellCoord a, CellCoord b) => _wallHealth.GetValueOrDefault(Normalize(a, b), 1f);

    /// <summary>Reduces floor health by `amount` (clamped at 0) — reaching exactly 0 calls the
    /// *existing* <see cref="BreachHull"/> automatically, so decay is just a new cause feeding the
    /// same breach mechanic every other consumer (atmosphere, movement, panels) already reads;
    /// nothing downstream needs to know health exists at all.</summary>
    public void DamageFloor(CellCoord cell, float amount)
    {
        var health = Math.Max(0f, FloorHealth(cell) - amount);
        _floorHealth[cell] = health;
        if (health <= 0f)
        {
            BreachHull(cell, StructuralSurface.Floor);
        }
    }

    public void DamageCeiling(CellCoord cell, float amount)
    {
        var health = Math.Max(0f, CeilingHealth(cell) - amount);
        _ceilingHealth[cell] = health;
        if (health <= 0f)
        {
            BreachHull(cell, StructuralSurface.Ceiling);
        }
    }

    public void DamageWall(CellCoord a, CellCoord b, float amount)
    {
        var edge = Normalize(a, b);
        var health = Math.Max(0f, WallHealth(a, b) - amount);
        _wallHealth[edge] = health;
        if (health <= 0f)
        {
            BreachWallEdge(a, b);
        }
    }

    /// <summary>Resets health to full — does *not* itself clear a breach (repairing is only ever
    /// offered while a surface isn't breached; a genuinely breached surface needs the existing,
    /// more expensive Install verb instead, which resets health as part of installing a fresh
    /// panel).</summary>
    public void RepairFloor(CellCoord cell) => _floorHealth[cell] = 1f;

    public void RepairCeiling(CellCoord cell) => _ceilingHealth[cell] = 1f;

    public void RepairWall(CellCoord a, CellCoord b) => _wallHealth[Normalize(a, b)] = 1f;

    /// <summary>Sets health to an absolute value with no clamping or breach side-effect — used
    /// only by save/load restore (see ShipBuildTarget.ApplyBuildState), which reconstructs breach
    /// state separately and explicitly; DamageFloor's own breach-on-zero side effect would be
    /// redundant (and load-order-dependent) here.</summary>
    public void SetFloorHealth(CellCoord cell, float health) => _floorHealth[cell] = health;

    public void SetCeilingHealth(CellCoord cell, float health) => _ceilingHealth[cell] = health;

    public void SetWallHealth(CellCoord a, CellCoord b, float health) => _wallHealth[Normalize(a, b)] = health;

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

using Scavengineers.Sim.Connectivity;
using Scavengineers.Sim.Grid;

namespace Scavengineers.Sim.Atmosphere;

/// <summary>
/// Minimal atmosphere sim for Phase 0 Spike 2: a single-deck grid of cells, sealed internal
/// edges (walls) and breached hull cells (open to vacuum). Reuses <see cref="ConnectivitySolver"/> —
/// the same solver <see cref="Scavengineers.Sim.Power.PowerSystem"/> reuses for its own network.
/// </summary>
public sealed class AtmosphereSystem : IConnectivityGraph<AtmosphereNode>
{
    private const double VentRatePerSecond = 0.5;

    private readonly HashSet<CellCoord> _cells;
    private readonly HashSet<(CellCoord, CellCoord)> _sealedEdges = [];
    private readonly HashSet<CellCoord> _breachedHull = [];
    private readonly Dictionary<CellCoord, AtmosphereVolume> _volumes;

    public AtmosphereSystem(IEnumerable<CellCoord> cells, AtmosphereVolume? initialVolume = null)
    {
        _cells = [.. cells];
        _volumes = _cells.ToDictionary(c => c, _ => initialVolume ?? AtmosphereVolume.Breathable);
    }

    public IEnumerable<AtmosphereNode> Nodes =>
        _cells.Select(c => new AtmosphereNode(c)).Append(AtmosphereNode.Outside);

    public IEnumerable<AtmosphereNode> Neighbors(AtmosphereNode node)
    {
        if (node.IsOutside)
        {
            foreach (var breached in _breachedHull)
            {
                yield return new AtmosphereNode(breached);
            }
            yield break;
        }

        var cell = node.Cell!.Value;

        if (_breachedHull.Contains(cell))
        {
            yield return AtmosphereNode.Outside;
        }

        foreach (var neighbor in AdjacentCells(cell))
        {
            if (_cells.Contains(neighbor) && !_sealedEdges.Contains(Normalize(cell, neighbor)))
            {
                yield return new AtmosphereNode(neighbor);
            }
        }
    }

    public void SealEdge(CellCoord a, CellCoord b) => _sealedEdges.Add(Normalize(a, b));

    public void UnsealEdge(CellCoord a, CellCoord b) => _sealedEdges.Remove(Normalize(a, b));

    public void BreachHull(CellCoord cell) => _breachedHull.Add(cell);

    public void RepairHull(CellCoord cell) => _breachedHull.Remove(cell);

    public AtmosphereVolume VolumeAt(CellCoord cell) => _volumes[cell];

    /// <summary>
    /// Advances the sim by <paramref name="dt"/> seconds: equalizes each sealed component's
    /// scalars across its cells, then vents any component connected to the outside toward vacuum.
    /// </summary>
    public void Tick(double dt)
    {
        foreach (var component in ConnectivitySolver.FindComponents(this))
        {
            var cells = component.Where(n => !n.IsOutside).Select(n => n.Cell!.Value).ToList();
            if (cells.Count == 0)
            {
                continue;
            }

            Equalize(cells);

            if (component.Contains(AtmosphereNode.Outside))
            {
                Vent(cells, dt);
            }
        }
    }

    private void Equalize(IReadOnlyList<CellCoord> cells)
    {
        if (cells.Count < 2)
        {
            return;
        }

        var averagePressure = cells.Average(c => _volumes[c].Pressure);
        var averageO2 = cells.Average(c => _volumes[c].O2Fraction);
        var averageTemperature = cells.Average(c => _volumes[c].Temperature);

        foreach (var cell in cells)
        {
            _volumes[cell] = _volumes[cell] with
            {
                Pressure = averagePressure,
                O2Fraction = averageO2,
                Temperature = averageTemperature,
            };
        }
    }

    private void Vent(IReadOnlyList<CellCoord> cells, double dt)
    {
        var factor = Math.Clamp(VentRatePerSecond * dt, 0, 1);

        foreach (var cell in cells)
        {
            var current = _volumes[cell];
            _volumes[cell] = current with
            {
                Pressure = Lerp(current.Pressure, AtmosphereVolume.Vacuum.Pressure, factor),
                O2Fraction = Lerp(current.O2Fraction, AtmosphereVolume.Vacuum.O2Fraction, factor),
            };
        }
    }

    private static double Lerp(double from, double to, double factor) => from + (to - from) * factor;

    private static IEnumerable<CellCoord> AdjacentCells(CellCoord cell)
    {
        yield return cell with { X = cell.X + 1 };
        yield return cell with { X = cell.X - 1 };
        yield return cell with { Y = cell.Y + 1 };
        yield return cell with { Y = cell.Y - 1 };
    }

    private static (CellCoord, CellCoord) Normalize(CellCoord a, CellCoord b)
    {
        if (a.X != b.X)
        {
            return a.X < b.X ? (a, b) : (b, a);
        }

        return a.Y <= b.Y ? (a, b) : (b, a);
    }
}

using Scavengineers.Sim.Connectivity;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Sim.Atmosphere;

/// <summary>
/// A pure reader of a <see cref="Deck"/> (the shared structural tier — see
/// docs/architecture/ship-model.md) plus its own atmosphere-specific state (per-cell
/// <see cref="AtmosphereVolume"/>). Reuses <see cref="ConnectivitySolver"/> — the same
/// solver <see cref="Scavengineers.Sim.Power.PowerSystem"/> reuses for its own network.
/// </summary>
public sealed class AtmosphereSystem : IConnectivityGraph<AtmosphereNode>
{
    private const double VentRatePerSecond = 0.5;
    private const double LifeSupportRegenRatePerSecond = 0.2;

    private readonly Deck _deck;
    private readonly Dictionary<CellCoord, AtmosphereVolume> _volumes;
    private readonly bool _hasLifeSupport;

    /// <param name="hasLifeSupport">Whether a sealed (non-vented) component should drift back
    /// toward <see cref="AtmosphereVolume.Breathable"/> over time, representing always-on
    /// scrubbers/O2 generation — defaults to <c>false</c> so anything that doesn't opt in keeps
    /// today's behavior (a sealed room just holds whatever scalars it's at).</param>
    public AtmosphereSystem(Deck deck, AtmosphereVolume? initialVolume = null, bool hasLifeSupport = false)
    {
        _deck = deck;
        _volumes = _deck.Cells.ToDictionary(c => c, _ => initialVolume ?? AtmosphereVolume.Breathable);
        _hasLifeSupport = hasLifeSupport;
    }

    public IEnumerable<AtmosphereNode> Nodes =>
        _deck.Cells.Select(c => new AtmosphereNode(c)).Append(AtmosphereNode.Outside);

    public IEnumerable<AtmosphereNode> Neighbors(AtmosphereNode node)
    {
        if (node.IsOutside)
        {
            foreach (var breached in _deck.HullBreaches)
            {
                if (_deck.Cells.Contains(breached))
                {
                    yield return new AtmosphereNode(breached);
                }
            }
            yield break;
        }

        var cell = node.Cell!.Value;

        if (_deck.IsHullBreached(cell))
        {
            yield return AtmosphereNode.Outside;
        }

        foreach (var neighbor in AdjacentCells(cell))
        {
            if (_deck.Cells.Contains(neighbor) && !_deck.IsEdgeSealed(cell, neighbor))
            {
                yield return new AtmosphereNode(neighbor);
            }
        }
    }

    public AtmosphereVolume VolumeAt(CellCoord cell) => _volumes[cell];

    /// <summary>Registers a cell added to <see cref="_deck"/> after construction (dynamic ship
    /// expansion) — the constructor's own <c>_volumes</c> snapshot has no other way to grow, so
    /// skipping this leaves every read/write below throwing KeyNotFoundException the next tick.
    /// Defaults to Breathable, same as the constructor's own default, since a freshly extended
    /// cell is normally still connected to its origin room's air.</summary>
    public void AddCell(CellCoord cell, AtmosphereVolume? volume = null) =>
        _volumes[cell] = volume ?? AtmosphereVolume.Breathable;

    /// <summary>Counterpart of <see cref="AddCell"/> for a cell removed from <see cref="_deck"/>
    /// (e.g. an extended cell torn down by a load that doesn't include it) — keeps this system's
    /// own per-cell state from silently outliving the Deck cell it belonged to.</summary>
    public void RemoveCell(CellCoord cell) => _volumes.Remove(cell);

    /// <summary>
    /// Overwrites a cell's volume from outside this system's own <see cref="Tick"/> — the hook
    /// <see cref="AirlockBridge"/> uses to write back a cell's state after equalizing it against
    /// another, independent <see cref="AtmosphereSystem"/> (e.g. a docked ship's).
    /// </summary>
    public void ApplyExternalVolume(CellCoord cell, AtmosphereVolume volume) => _volumes[cell] = volume;

    /// <summary>
    /// Every cell currently connected to <paramref name="cell"/> (through unsealed edges, not
    /// vented to Outside) — the hook <see cref="AirlockBridge"/> uses to bridge two ships'
    /// *entire* currently-connected volumes rather than just one named tile each, since a real
    /// airlock joins two whole rooms, not two points.
    /// </summary>
    public IReadOnlySet<CellCoord> ComponentContaining(CellCoord cell)
    {
        var node = new AtmosphereNode(cell);
        foreach (var component in ConnectivitySolver.FindComponents(this))
        {
            if (component.Contains(node))
            {
                return component.Where(n => !n.IsOutside).Select(n => n.Cell!.Value).ToHashSet();
            }
        }

        return new HashSet<CellCoord> { cell };
    }

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
            else if (_hasLifeSupport)
            {
                Regenerate(cells, dt);
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

    private void Regenerate(IReadOnlyList<CellCoord> cells, double dt)
    {
        var factor = Math.Clamp(LifeSupportRegenRatePerSecond * dt, 0, 1);

        foreach (var cell in cells)
        {
            var current = _volumes[cell];
            _volumes[cell] = current with
            {
                Pressure = Lerp(current.Pressure, AtmosphereVolume.Breathable.Pressure, factor),
                O2Fraction = Lerp(current.O2Fraction, AtmosphereVolume.Breathable.O2Fraction, factor),
                Temperature = Lerp(current.Temperature, AtmosphereVolume.Breathable.Temperature, factor),
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
}

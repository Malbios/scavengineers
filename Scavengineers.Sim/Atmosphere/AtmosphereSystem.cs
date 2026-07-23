using Scavengineers.Sim.Connectivity;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Sim.Atmosphere;

/// <summary>A pure reader of a <see cref="Deck"/> (the shared structural tier) plus its own
/// atmosphere-specific state (per-cell <see cref="AtmosphereVolume"/>). Reuses
/// <see cref="ConnectivitySolver"/> — the same solver <see cref="Scavengineers.Sim.Power.PowerSystem"/>
/// reuses for its own network.</summary>
public sealed class AtmosphereSystem : IConnectivityGraph<AtmosphereNode>
{
    // Placeholder/tunable — applied uniformly to an entire vented component (see Vent): remaining
    // fraction after t seconds ≈ e^(-VentRatePerSecond * t), so at 5.0, any breached component
    // (regardless of size) reaches near-vacuum in roughly 1-2s, matching real depressurization.
    private const double VentRatePerSecond = 5.0;
    private const double LifeSupportRegenRatePerSecond = 0.2;

    // Placeholder/tunable — picked via a throwaway probe (a straight 16-cell corridor, breach
    // cell 0 only, tick at 1/60 dt). At this rate a cell 5 hops from the breach stays within ~2%
    // of full Breathable O2 for the first 1-2 seconds, then visibly drops; the whole corridor
    // still converges to near-vacuum in ~50s. A lower rate (2.0) gave a longer grace period but
    // left the far end of a large room stuck around 15%+ O2 even after 50s.
    private const double DiffusionRatePerSecond = 12.0;

    // Hard ceiling on the per-tick diffusion factor, independent of dt or DiffusionRatePerSecond.
    // Diffuse is a simultaneous (Jacobi) neighbor-average step, and two mutually-adjacent cells
    // can "sign-flip" (sloshing back and forth) instead of converging once the factor exceeds 0.5
    // (for a 2-node pair, the imbalance decays by (1 - 2*factor) each tick — negative past 0.5).
    // 0.25 leaves 2x headroom and also protects against a frame-hitch dt spike. Vent/Regenerate
    // don't need this cap — they're single-target Lerps toward a fixed constant, not
    // pairwise-coupled.
    private const double MaxDiffusionFactorPerTick = 0.25;

    private readonly Deck _deck;
    private readonly Dictionary<CellCoord, AtmosphereVolume> _volumes;
    private readonly bool _hasLifeSupport;
    private readonly HashSet<CellCoord> _externallyVented = new();

    /// <param name="hasLifeSupport">Whether a sealed (non-vented) component should drift back
    /// toward <see cref="AtmosphereVolume.Breathable"/> over time, representing always-on
    /// scrubbers/O2 generation — defaults to false, so a sealed room just holds whatever scalars
    /// it's at.</param>
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

        foreach (var neighbor in UnsealedCellNeighbors(cell))
        {
            yield return new AtmosphereNode(neighbor);
        }
    }

    /// <summary>The shared "what counts as a connected cell" check — used both by
    /// <see cref="Neighbors"/> and by Diffuse itself, so they can never disagree about adjacency.</summary>
    private IEnumerable<CellCoord> UnsealedCellNeighbors(CellCoord cell)
    {
        foreach (var neighbor in cell.OrthogonalNeighbors())
        {
            if (_deck.Cells.Contains(neighbor) && !_deck.IsEdgeSealed(cell, neighbor))
            {
                yield return neighbor;
            }
        }
    }

    public AtmosphereVolume VolumeAt(CellCoord cell) => _volumes[cell];

    /// <summary>Registers a cell added to <see cref="_deck"/> after construction (dynamic ship
    /// expansion) — the constructor's own <c>_volumes</c> snapshot has no other way to grow, so
    /// skipping this leaves every read/write below throwing KeyNotFoundException the next tick.</summary>
    public void AddCell(CellCoord cell, AtmosphereVolume? volume = null) =>
        _volumes[cell] = volume ?? AtmosphereVolume.Breathable;

    /// <summary>Counterpart of <see cref="AddCell"/> for a cell removed from <see cref="_deck"/> —
    /// keeps this system's own per-cell state from silently outliving the Deck cell it belonged
    /// to.</summary>
    public void RemoveCell(CellCoord cell) => _volumes.Remove(cell);

    /// <summary>Overwrites a cell's volume from outside this system's own <see cref="Tick"/> —
    /// the hook <see cref="AirlockBridge"/> uses to write back a cell's state after equalizing it
    /// against another, independent <see cref="AtmosphereSystem"/>.</summary>
    public void ApplyExternalVolume(CellCoord cell, AtmosphereVolume volume) => _volumes[cell] = volume;

    /// <summary>The raw connectivity-graph component (cells plus the shared Outside sentinel, if
    /// reachable) containing <paramref name="cell"/> — the shared traversal behind both
    /// <see cref="ComponentContaining"/> and <see cref="IsConnectedToOutside"/>.</summary>
    private IReadOnlySet<AtmosphereNode> RawComponentContaining(CellCoord cell)
    {
        var node = new AtmosphereNode(cell);
        foreach (var component in ConnectivitySolver.FindComponents(this))
        {
            if (component.Contains(node))
            {
                return component;
            }
        }

        return new HashSet<AtmosphereNode> { node };
    }

    /// <summary>Every cell currently connected to <paramref name="cell"/> (through unsealed
    /// edges, not vented to Outside) — the hook <see cref="Scavengineers.Scripts.Player.Player"/>
    /// uses for its own smoke-detection check.</summary>
    public IReadOnlySet<CellCoord> ComponentContaining(CellCoord cell) =>
        RawComponentContaining(cell).Where(n => !n.IsOutside).Select(n => n.Cell!.Value).ToHashSet();

    /// <summary>Whether <paramref name="cell"/>'s current component includes the shared
    /// <see cref="AtmosphereNode.Outside"/> sentinel — one of the two conditions (the other being
    /// <see cref="MarkExternallyVented"/>) under which <see cref="Tick"/> would <see cref="Vent"/>
    /// this component. <see cref="AirlockBridge"/> uses this to decide whether either side of an
    /// open airlock already has its own leak to vacuum, in which case the airlock becomes part of
    /// that leak instead of just averaging the two cells against each other.</summary>
    public bool IsConnectedToOutside(CellCoord cell) => RawComponentContaining(cell).Contains(AtmosphereNode.Outside);

    /// <summary>All deck cells whose current connected component reaches
    /// <see cref="AtmosphereNode.Outside"/> — a single <see cref="ConnectivitySolver.FindComponents"/>
    /// pass, so a caller seeding a freshly-breached ship's cells to
    /// <see cref="AtmosphereVolume.Vacuum"/> immediately doesn't need its own second flood-fill.
    /// Since Outside is one shared sentinel connected to every hull breach at once, every breached
    /// room ends up in this one component — there's never more than one.</summary>
    public IReadOnlySet<CellCoord> CellsConnectedToOutside()
    {
        foreach (var component in ConnectivitySolver.FindComponents(this))
        {
            if (component.Contains(AtmosphereNode.Outside))
            {
                return component.Where(n => !n.IsOutside).Select(n => n.Cell!.Value).ToHashSet();
            }
        }

        return new HashSet<CellCoord>();
    }

    /// <summary>Marks a cell as currently being drained by an open <see cref="AirlockBridge"/>
    /// whose far side has its own path to Outside — for this tick only. This is what makes
    /// <see cref="Tick"/> treat this cell's whole connected component as vented, even though its
    /// own internal connectivity graph has no breach of its own (a bridge is a deliberate
    /// bolt-on, never merged into the graph); it also suppresses life-support regen for that
    /// component, since Vent and Regenerate are mutually exclusive per component. Re-applied
    /// every tick the leak condition holds and cleared unconditionally at the end of every
    /// <see cref="Tick"/> call — so consumption is deferred to whichever <see cref="Tick"/> call
    /// runs *next* (see <see cref="AirlockBridge.Tick"/> for the ordering this relies on).</summary>
    public void MarkExternallyVented(CellCoord cell) => _externallyVented.Add(cell);

    /// <summary>Advances the sim by <paramref name="dt"/> seconds. Each connected component gets
    /// exactly one of two treatments, never both: if it's connected to Outside or has any cell
    /// marked via <see cref="MarkExternallyVented"/> this tick, the whole component is vented
    /// uniformly — matching real depressurization, where internal pressure equalizes at the
    /// speed of sound, vastly faster than air escaping through a hole. Otherwise the component
    /// diffuses and, if it has life support, is regenerated toward Breathable — this path is for
    /// two sealed, non-vented rooms mixing through an open door, which stays legitimately gradual.</summary>
    public void Tick(double dt)
    {
        foreach (var component in ConnectivitySolver.FindComponents(this))
        {
            var cells = component.Where(n => !n.IsOutside).Select(n => n.Cell!.Value).ToList();
            if (cells.Count == 0)
            {
                continue;
            }

            var isVented = component.Contains(AtmosphereNode.Outside) || cells.Any(_externallyVented.Contains);

            if (isVented)
            {
                Vent(cells, dt);
            }
            else
            {
                Diffuse(cells, dt);
                if (_hasLifeSupport)
                {
                    Regenerate(cells, dt);
                }
            }
        }

        _externallyVented.Clear();
    }

    /// <summary>Each cell moves toward the average of its own unsealed neighbors' *previous*-tick
    /// values (a simultaneous/"Jacobi" step, not a sequential one — see the snapshot below) —
    /// reserved for sealed, non-vented multi-cell components only (e.g. two rooms joined by an
    /// open door, neither with its own path to Outside). Never runs for a vented component: a
    /// real breach's pressure equalizes across the whole connected volume far faster than air
    /// escapes through the hole, so there's no distance-based lag to model there (see
    /// <see cref="Vent"/>).</summary>
    private void Diffuse(IReadOnlyList<CellCoord> cells, double dt)
    {
        if (cells.Count < 2)
        {
            return;
        }

        var factor = Math.Min(DiffusionRatePerSecond * dt, MaxDiffusionFactorPerTick);
        if (factor <= 0)
        {
            return;
        }

        var snapshot = cells.ToDictionary(c => c, c => _volumes[c]);
        var updated = new Dictionary<CellCoord, AtmosphereVolume>(snapshot.Count);

        foreach (var cell in cells)
        {
            var neighborValues = UnsealedCellNeighbors(cell).Select(n => snapshot[n]).ToList();
            if (neighborValues.Count == 0)
            {
                continue;
            }

            var current = snapshot[cell];
            updated[cell] = current with
            {
                Pressure = Lerp(current.Pressure, neighborValues.Average(v => v.Pressure), factor),
                O2Fraction = Lerp(current.O2Fraction, neighborValues.Average(v => v.O2Fraction), factor),
                Temperature = Lerp(current.Temperature, neighborValues.Average(v => v.Temperature), factor),
            };
        }

        foreach (var (cell, volume) in updated)
        {
            _volumes[cell] = volume;
        }
    }

    /// <summary>Vents every cell in a component connected to Outside (or externally vented via
    /// <see cref="MarkExternallyVented"/>) toward vacuum, uniformly and independently — no cell
    /// lags another, since each gets the same Lerp toward the same target from a component that
    /// started at the same values. Matches real depressurization: internal pressure equalizes far
    /// faster than air escapes through a hole, so the whole connected volume drops together at a
    /// rate set by the hole, not by hop-count from it.</summary>
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
                Temperature = Lerp(current.Temperature, AtmosphereVolume.Vacuum.Temperature, factor),
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
}

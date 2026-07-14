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
    // Placeholder/tunable — must decisively outpace AirlockBridge's own EqualizeRatePerSecond
    // (0.5). At equal rates, opening an airlock into a breached room let the bridge's push toward
    // a shared average and this vent's pull toward vacuum settle into a tug-of-war equilibrium
    // that held several percent O2 indefinitely — the room visibly "got a bit of air" the whole
    // time the airlock stayed open, instead of just a brief moment. 10x the bridge's rate keeps
    // that peak under 1% at realistic per-frame dt (see AirlockBridgeTests).
    private const double VentRatePerSecond = 5.0;
    private const double LifeSupportRegenRatePerSecond = 0.2;

    // Placeholder/tunable — picked via a throwaway probe (a straight 16-cell corridor, breach
    // cell 0 only, tick at a realistic 1/60 dt). At this rate, a cell 5 hops from the breach
    // stays within ~2% of full Breathable O2 for the first ~1-2 seconds (the "moment of grace"
    // this change exists for), then visibly drops; a cell 10 hops away stays essentially
    // untouched for a couple of seconds before following. The whole corridor still converges to
    // near-vacuum in ~50s (the far end, 15 hops out, reaches ~2% O2 by then) — comparable to
    // BreachedHull_ApproachesVacuumAsTimeAccumulates's single-cell benchmark, not wildly slower.
    // A lower rate (2.0) gave a longer, more dramatic grace period but left the far end of a
    // large room stuck around 15%+ O2 even after 50s — too slow to ever feel like it resolves.
    private const double DiffusionRatePerSecond = 12.0;

    // Hard ceiling on the per-tick diffusion factor, independent of dt or DiffusionRatePerSecond.
    // Diffuse is a simultaneous (Jacobi) neighbor-average step, and two mutually-adjacent cells
    // updating from the same snapshot can "sign-flip" (sloshing back and forth) instead of
    // smoothly converging once the factor exceeds 0.5 (for a 2-node pair, the imbalance decays by
    // (1 - 2*factor) each tick — negative once factor > 0.5, meaning the two cells swap places
    // instead of meeting in the middle). 0.25 leaves 2x headroom under that threshold and also
    // protects against a large/spiking dt (a frame hitch) turning a modest rate into an
    // oscillation-triggering factor for one unlucky frame. Vent/Regenerate don't need this cap —
    // they're single-target Lerps toward a fixed constant, not pairwise-coupled.
    private const double MaxDiffusionFactorPerTick = 0.25;

    private readonly Deck _deck;
    private readonly Dictionary<CellCoord, AtmosphereVolume> _volumes;
    private readonly bool _hasLifeSupport;
    private readonly HashSet<CellCoord> _externallyVented = new();

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

        foreach (var neighbor in UnsealedCellNeighbors(cell))
        {
            yield return new AtmosphereNode(neighbor);
        }
    }

    /// <summary>The shared "what counts as a connected cell" check — used both by <see
    /// cref="Neighbors"/> (so the connectivity graph and <see cref="Diffuse"/> can never disagree
    /// about adjacency) and by Diffuse itself.</summary>
    private IEnumerable<CellCoord> UnsealedCellNeighbors(CellCoord cell)
    {
        foreach (var neighbor in AdjacentCells(cell))
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

    /// <summary>The raw connectivity-graph component (cells plus the shared Outside sentinel, if
    /// reachable) containing <paramref name="cell"/> — the shared traversal behind both <see
    /// cref="ComponentContaining"/> (public, cell-only view) and <see
    /// cref="IsConnectedToOutside"/> (does this component reach Outside), keeping both in
    /// lockstep with how <see cref="Tick"/> itself partitions the deck via <see
    /// cref="ConnectivitySolver.FindComponents"/>.</summary>
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

    /// <summary>
    /// Every cell currently connected to <paramref name="cell"/> (through unsealed edges, not
    /// vented to Outside) — the hook <see cref="Scavengineers.Scripts.Player.Player"/> uses for
    /// its own smoke-detection check ("is there a fire anywhere in my current room").
    /// </summary>
    public IReadOnlySet<CellCoord> ComponentContaining(CellCoord cell) =>
        RawComponentContaining(cell).Where(n => !n.IsOutside).Select(n => n.Cell!.Value).ToHashSet();

    /// <summary>Whether <paramref name="cell"/>'s current component includes the shared <see
    /// cref="AtmosphereNode.Outside"/> sentinel — i.e. whether <see cref="Tick"/> would <see
    /// cref="Vent"/> this component this tick. <see cref="AirlockBridge"/> uses this to decide
    /// whether either side of an open airlock already has its own leak to vacuum: if so, the
    /// airlock becomes part of that leak for both sides instead of just averaging the two cells
    /// against each other (see AirlockBridge's own doc comment for why plain averaging isn't
    /// enough there). Runs a full connectivity pass same as <see cref="ComponentContaining"/> —
    /// called up to twice per AirlockBridge tick, on top of Tick's own per-system pass; in line
    /// with this game's existing deck sizes, not a new asymptotic cost.</summary>
    public bool IsConnectedToOutside(CellCoord cell) => RawComponentContaining(cell).Contains(AtmosphereNode.Outside);

    /// <summary>Marks a cell as currently being drained by an open <see cref="AirlockBridge"/>
    /// whose far side has its own path to Outside — for this tick only. Suppresses life-support
    /// regen for this cell's whole connected component (see <see cref="Tick"/>), since the ship
    /// is now effectively leaking through this cell even though its own internal connectivity
    /// graph has no breach of its own (a bridge is a deliberate bolt-on, never merged into the
    /// graph — see <see cref="AirlockBridge"/>'s own doc comment). Re-applied every tick the leak
    /// condition holds (self-sustaining, no permanent state needed) and cleared unconditionally
    /// at the end of every <see cref="Tick"/> call.</summary>
    public void MarkExternallyVented(CellCoord cell) => _externallyVented.Add(cell);

    /// <summary>
    /// Advances the sim by <paramref name="dt"/> seconds: diffuses each connected component's
    /// scalars toward each cell's own neighbors (not an instant whole-component average), then
    /// vents any directly-breached cell in a component connected to the outside toward vacuum,
    /// or regenerates a fully-sealed component with life support toward breathable — unless a
    /// cell in that component was marked via <see cref="MarkExternallyVented"/> this tick, in
    /// which case regen is skipped entirely: a whole-component regen at
    /// <see cref="LifeSupportRegenRatePerSecond"/> can otherwise out-compete an
    /// <see cref="AirlockBridge"/>'s single-point drain purely through dilution (that one cell
    /// keeps getting refilled by <see cref="Diffuse"/> from its many still-full neighbors faster
    /// than the bridge can drain it), settling into a stable, deceptively-safe plateau instead of
    /// ever converging toward vacuum.
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

            Diffuse(cells, dt);

            if (component.Contains(AtmosphereNode.Outside))
            {
                Vent(cells.Where(_deck.IsHullBreached).ToList(), dt);
            }
            else if (_hasLifeSupport && !cells.Any(_externallyVented.Contains))
            {
                Regenerate(cells, dt);
            }
        }

        _externallyVented.Clear();
    }

    /// <summary>Each cell moves toward the average of its own unsealed neighbors' *previous*-tick
    /// values (a simultaneous/"Jacobi" step, not a sequential one — see the snapshot below) —
    /// replaces the old instant whole-component average so distance/hop-count now genuinely
    /// matters: a cell several hops from a breach only feels the effect once it's propagated
    /// neighbor-by-neighbor, rather than every cell in a room updating in lockstep the same
    /// tick. Runs unconditionally for every multi-cell component (breached or not) — two sealed
    /// rooms joined by an open door still gradually mix toward each other, just now over several
    /// ticks instead of instantly.</summary>
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

    /// <summary>Vents only the cell(s) directly exposed to a hull breach — everywhere else in the
    /// component only feels this via <see cref="Diffuse"/> carrying the drop outward tick by
    /// tick, which is what creates the "a few cells away keeps some air for a moment"
    /// distance-based delay.</summary>
    private void Vent(IReadOnlyList<CellCoord> breachedCells, double dt)
    {
        var factor = Math.Clamp(VentRatePerSecond * dt, 0, 1);

        foreach (var cell in breachedCells)
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

    private static IEnumerable<CellCoord> AdjacentCells(CellCoord cell)
    {
        yield return cell with { X = cell.X + 1 };
        yield return cell with { X = cell.X - 1 };
        yield return cell with { Y = cell.Y + 1 };
        yield return cell with { Y = cell.Y - 1 };
    }
}

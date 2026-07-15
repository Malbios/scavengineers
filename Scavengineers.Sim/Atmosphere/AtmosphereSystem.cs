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
    // Placeholder/tunable — applied uniformly to an entire vented component (see Vent), so this
    // now sets how fast ANY size room/ship reaches near-vacuum once breached, not just a single
    // cell's convergence speed: remaining fraction after t seconds ≈ e^(-VentRatePerSecond * t),
    // so at 5.0, ~0.67% remains after 1 second — any breached component, regardless of its size,
    // reaches near-vacuum in roughly 1-2s. This is a substantial pacing change from the old
    // per-cell/diffusion-carried approach (minutes for a large ship) and needs an explicit
    // playtest check, not a blind retune — see docs/architecture/atmosphere-power-sim.md.
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
    /// cref="AtmosphereNode.Outside"/> sentinel — one of the two conditions (the other being <see
    /// cref="MarkExternallyVented"/>) under which <see cref="Tick"/> would <see cref="Vent"/> this
    /// component this tick. <see cref="AirlockBridge"/> uses this to decide whether either side of
    /// an open airlock already has its own leak to vacuum: if so, the airlock becomes part of
    /// that leak for both sides instead of just averaging the two cells against each other (see
    /// AirlockBridge's own doc comment). Runs a full connectivity pass same as <see
    /// cref="ComponentContaining"/> — called up to twice per AirlockBridge tick, on top of Tick's
    /// own per-system pass; in line with this game's existing deck sizes, not a new asymptotic
    /// cost.</summary>
    public bool IsConnectedToOutside(CellCoord cell) => RawComponentContaining(cell).Contains(AtmosphereNode.Outside);

    /// <summary>All deck cells whose current connected component reaches <see
    /// cref="AtmosphereNode.Outside"/> (i.e. what the very next <see cref="Tick"/> would <see
    /// cref="Vent"/>) — a single <see cref="ConnectivitySolver.FindComponents"/> pass, so a caller
    /// seeding a freshly-breached ship's cells to <see cref="AtmosphereVolume.Vacuum"/> immediately
    /// (see <see cref="Scavengineers.Scripts.Ship.ShipSim"/>) doesn't need its own second flood-fill
    /// over this same graph. Since Outside is one shared sentinel connected to every hull breach at
    /// once, every breached room ends up in this one component — there's never more than one.</summary>
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
    /// whose far side has its own path to Outside — for this tick only. This is what makes <see
    /// cref="Tick"/> treat this cell's whole connected component as vented (see <see
    /// cref="Vent"/>), even though its own internal connectivity graph has no breach of its own (a
    /// bridge is a deliberate bolt-on, never merged into the graph — see <see
    /// cref="AirlockBridge"/>'s own doc comment); this also has the effect of suppressing
    /// life-support regen for that component, since Vent and Regenerate are mutually exclusive per
    /// component now. Re-applied every tick the leak condition holds (self-sustaining, no
    /// permanent state needed) and cleared unconditionally at the end of every <see cref="Tick"/>
    /// call — so consumption is deferred to whichever of this system's own <see cref="Tick"/>
    /// calls runs *next*, not the same call that marked it (see <see cref="AirlockBridge.Tick"/>
    /// for the ordering this relies on).</summary>
    public void MarkExternallyVented(CellCoord cell) => _externallyVented.Add(cell);

    /// <summary>
    /// Advances the sim by <paramref name="dt"/> seconds. Each connected component gets exactly
    /// one of two treatments, never both: if it's connected to Outside (a direct hull breach) or
    /// has any cell marked via <see cref="MarkExternallyVented"/> this tick (an open <see
    /// cref="AirlockBridge"/> leaking into vacuum on the far side), the whole component is vented
    /// uniformly — matching real depressurization, where internal pressure equalizes at the speed
    /// of sound, vastly faster than air escaping through a hole, so a connected volume has no time
    /// to develop a distance-based gradient (see docs/architecture/atmosphere-power-sim.md).
    /// Otherwise the component diffuses (each cell moving toward its own unsealed neighbors,
    /// gradually) and, if it has life support, is regenerated toward Breathable — this remaining
    /// path is for two sealed, non-vented rooms mixing through an open door, which has no external
    /// vacuum driving force and stays legitimately gradual.
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
    /// reserved now for sealed, non-vented multi-cell components only (e.g. two rooms joined by an
    /// open door, neither with its own path to Outside). Never runs for a vented component: a real
    /// breach's pressure equalizes across the whole connected volume far faster than air escapes
    /// through the hole, so there's no physical distance-based lag to model there — see <see
    /// cref="Vent"/> and docs/architecture/atmosphere-power-sim.md. Kept here only because two
    /// sealed rooms mixing through an open, unbreached door genuinely has no external vacuum
    /// forcing an instant equalization, so a gradual multi-tick mix is still the right model for
    /// that case.</summary>
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

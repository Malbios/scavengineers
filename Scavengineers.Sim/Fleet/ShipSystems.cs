using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.Hazards;
using Scavengineers.Sim.Power;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Sim.Fleet;

/// <summary>One ship's whole simulation — its <see cref="ShipModel.Deck"/> plus the systems
/// reading it — bundled so it can be created, ticked and inspected with no Godot node anywhere in
/// sight: a ship the player isn't aboard has no loaded scene, so its sim must run without one.
/// The <c>ShipSim</c> node owns one of these and delegates; the systems themselves have no idea
/// whether a scene exists.
///
/// Which systems a ship has is deliberately not uniform: atmosphere and wear apply to every ship,
/// a power system only exists once something can source power, and fire only for a ship that
/// opted into the conduit hazard. Nulls here mean "this ship genuinely has no such system", not
/// "not initialised yet".</summary>
public sealed class ShipSystems
{
    /// <summary>How much simulated time a single coarse tick advances. Chosen so an absent ship
    /// still pays its full costs while costing ~1/60th of the per-frame work, since every one of
    /// these ticks runs a connectivity flood-fill.</summary>
    public const double CoarseTickSeconds = 1.0;

    private double _coarseAccumulator;

    public ShipSystems(Deck deck, bool hasLifeSupport)
    {
        Deck = deck;
        Atmosphere = new AtmosphereSystem(deck, hasLifeSupport: hasLifeSupport);
        Wear = new WearSystem(deck);
    }

    public Deck Deck { get; }

    public AtmosphereSystem Atmosphere { get; }

    public WearSystem Wear { get; }

    /// <summary>Null until something can actually source power on this ship — see
    /// <see cref="EnsurePower"/>.</summary>
    public PowerSystem? Power { get; private set; }

    /// <summary>Null unless this ship opted into the conduit fire hazard.</summary>
    public FireSystem? Fire { get; private set; }

    /// <summary>Creates the power system on first use and returns it — a ship gains one the moment
    /// anything can source or carry power, and several independent callers (a power grid, the fire
    /// hazard's own always-on generator) may each be the first.</summary>
    public PowerSystem EnsurePower() => Power ??= new PowerSystem(Deck);

    public FireSystem EnableFire() => Fire ??= new FireSystem(Deck, Atmosphere, EnsurePower());

    /// <summary>Full-fidelity tick — every system, every frame. For the ship the player is
    /// currently on.</summary>
    public void Tick(double dt)
    {
        Atmosphere.Tick(dt);
        Fire?.Tick(dt);
        Wear.Tick(dt);
    }

    /// <summary>Level-of-detail tick for a ship with no player present: banks elapsed time and
    /// spends it in <see cref="CoarseTickSeconds"/> lumps. The same systems run over the same
    /// total dt — nothing is skipped or discounted, just integrated in fewer, larger steps. Every
    /// system here integrates over dt rather than assuming a fixed step, which is what makes this
    /// safe: wear decays by rate × dt, and atmosphere venting/diffusion are Lerps whose factors
    /// are clamped so a large dt can't overshoot into oscillation. Fire spread is coarser-grained
    /// than at 60fps — it re-evaluates ignition once a second instead of 60 times.</summary>
    public void TickCoarse(double dt)
    {
        _coarseAccumulator += dt;
        if (_coarseAccumulator < CoarseTickSeconds)
        {
            return;
        }

        var elapsed = _coarseAccumulator;
        _coarseAccumulator = 0;
        Tick(elapsed);
    }

    /// <summary>Every modelled cell's current atmosphere, for save/load. Returned as a plain
    /// sequence rather than the live dictionary so a caller can't mutate the sim by writing to
    /// it.</summary>
    public IEnumerable<(CellCoord Cell, AtmosphereVolume Volume)> CaptureVolumes() =>
        Deck.Cells.Select(cell => (cell, Atmosphere.VolumeAt(cell)));

    /// <summary>Restores saved per-cell atmosphere. Cells the save doesn't mention, or that no
    /// longer exist on this deck, are left at whatever startup produced.</summary>
    public void ApplyVolumes(IEnumerable<(CellCoord Cell, AtmosphereVolume Volume)> volumes)
    {
        foreach (var (cell, volume) in volumes)
        {
            if (Deck.Cells.Contains(cell))
            {
                Atmosphere.ApplyExternalVolume(cell, volume);
            }
        }
    }

    /// <summary>Replaces the burning-cell set wholesale. Clearing first matters: a save with no
    /// fires must actually put a currently-burning ship out, not merely fail to add any.</summary>
    public void ApplyFires(IEnumerable<CellCoord> fires)
    {
        foreach (var cell in Deck.Fires.ToList())
        {
            Deck.ExtinguishFire(cell);
        }

        foreach (var cell in fires)
        {
            if (Deck.Cells.Contains(cell))
            {
                Deck.IgniteFire(cell);
            }
        }
    }
}

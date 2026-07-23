using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Sim.Hazards;

/// <summary>Passive wear — everything in the ship slowly degrades over time on its own, whether
/// or not anything else is happening to it (FireSystem's own local heat damage to nearby conduits
/// stacks on top of this baseline rather than replacing it). Decays every Fixture's own Condition
/// except BatteryFixture/ThrusterFixture (whose Condition already means charge) plus every
/// structural surface's health for every present cell/sealed edge — a breached/missing surface
/// has nothing left to decay.</summary>
public sealed class WearSystem(Deck deck)
{
    // Placeholder/tunable — a multi-hour real-time bake from full health (1.0) down to 0 with no
    // maintenance at all, so a normal play session comfortably drifts from Healthy (>0.5) into
    // Damaged (<=0.5) territory without needing active neglect to get there, but nothing fully
    // breaks down from idle decay alone within one sitting.
    private const float DecayPerSecond = 1f / 10800f; // ~3 hours, full health to 0.

    public void Tick(double dt)
    {
        var amount = DecayPerSecond * (float)dt;

        foreach (var fixture in deck.Fixtures)
        {
            if (fixture is BatteryFixture or ThrusterFixture)
            {
                continue;
            }

            fixture.Condition = Math.Max(0f, fixture.Condition - amount);
        }

        foreach (var cell in deck.Cells)
        {
            if (!deck.IsHullBreached(cell, StructuralSurface.Floor))
            {
                deck.DamageFloor(cell, amount);
            }

            if (!deck.IsHullBreached(cell, StructuralSurface.Ceiling))
            {
                deck.DamageCeiling(cell, amount);
            }

            foreach (var neighbor in cell.OrthogonalNeighbors())
            {
                if (deck.Cells.Contains(neighbor))
                {
                    // A real interior edge is visited once from each side — only process it from
                    // its canonical "first" cell (see Deck.Normalize) to avoid double-decaying it.
                    if (Deck.Normalize(cell, neighbor).Item1 != cell)
                    {
                        continue;
                    }

                    if (deck.IsEdgeSealed(cell, neighbor))
                    {
                        deck.DamageWall(cell, neighbor, amount);
                    }
                }
                else if (!deck.IsWallEdgeBreached(cell, neighbor))
                {
                    // A boundary edge (the neighbor doesn't exist) is only ever visited from this
                    // one real cell — no "other side" to dedupe against, so always process it.
                    deck.DamageWall(cell, neighbor, amount);
                }
            }
        }
    }
}

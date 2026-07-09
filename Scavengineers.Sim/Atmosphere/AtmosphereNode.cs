using Scavengineers.Sim.Grid;

namespace Scavengineers.Sim.Atmosphere;

/// <summary>
/// A node in the atmosphere connectivity graph: either a cell, or the single shared
/// "outside" (vacuum) node every hull breach connects into.
/// </summary>
public readonly record struct AtmosphereNode(CellCoord? Cell)
{
    public static readonly AtmosphereNode Outside = new(null);

    public bool IsOutside => Cell is null;
}

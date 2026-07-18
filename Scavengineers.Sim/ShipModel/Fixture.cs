using Scavengineers.Sim.Grid;

namespace Scavengineers.Sim.ShipModel;

/// <summary>
/// Tier-2 placement (docs/architecture/ship-model.md): surface-attached, never affects
/// airtightness. Tracks which tile a fixture is on, not its free sub-tile position —
/// that's a presentation/placement concern, not something the sim layer needs yet.
/// </summary>
public abstract class Fixture(string id, CellCoord tile, FixtureSurface surface)
{
    public string Id { get; } = id;

    public CellCoord Tile { get; } = tile;

    public FixtureSurface Surface { get; } = surface;

    public float Condition { get; set; } = 1.0f;

    /// <summary>Current instantaneous power draw, in the same abstract units as
    /// ShipSim.BatteryCapacity — mutable and script-owned like Condition, but a genuinely
    /// separate concept (this is "how much power right now," not "how charged/worn is this").
    /// Zero by default: a relay (conduit/switch) or the battery/source itself never draws, so only
    /// the consumer types that actually need a nonzero value ever set one.</summary>
    public float PowerDraw { get; set; }
}

public enum FixtureSurface
{
    FloorTop,
    FloorUnderside,
    WallInner,
    WallOuter,
    CeilingUnderside,
}

public sealed class ConduitFixture(string id, CellCoord tile, FixtureSurface surface)
    : Fixture(id, tile, surface);

public sealed class SwitchFixture(string id, CellCoord tile, FixtureSurface surface)
    : Fixture(id, tile, surface)
{
    public bool IsOpen { get; set; }
}

public sealed class MachineFixture(string id, CellCoord tile, FixtureSurface surface)
    : Fixture(id, tile, surface);

/// <summary>
/// A ship's finite power source. Reuses the base <see cref="Fixture.Condition"/> float as its
/// charge fraction (0 = empty, 1 = full) rather than adding a dedicated field — the same way
/// <see cref="ConduitFixture"/> already overloads Condition to mean "repair state."
/// </summary>
public sealed class BatteryFixture(string id, CellCoord tile, FixtureSurface surface)
    : Fixture(id, tile, surface);

/// <summary>
/// A player-installable ship engine block. Same "Condition overloaded as charge, not wear" idea
/// as <see cref="BatteryFixture"/> — here it's the block's own internal N2 tank fraction, drained
/// during travel rather than by passive wear. Unlike Battery there can be many of these on a ship
/// at once, one per installed thruster (see ShipBuildTarget's own per-edge tracking).
/// </summary>
public sealed class ThrusterFixture(string id, CellCoord tile, FixtureSurface surface)
    : Fixture(id, tile, surface);

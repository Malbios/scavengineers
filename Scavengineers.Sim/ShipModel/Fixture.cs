using Scavengineers.Sim.Grid;

namespace Scavengineers.Sim.ShipModel;

/// <summary>Tier-2 placement: surface-attached, never affects airtightness. Tracks which tile a
/// fixture is on, not its free sub-tile position — that's a presentation concern, not something
/// the sim layer needs yet.</summary>
public abstract class Fixture(string id, CellCoord tile, FixtureSurface surface)
{
    public string Id { get; } = id;

    public CellCoord Tile { get; } = tile;

    public FixtureSurface Surface { get; } = surface;

    public float Condition { get; set; } = 1.0f;

    /// <summary>Current instantaneous power draw, in the same abstract units as
    /// ShipSim.BatteryCapacity — a genuinely separate concept from Condition. Zero by default: a
    /// relay (conduit/switch) or the battery/source itself never draws.</summary>
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

/// <summary>A ship's finite power source. Reuses the base <see cref="Fixture.Condition"/> float
/// as its charge fraction (0 = empty, 1 = full) rather than adding a dedicated field.</summary>
public sealed class BatteryFixture(string id, CellCoord tile, FixtureSurface surface)
    : Fixture(id, tile, surface);

/// <summary>A player-installable ship engine block. Same "Condition overloaded as charge, not
/// wear" idea as <see cref="BatteryFixture"/> — here it's the block's internal N2 tank fraction,
/// drained during travel rather than by passive wear. Unlike Battery there can be many of these
/// on a ship at once, one per installed thruster.</summary>
public sealed class ThrusterFixture(string id, CellCoord tile, FixtureSurface surface)
    : Fixture(id, tile, surface);

/// <summary>A player-installable shelf/bin — a pure marker, same shape as
/// <see cref="ThrusterFixture"/>. Draws no power and has no charge/wear concept; it exists here
/// purely so it's discoverable via Deck.Fixtures like every other wall-mounted machine.</summary>
public sealed class StorageFixture(string id, CellCoord tile, FixtureSurface surface)
    : Fixture(id, tile, surface);

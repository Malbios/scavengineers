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

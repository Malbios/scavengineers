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

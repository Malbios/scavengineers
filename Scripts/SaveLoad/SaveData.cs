using System.Collections.Generic;

using Scavengineers.Scripts.Inventory;

namespace Scavengineers.Scripts.SaveLoad;

public sealed class SaveData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public PlayerSaveData Player { get; set; } = new();

    public Dictionary<string, bool> ObjectStates { get; set; } = new();

    public Dictionary<string, BuildTargetSaveData> BuildTargets { get; set; } = new();

    public Dictionary<string, string> ObjectStringStates { get; set; } = new();

    /// <summary>Full backpacks (or other containers, later) sitting loose in the world — not
    /// tied to any ShipBuildTarget, so none of the lists above fit. Scanned/respawned by
    /// SaveManager via the "dropped_container" group (see ContainerPickupItem).</summary>
    public List<DroppedContainerSaveData> DroppedContainers { get; set; } = new();
}

/// <summary>A ContainerPickupItem's whole world state — position plus its own contents, since
/// it's a loose, dynamically-spawned object rather than a fixed scene node.</summary>
public sealed class DroppedContainerSaveData
{
    public float PosX { get; set; }

    public float PosY { get; set; }

    public float PosZ { get; set; }

    public string ItemId { get; set; } = "";

    public Dictionary<string, int> Contents { get; set; } = new();
}

/// <summary>Everything a ShipBuildTarget places dynamically — no fixed scene node to hang the
/// simpler bool-only ISaveable off, so this captures its whole placed-tile/edge state instead.
/// Plain DTOs, deliberately decoupled from Scavengineers.Sim.Grid.CellCoord (docs/architecture/
/// save-schema.md's own "per-tile / per-edge" framing for the save format).</summary>
public sealed class BuildTargetSaveData
{
    public List<TileCoord> Conduits { get; set; } = new();

    /// <summary>Wall-mounted conduits — same tile-addressed fixture as <see cref="Conduits"/>,
    /// just rendered against a specific wall face instead of the floor, so which neighbor cell
    /// (i.e. which of up to 4 walls) it's mounted against is saved too (needed to reconstruct
    /// the right visual on load, and a tile can carry more than one of these at once).</summary>
    public List<WallConduitCoord> WallConduits { get; set; } = new();

    /// <summary>Covers both player-built interior partitions and repaired hull breaches — both
    /// already land in ShipBuildTarget's own _placedWalls dictionary today.</summary>
    public List<EdgeCoord> Walls { get; set; } = new();

    /// <summary>Tiles whose floor/ceiling panel is currently missing (breached) — the ship's
    /// default layout starts with none, so these only ever record player-scrapped tiles.</summary>
    public List<TileCoord> FloorBreaches { get; set; } = new();

    public List<TileCoord> CeilingBreaches { get; set; } = new();

    /// <summary>Battery/Switch/RechargeStation — at most one of each Type ("battery"/"switch"/
    /// "recharge_station") at a time. Purely additive: defaults to empty for any save predating
    /// these being player-install/uninstall-able rather than fixed scene nodes.</summary>
    public List<MachineCoord> Machines { get; set; } = new();

    /// <summary>Cells added beyond the ship's default footprint via the Extend Floor verb —
    /// distinct from every other list here, which all describe state *on* an existing cell.
    /// Replayed first (see ShipBuildTarget.ApplyBuildState) since Walls/FloorBreaches/
    /// CeilingBreaches entries may reference one of these cells. Purely additive: empty for any
    /// save predating dynamic ship expansion.</summary>
    public List<TileCoord> ExtendedCells { get; set; } = new();
}

public readonly record struct TileCoord(int X, int Y);

public readonly record struct EdgeCoord(int AX, int AY, int BX, int BY);

/// <summary>Slot defaults to 1 (today's top height, 2 slots per wall) so a save written before
/// wall conduits had height slots keeps its wire at the same visual height it was saved at.</summary>
public readonly record struct WallConduitCoord(int TileX, int TileY, int NeighborX, int NeighborY, int Slot = 1);

/// <summary>State is each machine's own extra save data, stringified (Battery's charge fraction,
/// Switch's on/off bool) — null for a stateless machine (Recharge Station). Applied directly to
/// the freshly spawned instance on load, not through the generic "saveable" group scan (a
/// dynamically spawned machine is never in that group).</summary>
public readonly record struct MachineCoord(string Type, int EdgeAX, int EdgeAY, int EdgeBX, int EdgeBY, string? State);

public sealed class PlayerSaveData
{
    public float PosX { get; set; }

    public float PosY { get; set; }

    public float PosZ { get; set; }

    public float Yaw { get; set; }

    public float Pitch { get; set; }

    public float O2Percent { get; set; }

    /// <summary>Defaulted to 100f — an old save predating Health has no way to have died, so it
    /// loads as fully healthy, same "missing field = safe default" convention as HungerPercent
    /// etc. below.</summary>
    public float HealthPercent { get; set; } = 100f;

    /// <summary>Defaulted to 100f, not 0 — an old save missing this field (System.Text.Json
    /// only overwrites properties actually present in the JSON) loads as "fully rested" rather
    /// than instantly starving.</summary>
    public float HungerPercent { get; set; } = 100f;

    public float ThirstPercent { get; set; } = 100f;

    public float EnergyPercent { get; set; } = 100f;

    public Dictionary<string, int> Inventory { get; set; } = new();

    public int Credits { get; set; }

    /// <summary>Null when no backpack is worn — the body's own Inventory dict above stays
    /// backpack-contents-free either way (see Player.CapturePlayerState/ApplyPlayerState).</summary>
    public string? BackpackItemId { get; set; }

    public Dictionary<string, int> BackpackContents { get; set; } = new();

    /// <summary>Defaulted to the normal backpack's own size — an old save missing this field
    /// (predates the debug backpack's different slot count) reconstructs at the size every
    /// backpack used to always be. Captured from the actual worn container's own slot count at
    /// save time (Player.CapturePlayerState), not assumed, so a debug backpack's real 24 slots
    /// round-trip correctly instead of being silently truncated back to 8.</summary>
    public int BackpackSlotCount { get; set; } = PlayerInventory.BackpackSlotCount;

    /// <summary>False for a save from before the power drill existed — distinct from
    /// DrillHasBattery/DrillCharge's own JSON-missing defaults (false/0f) so an old save
    /// doesn't get misread as "owns an empty drill" (see Player.ApplyPlayerState).</summary>
    public bool HasDrill { get; set; }

    public bool DrillHasBattery { get; set; }

    public float DrillCharge { get; set; }

    /// <summary>False for a save from before the flashlight had its own battery — same
    /// HasDrill/DrillHasBattery/DrillCharge shape and reasoning.</summary>
    public bool HasFlashlight { get; set; }

    public bool FlashlightHasBattery { get; set; }

    public float FlashlightCharge { get; set; }
}

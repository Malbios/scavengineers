using System.Collections.Generic;

namespace Scavengineers.Scripts.SaveLoad;

public sealed class SaveData
{
    public int Version { get; set; } = SaveManager.CurrentVersion;

    public PlayerSaveData Player { get; set; } = new();

    public Dictionary<string, bool> ObjectStates { get; set; } = new();

    public Dictionary<string, BuildTargetSaveData> BuildTargets { get; set; } = new();
}

/// <summary>Everything a ShipBuildTarget places dynamically — no fixed scene node to hang the
/// simpler bool-only ISaveable off, so this captures its whole placed-tile/edge state instead.
/// Plain DTOs, deliberately decoupled from Scavengineers.Sim.Grid.CellCoord (docs/architecture/
/// save-schema.md's own "per-tile / per-edge" framing for the save format).</summary>
public sealed class BuildTargetSaveData
{
    public List<TileCoord> Conduits { get; set; } = new();

    /// <summary>Covers both player-built interior partitions and repaired hull breaches — both
    /// already land in ShipBuildTarget's own _placedWalls dictionary today.</summary>
    public List<EdgeCoord> Walls { get; set; } = new();
}

public readonly record struct TileCoord(int X, int Y);

public readonly record struct EdgeCoord(int AX, int AY, int BX, int BY);

public sealed class PlayerSaveData
{
    public float PosX { get; set; }

    public float PosY { get; set; }

    public float PosZ { get; set; }

    public float Yaw { get; set; }

    public float Pitch { get; set; }

    public float O2Percent { get; set; }

    public float PowerPercent { get; set; }

    public Dictionary<string, int> Inventory { get; set; } = new();
}

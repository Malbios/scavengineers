using System.Collections.Generic;

namespace Scavengineers.Scripts.SaveLoad;

public sealed class SaveData
{
    public int Version { get; set; } = SaveManager.CurrentVersion;

    public PlayerSaveData Player { get; set; } = new();

    public Dictionary<string, bool> ObjectStates { get; set; } = new();
}

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

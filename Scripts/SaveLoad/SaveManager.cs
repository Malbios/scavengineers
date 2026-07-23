using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Godot;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Verbs;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.SaveLoad;

/// <summary>F5 saves, F9 loads. Kept as its own node/concern rather than folded into Player's
/// input handling.</summary>
public partial class SaveManager : Node
{
    /// <summary>Shared default, also used directly by ShipSim.ProcedurallyGenerate to read its
    /// own seed off disk. A ShipSim has no reference to this node — the path itself is the only
    /// thing that needs to be shared.</summary>
    public static readonly string DefaultSavePath = ProjectSettings.GlobalizePath("user://savegame.json");

    /// <summary>Instance property, not a static constant, so a test can point this at a temp file
    /// instead of the player's real save data.</summary>
    public string SavePath { get; set; } = DefaultSavePath;

    /// <summary>Placeholder/tunable — how often autosave fires. Instance property, not a
    /// constant, so a test can dial it down to a much shorter interval before this node enters
    /// the tree.</summary>
    public float AutosaveIntervalSeconds { get; set; } = 300f;

    [Export]
    public PlayerScript? PlayerRef { get; set; }

    private PlayerScript? _player;
    private Timer? _autosaveTimer;

    public override void _Ready()
    {
        _player = PlayerRef;

        _autosaveTimer = new Timer { WaitTime = AutosaveIntervalSeconds, Autostart = true };
        AddChild(_autosaveTimer);
        _autosaveTimer.Timeout += Save;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Keycode: Key.F5, Pressed: true })
        {
            Save();
        }
        else if (@event is InputEventKey { Keycode: Key.F9, Pressed: true })
        {
            Load();
        }
    }

    public void Save()
    {
        if (_player?.IsAwaitingDeathChoice == true)
        {
            GD.Print("[SaveManager] Skipping save — death screen awaiting a choice.");
            return;
        }

        var data = new SaveData
        {
            Version = SaveData.CurrentVersion,
            Player = _player!.CapturePlayerState(),
        };

        foreach (var saveable in FindSaveables<ISaveable>())
        {
            data.ObjectStates[saveable.SaveId] = saveable.GetSaveState();
        }

        foreach (var buildSaveable in FindSaveables<IBuildTargetSaveable>())
        {
            data.BuildTargets[buildSaveable.SaveId] = buildSaveable.CaptureBuildState();
        }

        foreach (var stateSaveable in FindSaveables<IStateSaveable>())
        {
            data.ObjectStringStates[stateSaveable.SaveId] = stateSaveable.GetSaveState();
        }

        foreach (var shipLayoutSaveable in FindSaveables<IShipLayoutSaveable>())
        {
            if (shipLayoutSaveable.LayoutSeed is { } seed)
            {
                data.ShipLayoutSeeds[shipLayoutSaveable.SaveId] = seed;
            }
        }

        foreach (var shipStateSaveable in FindSaveables<IShipStateSaveable>())
        {
            // An unnamed ship is skipped rather than colliding on the empty-string key with every
            // other unnamed one — which would silently give them all the last one's atmosphere.
            if (!string.IsNullOrEmpty(shipStateSaveable.SaveId))
            {
                data.Ships[shipStateSaveable.SaveId] = shipStateSaveable.CaptureShipState();
            }
        }

        foreach (var node in GetTree().GetNodesInGroup("dropped_container"))
        {
            if (node is not ContainerPickupItem { Contents: { } contents } container)
            {
                continue;
            }

            data.DroppedContainers.Add(new DroppedContainerSaveData
            {
                PosX = container.GlobalPosition.X,
                PosY = container.GlobalPosition.Y,
                PosZ = container.GlobalPosition.Z,
                ItemId = container.ItemId,
                Slots = SlotSaveDataConverter.Capture(contents),
            });
        }

        foreach (var node in GetTree().GetNodesInGroup("mission_item"))
        {
            if (node is not PickupItem { MissionOwnerSaveId: not "" } item)
            {
                continue;
            }

            data.MissionItems.Add(new MissionItemSaveData
            {
                PosX = item.GlobalPosition.X,
                PosY = item.GlobalPosition.Y,
                PosZ = item.GlobalPosition.Z,
                ItemId = item.ItemId,
                Count = item.Count,
                Charge = item.Charge,
                OwnerBuildTargetSaveId = item.MissionOwnerSaveId,
            });
        }

        File.WriteAllText(SavePath, JsonSerializer.Serialize(data));
        GD.Print($"[SaveManager] Saved to {SavePath}");
        _player?.ShowSavedFlash();
    }

    /// <summary>Returns whether a save was actually applied — false for a missing or unreadable
    /// file, so callers like Player.Die() can fall back instead of retrying a load that will
    /// never succeed.</summary>
    public bool Load()
    {
        if (!File.Exists(SavePath))
        {
            GD.Print("[SaveManager] No save file found — nothing to load.");
            return false;
        }

        SaveData? data;
        try
        {
            data = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(SavePath));
        }
        catch (Exception e)
        {
            GD.PushWarning($"[SaveManager] Save file could not be read: {e.Message}");
            return false;
        }

        if (data is null)
        {
            GD.PushWarning("[SaveManager] Save file was empty or invalid.");
            return false;
        }

        if (data.Version != SaveData.CurrentVersion)
        {
            // No migrations exist yet (only v1). This is the dispatch point for them once a
            // v2 exists — for now, proceed best-effort rather than refusing to load.
            GD.PushWarning($"[SaveManager] Save version {data.Version} != current {SaveData.CurrentVersion} (no migration available yet).");
        }

        _player!.ApplyPlayerState(data.Player);

        var saveables = FindSaveables<ISaveable>();
        var liveIds = new HashSet<string>();
        foreach (var saveable in saveables)
        {
            liveIds.Add(saveable.SaveId);
            if (data.ObjectStates.TryGetValue(saveable.SaveId, out var state))
            {
                saveable.ApplySaveState(state);
            }
        }

        foreach (var savedId in data.ObjectStates.Keys)
        {
            if (!liveIds.Contains(savedId))
            {
                GD.PushWarning($"[SaveManager] Save references unknown object id '{savedId}' — skipping.");
            }
        }

        foreach (var buildSaveable in FindSaveables<IBuildTargetSaveable>())
        {
            if (data.BuildTargets.TryGetValue(buildSaveable.SaveId, out var buildState))
            {
                buildSaveable.ApplyBuildState(buildState);
            }
        }

        foreach (var stateSaveable in FindSaveables<IStateSaveable>())
        {
            if (data.ObjectStringStates.TryGetValue(stateSaveable.SaveId, out var state))
            {
                stateSaveable.ApplySaveState(state);
            }
        }

        // After the build targets above, deliberately: ApplyBuildState reconstructs walls and
        // breaches, which is what decides whether a room is sealed or open to vacuum. Restoring the
        // air first would leave the very next tick re-venting rooms whose breach the save had
        // already repaired.
        foreach (var shipStateSaveable in FindSaveables<IShipStateSaveable>())
        {
            if (data.Ships.TryGetValue(shipStateSaveable.SaveId, out var shipState))
            {
                shipStateSaveable.ApplyShipState(shipState);
            }
        }

        // Loose world objects, not tied to any owner node's ApplyBuildState — cleared and
        // respawned wholesale here instead.
        foreach (var node in GetTree().GetNodesInGroup("dropped_container"))
        {
            node.QueueFree();
        }

        foreach (var dropped in data.DroppedContainers)
        {
            var contents = new SlotContainer(PlayerInventory.BackpackSlotCount);
            if (dropped.Slots.Count > 0)
            {
                SlotSaveDataConverter.Restore(contents, dropped.Slots);
            }
            else
            {
                // Legacy save predating per-slot state (see DroppedContainerSaveData.Slots).
                foreach (var (itemId, count) in dropped.Contents)
                {
                    contents.Add(itemId, count);
                }
            }

            _player!.SpawnDroppedContainer(dropped.ItemId, contents, new Vector3(dropped.PosX, dropped.PosY, dropped.PosZ));
        }

        foreach (var node in GetTree().GetNodesInGroup("mission_item"))
        {
            node.QueueFree();
        }

        var buildTargetsBySaveId = GetTree().GetNodesInGroup("saveable").OfType<ShipBuildTarget>().ToDictionary(t => t.SaveId);
        foreach (var missionItem in data.MissionItems)
        {
            if (buildTargetsBySaveId.TryGetValue(missionItem.OwnerBuildTargetSaveId, out var owner))
            {
                owner.PlaceMissionItem(missionItem.ItemId, missionItem.Count, missionItem.Charge, new Vector3(missionItem.PosX, missionItem.PosY, missionItem.PosZ));
            }
        }

        GD.Print("[SaveManager] Loaded.");
        return true;
    }

    /// <summary>Reads just the ShipLayoutSeeds dictionary off disk, synchronously — called
    /// directly by ShipSim.ProcedurallyGenerate at the top of its own _Ready(), before this (or
    /// any) SaveManager instance necessarily exists. Null for a missing/unreadable file — a
    /// caller finding no seed for its own SaveId (or this returning null entirely) both correctly
    /// mean "roll a fresh one."</summary>
    public static Dictionary<string, int>? TryReadShipLayoutSeeds(string savePath)
    {
        if (!File.Exists(savePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SaveData>(File.ReadAllText(savePath))?.ShipLayoutSeeds;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private List<T> FindSaveables<T>() where T : class =>
        GetTree().GetNodesInGroup("saveable").OfType<T>().ToList();
}

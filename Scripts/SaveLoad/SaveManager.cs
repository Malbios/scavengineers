using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Godot;
using Scavengineers.Scripts.Inventory;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.SaveLoad;

/// <summary>
/// F5 saves, F9 loads. Kept as its own node/concern rather than folded into Player's input
/// handling. Scoped to GreyboxRoom (the real demo scene) — the throwaway FloatSpike doesn't
/// need this.
/// </summary>
public partial class SaveManager : Node
{
    /// <summary>Shared default, also used directly by ShipSim.ProcedurallyGenerate to read its
    /// own seed off disk (see IShipLayoutSaveable's own doc comment for why that read can't go
    /// through the normal ApplySaveState callback). A ShipSim has no reference to this node — the
    /// path itself is the only thing that needs to be shared, not a NodePath wiring.</summary>
    public static readonly string DefaultSavePath = ProjectSettings.GlobalizePath("user://savegame.json");

    /// <summary>Instance property (not a static constant) so a test can point this at a temp file
    /// instead of the player's real save data — every production caller just uses the default.</summary>
    public string SavePath { get; set; } = DefaultSavePath;

    /// <summary>Placeholder/tunable — how often autosave fires. Instance property, not a
    /// constant, for the same reason SavePath already is one: a test needs to point this at a
    /// much shorter interval than real play ever would, set before this node enters the tree.</summary>
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

        File.WriteAllText(SavePath, JsonSerializer.Serialize(data));
        GD.Print($"[SaveManager] Saved to {SavePath}");
        _player?.ShowSavedFlash();
    }

    /// <summary>Returns whether a save was actually applied — false (no-op, logged) for a missing
    /// or unreadable file, so callers like Player.Die() can fall back instead of retrying a load
    /// that will never succeed.</summary>
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

        // Loose world objects, not tied to any owner node's own ApplyBuildState — cleared and
        // respawned wholesale here instead, same "clear then reapply" shape as ShipBuildTarget's
        // ClearAllBuildState/ApplyBuildState uses for its own dynamically-placed machines.
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

        GD.Print("[SaveManager] Loaded.");
        return true;
    }

    /// <summary>Reads just the ShipLayoutSeeds dictionary off disk, synchronously — called
    /// directly by ShipSim.ProcedurallyGenerate at the very top of its own _Ready(), before this
    /// (or any) SaveManager instance necessarily exists yet. Null for a missing/unreadable file,
    /// same "can't load, fall back" shape as <see cref="Load"/>'s own file-read guard — a
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

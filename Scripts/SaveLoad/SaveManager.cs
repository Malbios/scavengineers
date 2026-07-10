using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.SaveLoad;

/// <summary>
/// F5 saves, F9 loads. Kept as its own node/concern rather than folded into Player's input
/// handling. Scoped to GreyboxRoom (the real demo scene) — the throwaway FloatSpike doesn't
/// need this.
/// </summary>
public partial class SaveManager : Node
{
    public const int CurrentVersion = 1;

    private static readonly string SavePath = ProjectSettings.GlobalizePath("user://savegame.json");

    [Export]
    public PlayerScript? PlayerRef { get; set; }

    private PlayerScript? _player;

    public override void _Ready()
    {
        _player = PlayerRef;
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
            Version = CurrentVersion,
            Player = _player!.CapturePlayerState(),
        };

        foreach (var saveable in FindSaveables())
        {
            data.ObjectStates[saveable.SaveId] = saveable.GetSaveState();
        }

        foreach (var buildSaveable in FindBuildTargetSaveables())
        {
            data.BuildTargets[buildSaveable.SaveId] = buildSaveable.CaptureBuildState();
        }

        File.WriteAllText(SavePath, JsonSerializer.Serialize(data));
        GD.Print($"[SaveManager] Saved to {SavePath}");
    }

    public void Load()
    {
        if (!File.Exists(SavePath))
        {
            GD.Print("[SaveManager] No save file found — nothing to load.");
            return;
        }

        SaveData? data;
        try
        {
            data = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(SavePath));
        }
        catch (Exception e)
        {
            GD.PushWarning($"[SaveManager] Save file could not be read: {e.Message}");
            return;
        }

        if (data is null)
        {
            GD.PushWarning("[SaveManager] Save file was empty or invalid.");
            return;
        }

        if (data.Version != CurrentVersion)
        {
            // No migrations exist yet (only v1). This is the dispatch point for them once a
            // v2 exists — for now, proceed best-effort rather than refusing to load.
            GD.PushWarning($"[SaveManager] Save version {data.Version} != current {CurrentVersion} (no migration available yet).");
        }

        _player!.ApplyPlayerState(data.Player);

        var saveables = FindSaveables();
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

        foreach (var buildSaveable in FindBuildTargetSaveables())
        {
            if (data.BuildTargets.TryGetValue(buildSaveable.SaveId, out var buildState))
            {
                buildSaveable.ApplyBuildState(buildState);
            }
        }

        GD.Print("[SaveManager] Loaded.");
    }

    private List<ISaveable> FindSaveables()
    {
        var result = new List<ISaveable>();
        foreach (var node in GetTree().GetNodesInGroup("saveable"))
        {
            if (node is ISaveable saveable)
            {
                result.Add(saveable);
            }
        }

        return result;
    }

    private List<IBuildTargetSaveable> FindBuildTargetSaveables()
    {
        var result = new List<IBuildTargetSaveable>();
        foreach (var node in GetTree().GetNodesInGroup("saveable"))
        {
            if (node is IBuildTargetSaveable buildSaveable)
            {
                result.Add(buildSaveable);
            }
        }

        return result;
    }
}

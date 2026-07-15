using System.Collections.Generic;

using Godot;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Travel;

/// <summary>The travel console's map screen — one Button per destination, spawned fresh into
/// <see cref="MapArea"/> at each entry's own map position every time <see cref="Populate"/> runs,
/// rather than requiring one hand-placed, individually-wired scene node per destination. Lives
/// under Player.tscn's HUD CanvasLayer like every other piece of UI in this project (see
/// InventoryPanel/DrillWindow/etc.) — not a separately instanced scene.</summary>
public partial class TravelMapPanel : PanelContainer
{
    [Export]
    public Control? MapArea { get; set; }

    [Export]
    public Label? SelectionLabel { get; set; }

    [Export]
    public Button? TravelButton { get; set; }

    [Export]
    public Button? CancelButton { get; set; }

    /// <summary>Set by Player._Ready, same self-addressing shape InventorySlotUI.PlayerRef
    /// already uses — lets this panel call back into Player without Player needing to reach in
    /// and manage this panel's own button-click plumbing.</summary>
    public PlayerScript? PlayerRef { get; set; }

    // Placeholder/tunable — tints the selected destination's icon so it's visually obvious at a
    // glance which one you've picked, not just inferable from the (generic) selection label text.
    private static readonly Color SelectedColor = Colors.Gold;

    private int? _selectedId;
    private readonly Dictionary<int, Button> _spawnedIcons = new();

    public override void _Ready()
    {
        // Reuses the existing VERB_TRAVEL key rather than a duplicate one, matching the reuse
        // precedent AirlockDoorVerbTarget's own PryVerb sets with VERB_PRY_DOOR.
        TravelButton!.Text = Tr("VERB_TRAVEL");
        CancelButton!.Text = Tr("HUD_TRAVEL_MAP_CANCEL");

        TravelButton.Pressed += OnTravelPressed;
        CancelButton.Pressed += () => PlayerRef?.CloseTravelMap();
    }

    /// <summary>Rebuilds the map's icons from scratch — cheap enough at 6 destinations, and
    /// avoids tracking incremental diffs against whatever the console reported last time.</summary>
    public void Populate(IReadOnlyList<TravelMapEntry> entries, int currentId)
    {
        foreach (var icon in _spawnedIcons.Values)
        {
            icon.QueueFree();
        }

        _spawnedIcons.Clear();
        _selectedId = null;

        foreach (var entry in entries)
        {
            var button = new Button
            {
                Text = Tr(entry.DisplayNameKey),
                Position = entry.MapPosition,
                Disabled = entry.IsCurrent,
            };

            var id = entry.DestinationId;
            button.Pressed += () => OnDestinationPressed(id);
            MapArea!.AddChild(button);
            _spawnedIcons[id] = button;
        }

        UpdateSelectionUi();
    }

    private void OnDestinationPressed(int id)
    {
        _selectedId = id;
        UpdateSelectionUi();
    }

    private void OnTravelPressed()
    {
        if (_selectedId is { } id)
        {
            PlayerRef?.ConfirmTravel(id);
        }
    }

    /// <summary>The selected icon's own gold tint is the "which one is selected" signal now —
    /// the label just prompts before anything's picked, and goes quiet once it is, rather than
    /// repeating what the icon color already shows.</summary>
    private void UpdateSelectionUi()
    {
        TravelButton!.Disabled = _selectedId is null;

        foreach (var (id, icon) in _spawnedIcons)
        {
            icon.Modulate = id == _selectedId ? SelectedColor : Colors.White;
        }

        SelectionLabel!.Text = _selectedId is null ? Tr("HUD_TRAVEL_MAP_PROMPT") : "";
    }
}

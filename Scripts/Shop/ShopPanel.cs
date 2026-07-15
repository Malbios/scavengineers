using System;
using System.Collections.Generic;

using Godot;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Shop;

/// <summary>The station trade console's shop screen — one scrollable list of rows per tab (Buy/
/// Sell), spawned fresh into each tab's own list container every time <see cref="Populate"/> runs,
/// same rebuild-from-scratch shape TravelMapPanel already uses for its destination icons. Lives
/// under Player.tscn's HUD CanvasLayer like every other piece of UI in this project. Unlike the
/// travel map, this panel doesn't close after a transaction — a shop visit is naturally many
/// clicks, not a one-shot confirmation — so every row button re-populates both lists immediately
/// after acting (see Player.BuyItem/SellItem).</summary>
public partial class ShopPanel : PanelContainer
{
    [Export]
    public Control? BuyList { get; set; }

    [Export]
    public Control? SellList { get; set; }

    [Export]
    public Button? CloseButton { get; set; }

    /// <summary>Set by Player._Ready, same self-addressing shape InventorySlotUI.PlayerRef and
    /// TravelMapPanel.PlayerRef already use.</summary>
    public PlayerScript? PlayerRef { get; set; }

    private readonly List<Button> _buyRows = new();
    private readonly List<Button> _sellRows = new();

    public override void _Ready()
    {
        CloseButton!.Text = Tr("HUD_SHOP_CLOSE");
        CloseButton.Pressed += () => PlayerRef?.CloseShop();
    }

    public void Populate(IReadOnlyList<ShopEntry> buyEntries, IReadOnlyList<ShopEntry> sellEntries)
    {
        Rebuild(BuyList!, _buyRows, buyEntries, id => PlayerRef?.BuyItem(id));
        Rebuild(SellList!, _sellRows, sellEntries, id => PlayerRef?.SellItem(id));
    }

    private void Rebuild(Control list, List<Button> spawned, IReadOnlyList<ShopEntry> entries, Action<string> onPressed)
    {
        foreach (var row in spawned)
        {
            row.QueueFree();
        }

        spawned.Clear();

        foreach (var entry in entries)
        {
            var button = new Button
            {
                Text = $"{Tr(entry.DisplayNameKey)} ({entry.Price}cr)",
                Disabled = entry.Disabled,
            };

            var id = entry.ItemId;
            button.Pressed += () => onPressed(id);
            list.AddChild(button);
            spawned.Add(button);
        }
    }
}

using System;
using System.Collections.Generic;

using Godot;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Contracts;

/// <summary>The contract-giver's board screen — one scrollable list of rows per tab (Available/
/// Active), spawned fresh into each tab's own list container every time <see cref="Populate"/>
/// runs. Doesn't close after an action — accepting/turning in a contract just re-populates both
/// lists (see Player.RefreshContractBoard).</summary>
public partial class ContractBoardPanel : PanelContainer
{
    [Export]
    public Control? AvailableList { get; set; }

    [Export]
    public Control? ActiveList { get; set; }

    [Export]
    public Button? CloseButton { get; set; }

    public PlayerScript? PlayerRef { get; set; }

    private readonly List<Button> _availableRows = new();
    private readonly List<Button> _activeRows = new();

    public override void _Ready()
    {
        CloseButton!.Text = Tr("HUD_SHOP_CLOSE");
        CloseButton.Pressed += () => PlayerRef?.CloseContractBoard();
    }

    public void Populate(IReadOnlyList<ContractBoardEntry> available, IReadOnlyList<ContractBoardEntry> active)
    {
        Rebuild(AvailableList!, _availableRows, available, id => PlayerRef?.AcceptContract(id));
        Rebuild(ActiveList!, _activeRows, active, id => PlayerRef?.TryTurnInContract(id));
    }

    private static void Rebuild(Control list, List<Button> spawned, IReadOnlyList<ContractBoardEntry> entries, Action<string> onPressed)
    {
        foreach (var row in spawned)
        {
            row.QueueFree();
        }

        spawned.Clear();

        foreach (var entry in entries)
        {
            var button = new Button { Text = entry.Text, Disabled = !entry.ActionAvailable };
            var id = entry.InstanceId;
            button.Pressed += () => onPressed(id);
            list.AddChild(button);
            spawned.Add(button);
        }
    }
}

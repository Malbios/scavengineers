using Godot;

namespace Scavengineers.Scripts.Player;

/// <summary>The "you died" screen — Player.Die() shows this instead of reloading immediately,
/// same shape as ShopPanel/TravelMapPanel (Player owns a reference, the panel calls back into
/// Player on button press). Deliberately only two choices, no dismiss-without-choosing route.</summary>
public partial class DeathPanel : PanelContainer
{
    [Export]
    public Button? ReloadButton { get; set; }

    [Export]
    public Button? QuitButton { get; set; }

    /// <summary>Set by Player._Ready, same self-addressing shape InventorySlotUI.PlayerRef and
    /// ShopPanel/TravelMapPanel.PlayerRef already use.</summary>
    public Player? PlayerRef { get; set; }

    public override void _Ready()
    {
        ReloadButton!.Text = Tr("HUD_DEATH_RELOAD");
        QuitButton!.Text = Tr("HUD_DEATH_QUIT");

        ReloadButton.Pressed += () => PlayerRef?.ReloadAfterDeath();
        QuitButton.Pressed += () => PlayerRef?.QuitAfterDeath();
    }
}

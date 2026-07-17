using System.Collections.Generic;
using System.Linq;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Shop;
using PlayerScript = Scavengineers.Scripts.Player.Player;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the shop's Buy/Sell entry-building and transaction methods.
/// All four depend on resolving the player via the "player" group (same lookup
/// ContainerPickupItem.GetPlayer already uses elsewhere), which needs a real PlayerScript
/// instance — Credits/Inventory are plain field-initialized state, unaffected by _Ready(), so a
/// minimal subclass that overrides _Ready() to skip the real one's dozens of HUD GetNode calls
/// (and its debug stipend) gives a clean, fully isolated Player without loading Player.tscn — this
/// test project (Scavengineers.NodeTests) is its own separate Godot project with no Scenes/ of
/// its own, so the real scene isn't even loadable here.
/// wall_panel (10cr Buy / 4cr Sell) is used as a "known-empty, not part of any stipend" item
/// throughout, since the test player's inventory starts genuinely empty.</summary>
[TestSuite]
public partial class VendorVerbTargetTest
{
    private partial class TestPlayer : PlayerScript
    {
        public override void _Ready()
        {
            AddToGroup("player");
        }
    }

    private static (VendorVerbTarget Vendor, PlayerScript Player) CreateVendorWithPlayer(SceneTree sceneTree)
    {
        var player = AutoFree(new TestPlayer());
        sceneTree.Root.AddChild(player);

        var vendor = AutoFree(new VendorVerbTarget());
        sceneTree.Root.AddChild(vendor);

        return (vendor, player);
    }

    private static ShopEntry FindEntry(IReadOnlyList<ShopEntry> entries, string itemId) => entries.First(e => e.ItemId == itemId);

    [TestCase]
    [RequireGodotRuntime]
    public void BuildBuyEntries_DisabledWhenUnaffordable()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, _) = CreateVendorWithPlayer(sceneTree);

        // battery costs 40cr — well above the starting stipend.
        var battery = FindEntry(vendor.BuildBuyEntries(), "battery");

        AssertBool(battery.Disabled).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BuildBuyEntries_EnabledWhenAffordableWithRoom()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, _) = CreateVendorWithPlayer(sceneTree);

        var wallPanel = FindEntry(vendor.BuildBuyEntries(), "wall_panel");

        AssertBool(wallPanel.Disabled).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BuildSellEntries_DisabledWhenNotOwned()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, _) = CreateVendorWithPlayer(sceneTree);

        var wallPanel = FindEntry(vendor.BuildSellEntries(), "wall_panel");

        AssertBool(wallPanel.Disabled).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BuildSellEntries_EnabledWhenOwned()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, player) = CreateVendorWithPlayer(sceneTree);
        player.Inventory.Add("scrap_metal", 1);

        var scrapMetal = FindEntry(vendor.BuildSellEntries(), "scrap_metal");

        AssertBool(scrapMetal.Disabled).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryBuy_SpendsCreditsAndAddsItem()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, player) = CreateVendorWithPlayer(sceneTree);
        var creditsBefore = player.Credits;

        var bought = vendor.TryBuy("wall_panel");

        AssertBool(bought).IsTrue();
        AssertBool(player.Credits == creditsBefore - 10).IsTrue();
        AssertBool(player.Inventory.Has("wall_panel", 1)).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryBuy_FailsCleanlyWhenUnaffordable()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, player) = CreateVendorWithPlayer(sceneTree);
        player.TrySpendCredits(player.Credits); // drain to 0

        var bought = vendor.TryBuy("wall_panel");

        AssertBool(bought).IsFalse();
        AssertBool(player.Credits == 0).IsTrue();
        AssertBool(player.Inventory.Has("wall_panel", 1)).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TrySell_GrantsCreditsAndRemovesItem()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, player) = CreateVendorWithPlayer(sceneTree);
        player.Inventory.Add("scrap_metal", 1);
        var creditsBefore = player.Credits;

        var sold = vendor.TrySell("scrap_metal");

        AssertBool(sold).IsTrue();
        AssertBool(player.Credits == creditsBefore + 2).IsTrue();
        AssertBool(player.Inventory.Has("scrap_metal", 1)).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TrySell_FailsCleanlyWhenNotOwned()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (vendor, player) = CreateVendorWithPlayer(sceneTree);
        var creditsBefore = player.Credits;

        var sold = vendor.TrySell("wall_panel");

        AssertBool(sold).IsFalse();
        AssertBool(player.Credits == creditsBefore).IsTrue();
    }
}

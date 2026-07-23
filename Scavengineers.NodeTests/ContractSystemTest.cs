using System.Linq;
using System.Threading.Tasks;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Contracts;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;
using PlayerScript = Scavengineers.Scripts.Player.Player;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for the mission/contract system: accept/turn-in/expiry/arrival-
/// completion, using ContractGiverVerbTarget.AddOfferForTests to inject deterministic Contract
/// instances rather than fighting RollOneOffer's real randomness (see that method's own doc
/// comment). Harness mirrors TravelConsoleMultiStationTest's 2-Station + 1-Derelict shape, plus a
/// real PlayerTestHarness player and a ContractGiverVerbTarget wired to the same console.</summary>
[TestSuite]
public class ContractSystemTest
{
    private static (PlayerScript Player, TravelConsoleVerbTarget Console, ContractGiverVerbTarget Giver) MakeHarness(SceneTree sceneTree)
    {
        var player = PlayerTestHarness.CreateAttached(sceneTree);

        var homeShip = AutoFree(new ShipSim { HasPowerGrid = true });
        sceneTree.Root.AddChild(homeShip);

        var stationGroups = new Node3D[2];
        var stationShipSims = new ShipSim[2];
        var stationDestinationAirlocks = new AirlockDoorVerbTarget[2];
        var stationBuildTargets = new ShipBuildTarget[2];

        AirlockDoorVerbTarget? stationAirlock = null;

        for (var i = 0; i < 2; i++)
        {
            var stationShip = AutoFree(new ShipSim { Name = $"StationShip{i}" });
            sceneTree.Root.AddChild(stationShip);

            var stationGroup = AutoFree(new Node3D { Name = $"StationGroup{i}" });
            sceneTree.Root.AddChild(stationGroup);

            // The Station's own Floor (ShipBuildTarget) — needed so a CargoDelivery contract
            // originating here has somewhere real to spawn its cargo item (see
            // ContractGiverVerbTarget.TryTakeOffer/ShipBuildTarget.SpawnMissionItem).
            var stationBuildTarget = AutoFree(new ShipBuildTarget { Name = "Floor", ShipSimRef = stationShip, ShipRoot = stationGroup, SaveId = $"station_build_target_test_{i}" });
            stationGroup.AddChild(stationBuildTarget);

            var stationDestinationAirlock = AutoFree(new AirlockDoorVerbTarget { Name = $"StationDestinationAirlock{i}", ShipARef = stationShip, OwnsBridge = false });
            sceneTree.Root.AddChild(stationDestinationAirlock);

            if (i == 0)
            {
                // The one shared Home-Ship-side door, initially bound to Station 0 to match
                // TravelConsoleVerbTarget's own default _currentDestination — the same "initial
                // ShipBRef matches index 0" convention DerelictAirlock's own harness setup uses.
                stationAirlock = AutoFree(new AirlockDoorVerbTarget { Name = "StationAirlock", ShipARef = homeShip, ShipBRef = stationShip, PartnerDoorRef = stationDestinationAirlock });
                sceneTree.Root.AddChild(stationAirlock);
                stationDestinationAirlock.PartnerDoorRef = stationAirlock; // bidirectional — see AirlockDoorVerbTarget.RefreshBridgeEngagement
            }

            stationGroups[i] = stationGroup;
            stationShipSims[i] = stationShip;
            stationDestinationAirlocks[i] = stationDestinationAirlock;
            stationBuildTargets[i] = stationBuildTarget;
        }

        var derelictGroup = AutoFree(new Node3D { Name = "DerelictGroup1" });
        sceneTree.Root.AddChild(derelictGroup);

        var derelictShip = new ShipSim { Name = "ShipSim" };
        derelictGroup.AddChild(derelictShip);

        var derelictAirlock = AutoFree(new AirlockDoorVerbTarget { Name = "DerelictAirlock", ShipARef = homeShip, ShipBRef = derelictShip });
        sceneTree.Root.AddChild(derelictAirlock);

        // The Derelict's own Floor (ShipBuildTarget) — needed so a RetrieveItem contract targeting
        // this Derelict has somewhere real to spawn its item (see
        // ContractGiverVerbTarget.TryTakeOffer/ShipBuildTarget.SpawnMissionItem).
        var derelictBuildTarget = AutoFree(new ShipBuildTarget { Name = "Floor", ShipSimRef = derelictShip, ShipRoot = derelictGroup, SaveId = "derelict_build_target_test" });
        derelictGroup.AddChild(derelictBuildTarget);

        var console = AutoFree(new TravelConsoleVerbTarget
        {
            ShipSimRef = homeShip,
            DerelictAirlock = derelictAirlock,
            StationAirlock = stationAirlock,
            BaseTravelSeconds = 0.3f,
            MinTravelSeconds = 0.1f,
        });
        sceneTree.Root.AddChild(console);

        for (var i = 0; i < 2; i++)
        {
            console.RegisterStation(stationGroups[i], stationShipSims[i], stationDestinationAirlocks[i], stationBuildTargets[i]);
        }

        console.RegisterDerelict(derelictGroup, derelictShip, derelictBuildTarget);

        homeShip.InstallBattery(new CellCoord(0, 0), FixtureSurface.WallInner);
        homeShip.InstallThruster("t1", new CellCoord(1, 0), FixtureSurface.WallInner);
        homeShip.SetThrusterCharge("t1", 1f);

        var giver = AutoFree(new ContractGiverVerbTarget { ConsoleRef = console });
        sceneTree.Root.AddChild(giver);

        player.OpenContractBoard(giver);

        return (player, console, giver);
    }

    private static async Task TravelAndDockAsync(SceneTree sceneTree, TravelConsoleVerbTarget console, int destinationId)
    {
        console.BeginTravel(destinationId);
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        console.CompleteDocking();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void AcceptContract_MovesTheOfferIntoTheActiveList_AndOffTheBoard()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, _, giver) = MakeHarness(sceneTree);

        var offer = new Contract { InstanceId = "c1", TemplateId = "salvage_quota", Type = ContractType.SalvageQuota, ItemId = "scrap_metal", Count = 5, Reward = 20, FailureFee = 10, RemainingSeconds = 200f };
        giver.AddOfferForTests(offer);

        player.AcceptContract("c1");

        AssertBool(giver.AvailableOffers.Any(o => o.InstanceId == "c1")).IsFalse();
        AssertBool(giver.TryTakeOffer("c1") is null).IsTrue(); // already taken once
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryTurnInContract_SalvageQuota_GrantsCreditsAndCompletes_WhenPlayerHasEnough()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, _, giver) = MakeHarness(sceneTree);
        var startingCredits = player.Credits;

        // power_cell (not scrap_metal): the debug-loadout kit already fills most of the backpack
        // with scrap_metal, leaving no reliable room for a plain Add() call. Setting the hand
        // slot directly bypasses that entirely.
        giver.AddOfferForTests(new Contract { InstanceId = "c1", TemplateId = "salvage_quota", Type = ContractType.SalvageQuota, ItemId = "power_cell", Count = 2, Reward = 20, FailureFee = 10, RemainingSeconds = 200f });
        player.AcceptContract("c1");
        player.Inventory.Hands.SetSlot(PlayerInventory.RightHandSlotIndex, ("power_cell", 2, 1f));

        player.TryTurnInContract("c1");

        AssertInt(player.Credits).IsEqual(startingCredits + 20);
        AssertBool(player.Inventory.Has("power_cell", 1)).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TryTurnInContract_SalvageQuota_DoesNothing_WhenPlayerLacksEnough()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, _, giver) = MakeHarness(sceneTree);
        var startingCredits = player.Credits;

        giver.AddOfferForTests(new Contract { InstanceId = "c1", TemplateId = "salvage_quota", Type = ContractType.SalvageQuota, ItemId = "power_cell", Count = 2, Reward = 20, FailureFee = 10, RemainingSeconds = 200f });
        player.AcceptContract("c1");
        player.Inventory.Hands.SetSlot(PlayerInventory.RightHandSlotIndex, ("power_cell", 1, 1f)); // not enough (needs 2)

        player.TryTurnInContract("c1");

        AssertInt(player.Credits).IsEqual(startingCredits);
        AssertBool(player.Inventory.Has("power_cell", 1)).IsTrue(); // untouched, not partially consumed
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ExpiredContract_IsRemoved_AndAddsItsFailureFeeToPendingDebt()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, _, giver) = MakeHarness(sceneTree);

        giver.AddOfferForTests(new Contract { InstanceId = "c1", TemplateId = "salvage_quota", Type = ContractType.SalvageQuota, ItemId = "scrap_metal", Count = 5, Reward = 20, FailureFee = 15, RemainingSeconds = 0.2f });
        player.AcceptContract("c1");

        await sceneTree.ToSignal(sceneTree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);

        AssertInt(player.PendingDebt).IsEqual(15);

        // Turning in after expiry is a no-op — the contract is already gone.
        player.Inventory.Add("scrap_metal", 5);
        var creditsAfterExpiry = player.Credits;
        player.TryTurnInContract("c1");
        AssertInt(player.Credits).IsEqual(creditsAfterExpiry);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ArrivingAtAnyStation_SettlesPendingDebt_CappedAtWhatsAffordable()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, console, giver) = MakeHarness(sceneTree);

        giver.AddOfferForTests(new Contract { InstanceId = "c1", TemplateId = "salvage_quota", Type = ContractType.SalvageQuota, ItemId = "scrap_metal", Count = 5, Reward = 20, FailureFee = 15, RemainingSeconds = 0.2f });
        player.AcceptContract("c1");
        await sceneTree.ToSignal(sceneTree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
        AssertInt(player.PendingDebt).IsEqual(15);

        var creditsBeforeArrival = player.Credits;

        // Travel to the second Station (id 1) — any Station arrival settles debt.
        await TravelAndDockAsync(sceneTree, console, 1);

        var expectedPayment = System.Math.Min(15, creditsBeforeArrival);
        AssertInt(player.PendingDebt).IsEqual(15 - expectedPayment);
        AssertInt(player.Credits).IsEqual(creditsBeforeArrival - expectedPayment);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task TurningInCargoDeliveryAtTheDestinationStation_PaysOut_WhenCarryingTheCargo()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, console, giver) = MakeHarness(sceneTree);
        var startingCredits = player.Credits;

        giver.AddOfferForTests(new Contract { InstanceId = "c1", TemplateId = "cargo_delivery", Type = ContractType.CargoDelivery, ItemId = "cargo_crate", Count = 1, OriginStationId = 0, DestinationStationId = 1, Reward = 50, FailureFee = 20, RemainingSeconds = 200f });
        player.AcceptContract("c1");

        var originStationGroup = sceneTree.Root.GetNode<Node3D>("StationGroup0");
        var crate = originStationGroup.GetChildren().OfType<PickupItem>().Single();
        AssertString(crate.ItemId).IsEqual("cargo_crate");
        crate.ExecuteVerb(crate.AvailableVerbs[0], player.Inventory);

        await TravelAndDockAsync(sceneTree, console, 1);

        // Arrival alone must not pay out — only handing it over at the destination's own
        // contract-giver does (see Player.CanTurnIn).
        AssertInt(player.Credits).IsEqual(startingCredits);

        player.TryTurnInContract("c1");

        AssertInt(player.Credits).IsEqual(startingCredits + 50);
        AssertBool(player.Inventory.Has("cargo_crate", 1)).IsFalse(); // consumed on hand-over
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task TurningInCargoDeliveryAtTheDestinationStation_DoesNothing_WithoutTheCargo()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, console, giver) = MakeHarness(sceneTree);
        var startingCredits = player.Credits;

        // Offer accepted (spawning the crate at the origin Station), but never picked up.
        giver.AddOfferForTests(new Contract { InstanceId = "c1", TemplateId = "cargo_delivery", Type = ContractType.CargoDelivery, ItemId = "cargo_crate", Count = 1, OriginStationId = 0, DestinationStationId = 1, Reward = 50, FailureFee = 20, RemainingSeconds = 200f });
        player.AcceptContract("c1");

        await TravelAndDockAsync(sceneTree, console, 1);
        player.TryTurnInContract("c1");

        AssertInt(player.Credits).IsEqual(startingCredits);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TurningInCargoDeliveryAtTheOriginStation_IsRefused_EvenWhileCarryingTheCargo()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, _, giver) = MakeHarness(sceneTree);
        var startingCredits = player.Credits;

        giver.AddOfferForTests(new Contract { InstanceId = "c1", TemplateId = "cargo_delivery", Type = ContractType.CargoDelivery, ItemId = "cargo_crate", Count = 1, OriginStationId = 0, DestinationStationId = 1, Reward = 50, FailureFee = 20, RemainingSeconds = 200f });
        player.AcceptContract("c1");

        var originStationGroup = sceneTree.Root.GetNode<Node3D>("StationGroup0");
        var crate = originStationGroup.GetChildren().OfType<PickupItem>().Single();
        crate.ExecuteVerb(crate.AvailableVerbs[0], player.Inventory);

        // Still docked at the origin (Station 0, the harness's default) — carrying the cargo isn't
        // enough on its own, it has to be handed over at the destination's own contract-giver.
        player.TryTurnInContract("c1");

        AssertInt(player.Credits).IsEqual(startingCredits);
        AssertBool(player.Inventory.Has("cargo_crate", 1)).IsTrue(); // not consumed by the refused attempt
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ArrivingAtTheSurveyTarget_CompletesAutomatically_WhenTheRightCartridgeIsEquipped()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        // The debug-loadout starting kit (Player._Ready) already equips a fully-loaded PDA (both
        // scan cartridges), so this is the harness's own default state — no extra setup needed.
        var (player, console, giver) = MakeHarness(sceneTree);
        var startingCredits = player.Credits;

        // Derelict is destination id 2 here (StationCount is 2, so 0/1 are Stations).
        giver.AddOfferForTests(new Contract { InstanceId = "c1", TemplateId = "survey_scan", Type = ContractType.Survey, ItemId = "health_scan_cartridge", TargetDestinationId = 2, Reward = 30, FailureFee = 10, RemainingSeconds = 200f });
        player.AcceptContract("c1");

        await TravelAndDockAsync(sceneTree, console, 2);

        AssertInt(player.Credits).IsEqual(startingCredits + 30);
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task ArrivingAtTheSurveyTarget_DoesNotComplete_WithoutTheCartridgeEquipped()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, console, giver) = MakeHarness(sceneTree);
        // Strips the debug-loadout's default PDA for this case — Player.TryUnequipItem needs a
        // free hand slot to relocate it to, which the debug loadout's crowbar/power_drill already
        // occupy both of; PlayerInventory.ClearEquippedContainer bypasses that entirely.
        player.Inventory.ClearEquippedContainer("pda");
        var startingCredits = player.Credits;

        giver.AddOfferForTests(new Contract { InstanceId = "c1", TemplateId = "survey_scan", Type = ContractType.Survey, ItemId = "health_scan_cartridge", TargetDestinationId = 2, Reward = 30, FailureFee = 10, RemainingSeconds = 200f });
        player.AcceptContract("c1");

        await TravelAndDockAsync(sceneTree, console, 2);

        AssertInt(player.Credits).IsEqual(startingCredits);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void AcceptingARetrieveItemContract_SpawnsTheTargetItem_OnTheTargetDerelict()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, _, giver) = MakeHarness(sceneTree);

        // Derelict is destination id 2 here (StationCount is 2, so 0/1 are Stations) — same
        // convention the existing Survey tests above already use.
        giver.AddOfferForTests(new Contract { InstanceId = "c1", TemplateId = "retrieve_common", Type = ContractType.RetrieveItem, ItemId = "o2_tank", Count = 1, TargetDestinationId = 2, Reward = 40, FailureFee = 20, RemainingSeconds = 200f });

        var derelictGroup = sceneTree.Root.GetNode<Node3D>("DerelictGroup1");
        AssertBool(derelictGroup.GetChildren().OfType<PickupItem>().Any()).IsFalse(); // nothing before acceptance

        player.AcceptContract("c1");

        var pickup = derelictGroup.GetChildren().OfType<PickupItem>().Single();
        AssertString(pickup.ItemId).IsEqual("o2_tank");
        AssertInt(pickup.Count).IsEqual(1);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void AcceptingARetrieveItemContract_ThenPickingUpTheSpawnedItem_LetsTheContractBeTurnedIn()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var (player, _, giver) = MakeHarness(sceneTree);
        var startingCredits = player.Credits;

        giver.AddOfferForTests(new Contract { InstanceId = "c1", TemplateId = "retrieve_common", Type = ContractType.RetrieveItem, ItemId = "o2_tank", Count = 1, TargetDestinationId = 2, Reward = 40, FailureFee = 20, RemainingSeconds = 200f });
        player.AcceptContract("c1");

        var derelictGroup = sceneTree.Root.GetNode<Node3D>("DerelictGroup1");
        var pickup = derelictGroup.GetChildren().OfType<PickupItem>().Single();
        pickup.ExecuteVerb(pickup.AvailableVerbs[0], player.Inventory);

        player.TryTurnInContract("c1");

        AssertInt(player.Credits).IsEqual(startingCredits + 40);
    }
}

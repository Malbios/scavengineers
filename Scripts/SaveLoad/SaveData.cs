using System.Collections.Generic;
using System.Linq;

using Scavengineers.Scripts.Contracts;
using Scavengineers.Scripts.Inventory;

namespace Scavengineers.Scripts.SaveLoad;

public sealed class SaveData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public PlayerSaveData Player { get; set; } = new();

    public Dictionary<string, bool> ObjectStates { get; set; } = new();

    public Dictionary<string, BuildTargetSaveData> BuildTargets { get; set; } = new();

    public Dictionary<string, string> ObjectStringStates { get; set; } = new();

    /// <summary>A procedurally-generated ship's own resolved seed (see IShipLayoutSaveable) —
    /// keyed by the ship's ShipSim.SaveId. Read directly by ShipSim itself (see
    /// SaveManager.TryReadShipLayoutSeeds), not through the normal ApplySaveState callback — by
    /// the time that would fire, the ship's Deck already exists.</summary>
    public Dictionary<string, int> ShipLayoutSeeds { get; set; } = new();

    /// <summary>Per-ship live simulation state — the atmosphere in every cell and which cells are
    /// burning — keyed by ShipSim.SaveId. Missing/empty means startup seeding applies as before
    /// (e.g. for a save predating this).</summary>
    public Dictionary<string, ShipStateSaveData> Ships { get; set; } = new();

    /// <summary>Full backpacks (or other containers, later) sitting loose in the world — not
    /// tied to any ShipBuildTarget. Scanned/respawned by SaveManager via the "dropped_container"
    /// group (see ContainerPickupItem).</summary>
    public List<DroppedContainerSaveData> DroppedContainers { get; set; } = new();

    /// <summary>A still-outstanding RetrieveItem contract's spawned target item — scanned/
    /// respawned by SaveManager via the "mission_item" group (see PickupItem.MissionOwnerSaveId).
    /// Deliberately decoupled from ActiveContracts: the item's own presence here is the source of
    /// truth, not the owning Contract — once picked up, PickupItem.ExecuteVerb already
    /// QueueFree()s it, so it simply stops appearing here.</summary>
    public List<MissionItemSaveData> MissionItems { get; set; } = new();
}

/// <summary>A RetrieveItem contract's spawned target item's whole world state — position plus
/// which ShipBuildTarget (by SaveId) it belongs to, so SaveManager.Load can re-parent it under
/// the right derelict's ShipRoot instead of the world root.</summary>
public sealed class MissionItemSaveData
{
    public float PosX { get; set; }

    public float PosY { get; set; }

    public float PosZ { get; set; }

    public string ItemId { get; set; } = "";

    public int Count { get; set; }

    public float Charge { get; set; } = 1f;

    public string OwnerBuildTargetSaveId { get; set; } = "";
}

/// <summary>A ContainerPickupItem's whole world state — position plus its own contents, since
/// it's a loose, dynamically-spawned object rather than a fixed scene node.</summary>
public sealed class DroppedContainerSaveData
{
    public float PosX { get; set; }

    public float PosY { get; set; }

    public float PosZ { get; set; }

    public string ItemId { get; set; } = "";

    /// <summary>Legacy pre-per-slot format (itemId → aggregate count). SaveManager.Load falls
    /// back to this only when <see cref="Slots"/> is empty.</summary>
    public Dictionary<string, int> Contents { get; set; } = new();

    /// <summary>Replaces <see cref="Contents"/> — each slot by position, carrying its own real
    /// Charge (see SlotSaveData).</summary>
    public List<SlotSaveData?> Slots { get; set; } = new();
}

/// <summary>Everything a ShipBuildTarget places dynamically — no fixed scene node to hang the
/// simpler bool-only ISaveable off, so this captures its whole placed-tile/edge state instead.
/// Plain DTOs, deliberately decoupled from Scavengineers.Sim.Grid.CellCoord.</summary>
public sealed class BuildTargetSaveData
{
    public List<TileCoord> Conduits { get; set; } = new();

    /// <summary>Wall-mounted conduits — same tile-addressed fixture as <see cref="Conduits"/>,
    /// just rendered against a specific wall face instead of the floor, so which neighbor cell
    /// it's mounted against is saved too.</summary>
    public List<WallConduitCoord> WallConduits { get; set; } = new();

    /// <summary>Covers both player-built interior partitions and repaired hull breaches.</summary>
    public List<EdgeCoord> Walls { get; set; } = new();

    /// <summary>Tiles whose floor/ceiling panel is currently missing (breached).</summary>
    public List<TileCoord> FloorBreaches { get; set; } = new();

    public List<TileCoord> CeilingBreaches { get; set; } = new();

    /// <summary>Battery/Switch/RechargeStation — at most one of each Type ("battery"/"switch"/
    /// "recharge_station") at a time.</summary>
    public List<MachineCoord> Machines { get; set; } = new();

    /// <summary>Cells added beyond the ship's default footprint via the Extend Floor verb —
    /// distinct from every other list here, which all describe state *on* an existing cell.
    /// Replayed first (see ShipBuildTarget.ApplyBuildState) since Walls/FloorBreaches/
    /// CeilingBreaches entries may reference one of these cells.</summary>
    public List<TileCoord> ExtendedCells { get; set; } = new();

    /// <summary>Only entries below full health (1.0) are saved — missing means default full
    /// health, same convention Deck.FloorHealth/CeilingHealth/WallHealth use.</summary>
    public List<TileHealthCoord> FloorHealthEntries { get; set; } = new();

    public List<TileHealthCoord> CeilingHealthEntries { get; set; } = new();

    public List<EdgeHealthCoord> WallHealthEntries { get; set; } = new();

    /// <summary>A placed conduit fixture's own Condition (wear), keyed by its ConduitFixtureId —
    /// a plain string key serializes natively (unlike Scavengineers.Sim.Grid.CellCoord, which
    /// can't back a JSON dictionary key without a custom converter). Only entries below full
    /// health are saved.</summary>
    public Dictionary<string, float> ConduitConditions { get; set; } = new();
}

/// <summary>One ship's live sim state (see <see cref="SaveData.Ships"/>). Plain DTOs, deliberately
/// decoupled from Scavengineers.Sim's own CellCoord/AtmosphereVolume.</summary>
public sealed class ShipStateSaveData
{
    /// <summary>Every modelled cell's pressure/O2/temperature. Written in full rather than diffed
    /// against a default: unlike structural health, there's no single "unmodified" value to omit
    /// against — a breathable cell and a vacuum cell are equally ordinary.</summary>
    public List<CellVolume> Volumes { get; set; } = new();

    /// <summary>Cells currently on fire. Applied as a wholesale replacement, so a save with none
    /// genuinely puts a burning ship out rather than merely adding nothing.</summary>
    public List<TileCoord> Fires { get; set; } = new();
}

public readonly record struct CellVolume(int X, int Y, double Pressure, double O2Fraction, double Temperature);

public readonly record struct TileCoord(int X, int Y);

public readonly record struct EdgeCoord(int AX, int AY, int BX, int BY);

public readonly record struct TileHealthCoord(int X, int Y, float Health);

public readonly record struct EdgeHealthCoord(int AX, int AY, int BX, int BY, float Health);

/// <summary>Slot defaults to 1 (today's top height, 2 slots per wall) so a save written before
/// wall conduits had height slots keeps its wire at the same visual height it was saved at.</summary>
public readonly record struct WallConduitCoord(int TileX, int TileY, int NeighborX, int NeighborY, int Slot = 1);

/// <summary>State is each machine's own extra save data, stringified (Battery's charge fraction,
/// Switch's on/off bool) — null for a stateless machine (Recharge Station). Applied directly to
/// the freshly spawned instance on load, not through the generic "saveable" group scan. Condition
/// is a separate field (not part of State) since it's the machine's own Deck fixture wear —
/// Battery is excluded from wear entirely, so Condition defaults to 1f and is only meaningful for
/// Switch/RechargeStation.</summary>
public readonly record struct MachineCoord(string Type, int EdgeAX, int EdgeAY, int EdgeBX, int EdgeBY, string? State, float Condition = 1f);

/// <summary>A draggable HUD window's top-left position. Nullable rather than defaulted to a
/// sentinel — a dragged window can legitimately end up at a negative X/Y, so only "the JSON key
/// is absent" unambiguously means "never moved / predates this feature."</summary>
public readonly record struct WindowPosition(float X, float Y);

/// <summary>One inventory slot's full save state — mirrors SlotContainer's own per-slot
/// (ItemId, Count, Charge) shape, so a battery's real remaining charge survives save/load instead
/// of collapsing into a bare itemId-count pair.</summary>
public sealed class SlotSaveData
{
    public string ItemId { get; set; } = "";

    public int Count { get; set; }

    public float Charge { get; set; } = 1f;
}

/// <summary>Converts between a live SlotContainer and its saved List&lt;SlotSaveData?&gt; form —
/// shared by PlayerSaveData's hand/backpack slots and DroppedContainerSaveData's own contents.</summary>
public static class SlotSaveDataConverter
{
    public static List<SlotSaveData?> Capture(SlotContainer container) =>
        container.Slots
            .Select(slot => slot is { } s ? new SlotSaveData { ItemId = s.ItemId, Count = s.Count, Charge = s.Charge } : null)
            .ToList();

    /// <summary>Applies each saved slot onto `container` by position — a null entry, or running
    /// past the saved list's length, leaves that slot empty. Assumes `container` starts empty.</summary>
    public static void Restore(SlotContainer container, List<SlotSaveData?> slots)
    {
        for (var i = 0; i < slots.Count && i < container.Slots.Count; i++)
        {
            if (slots[i] is { } slot)
            {
                container.SetSlot(i, (slot.ItemId, slot.Count, slot.Charge));
            }
        }
    }
}

public sealed class PlayerSaveData
{
    public float PosX { get; set; }

    public float PosY { get; set; }

    public float PosZ { get; set; }

    public float Yaw { get; set; }

    public float Pitch { get; set; }

    public float O2Percent { get; set; }

    public float HealthPercent { get; set; } = 100f;

    public float HungerPercent { get; set; } = 100f;

    public float ThirstPercent { get; set; } = 100f;

    public float EnergyPercent { get; set; } = 100f;

    /// <summary>Legacy pre-per-slot format (itemId → aggregate count, no hand position or per-slot
    /// Charge). ApplyPlayerState falls back to this only when <see cref="HandSlots"/> is empty.</summary>
    public Dictionary<string, int> Inventory { get; set; } = new();

    /// <summary>Replaces <see cref="Inventory"/> — index 0/1 = left/right hand (see
    /// PlayerInventory.LeftHandSlotIndex/RightHandSlotIndex), each carrying its own real Charge. A
    /// real capture always has exactly PlayerInventory.HandCount entries, so an empty list here
    /// unambiguously means "predates this field."</summary>
    public List<SlotSaveData?> HandSlots { get; set; } = new();

    public int Credits { get; set; }

    /// <summary>Null when no backpack is worn.</summary>
    public string? BackpackItemId { get; set; }

    /// <summary>Which backpack-type item ("backpack" or "debug_backpack") the player possesses at
    /// all — worn, merely held, or stored inside another backpack — independent of
    /// <see cref="BackpackItemId"/> (which only means "currently worn"). Null falls back to
    /// BackpackItemId at restore. Identifies which item <see cref="BackpackSlots"/> belongs to.</summary>
    public string? OwnedBackpackItemId { get; set; }

    /// <summary>Legacy pre-per-slot format — same story as <see cref="Inventory"/>, for the worn
    /// backpack's own contents.</summary>
    public Dictionary<string, int> BackpackContents { get; set; } = new();

    /// <summary>Replaces <see cref="BackpackContents"/> — same shape/fallback story as
    /// <see cref="HandSlots"/>. Captures whichever backpack-type item is owned at all (see
    /// <see cref="OwnedBackpackItemId"/>), not just a currently-worn one.</summary>
    public List<SlotSaveData?> BackpackSlots { get; set; } = new();

    /// <summary>Captured from the actual worn container's own slot count at save time, not
    /// assumed, so a debug backpack's real 24 slots round-trip instead of being truncated to 8.</summary>
    public int BackpackSlotCount { get; set; } = PlayerInventory.BackpackSlotCount;

    public bool HasDrill { get; set; }

    public bool DrillHasBattery { get; set; }

    public float DrillCharge { get; set; }

    public bool HasFlashlight { get; set; }

    public bool FlashlightHasBattery { get; set; }

    public float FlashlightCharge { get; set; }

    public WindowPosition? InventoryWindow { get; set; }

    public WindowPosition? DrillWindow { get; set; }

    public WindowPosition? FlashlightWindow { get; set; }

    public WindowPosition? BackpackWindow { get; set; }

    /// <summary>Null when no helmet is worn.</summary>
    public string? HeadItemId { get; set; }

    /// <summary>Null when no EVA suit torso piece is worn.</summary>
    public string? TorsoItemId { get; set; }

    /// <summary>Whether the player possesses an EVA suit at all — worn, merely held, or stored —
    /// independent of <see cref="TorsoItemId"/> (which only means "currently worn"). Falls back
    /// to TorsoItemId at restore. Gates whether <see cref="TorsoSlots"/>/HasSuitO2Tank/etc. below
    /// should be restored even when the suit isn't currently equipped.</summary>
    public bool HasEvaSuit { get; set; }

    /// <summary>The torso's own 2 pocket slots — same SlotSaveData shape as
    /// <see cref="HandSlots"/>/<see cref="BackpackSlots"/>. Captures the suit's contents whenever
    /// it's owned at all (see <see cref="HasEvaSuit"/>), not just worn.</summary>
    public List<SlotSaveData?> TorsoSlots { get; set; } = new();

    public int TorsoSlotCount { get; set; } = PlayerInventory.TorsoSlotCount;

    /// <summary>Whether a tank/filter/battery is actually loaded in the corresponding suit
    /// sub-slot. Meaningless unless <see cref="HasEvaSuit"/> (or, for an old save,
    /// <see cref="TorsoItemId"/>) is also set.</summary>
    public bool HasSuitO2Tank { get; set; }

    public float SuitO2Charge { get; set; }

    public bool HasSuitN2Tank { get; set; }

    public float SuitN2Charge { get; set; }

    public bool HasSuitFilter { get; set; }

    public float SuitFilterCharge { get; set; }

    public bool HasSuitBattery { get; set; }

    public float SuitBatteryCharge { get; set; }

    public float CO2Percent { get; set; }

    public WindowPosition? SuitWindow { get; set; }

    /// <summary>Null when the PDA isn't currently worn.</summary>
    public string? PdaItemId { get; set; }

    /// <summary>Whether the player possesses a PDA at all — worn, merely held, or stored —
    /// independent of <see cref="PdaItemId"/> (which only means "currently worn"). Falls back to
    /// PdaItemId at restore.</summary>
    public bool HasPda { get; set; }

    /// <summary>The PDA's one cartridge pocket — same SlotSaveData shape as
    /// <see cref="TorsoSlots"/>. Captures contents whenever the PDA is owned at all (see
    /// <see cref="HasPda"/>), not just worn.</summary>
    public List<SlotSaveData?> PdaSlots { get; set; } = new();

    public int PdaSlotCount { get; set; } = PlayerInventory.PdaSlotCount;

    public WindowPosition? PdaWindow { get; set; }

    public WindowPosition? ThrusterWindow { get; set; }

    /// <summary>Accepted-but-not-yet-resolved contracts (see Player._activeContracts).</summary>
    public List<ContractSaveData> ActiveContracts { get; set; } = new();

    /// <summary>Owed from a missed contract deadline, paid down capped at what's affordable
    /// whenever the ship docks at any Station (see Player.SettlePendingDebt).</summary>
    public int PendingDebt { get; set; }
}

/// <summary>An accepted Contract's full save state — plain-DTO twin of the live Contract record,
/// same pattern DroppedContainerSaveData uses for ContainerPickupItem.</summary>
public sealed class ContractSaveData
{
    public string InstanceId { get; set; } = "";

    public string TemplateId { get; set; } = "";

    public ContractType Type { get; set; }

    public string? ItemId { get; set; }

    public int Count { get; set; } = 1;

    public int? TargetDestinationId { get; set; }

    public int? OriginStationId { get; set; }

    public int? DestinationStationId { get; set; }

    public int Reward { get; set; }

    public int FailureFee { get; set; }

    public float RemainingSeconds { get; set; }
}

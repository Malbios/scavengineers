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
    /// keyed by the ship's ShipSim.SaveId. Purely additive: empty for any save predating
    /// procedural ship generation, same "missing = default" convention every other collection
    /// field here already uses. Read directly by ShipSim itself (see
    /// SaveManager.TryReadShipLayoutSeeds), not through the normal ApplySaveState callback — by
    /// the time that would fire, the ship's Deck already exists.</summary>
    public Dictionary<string, int> ShipLayoutSeeds { get; set; } = new();

    /// <summary>Per-ship live simulation state — the atmosphere in every cell and which cells are
    /// burning — keyed by ShipSim.SaveId. This is the "serialize live sim state, not just static
    /// layout" rule in docs/architecture/save-schema.md, which the save format previously didn't
    /// honour: everything structural round-tripped, but a ship you'd vented came back at whatever
    /// its startup seeding produced, and a fire mid-spread was simply lost. Purely additive: empty
    /// for any save predating this, in which case startup seeding still applies exactly as before.</summary>
    public Dictionary<string, ShipStateSaveData> Ships { get; set; } = new();

    /// <summary>Full backpacks (or other containers, later) sitting loose in the world — not
    /// tied to any ShipBuildTarget, so none of the lists above fit. Scanned/respawned by
    /// SaveManager via the "dropped_container" group (see ContainerPickupItem).</summary>
    public List<DroppedContainerSaveData> DroppedContainers { get; set; } = new();

    /// <summary>A still-outstanding RetrieveItem contract's spawned target item — scanned/
    /// respawned by SaveManager via the "mission_item" group (see PickupItem.MissionOwnerSaveId),
    /// same shape as <see cref="DroppedContainers"/>. Deliberately decoupled from ActiveContracts:
    /// the item's own presence here is the source of truth, not the owning Contract — once picked
    /// up, PickupItem.ExecuteVerb already QueueFree()s it, so it simply stops appearing here
    /// (zero double-spawn risk). Purely additive: empty for any save predating this feature.</summary>
    public List<MissionItemSaveData> MissionItems { get; set; } = new();
}

/// <summary>A RetrieveItem contract's spawned target item's whole world state — position plus
/// which ShipBuildTarget (by SaveId) it belongs to, so SaveManager.Load can re-parent it under the
/// right derelict's ShipRoot (see ShipBuildTarget.PlaceMissionItem) instead of the world root.</summary>
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

    /// <summary>Legacy pre-per-item-state format (itemId → aggregate count, no per-slot Charge) —
    /// no longer written by SaveManager.Save, kept only so a save from before <see cref="Slots"/>
    /// existed can still be read (SaveManager.Load falls back to this when Slots is empty).</summary>
    public Dictionary<string, int> Contents { get; set; } = new();

    /// <summary>Replaces <see cref="Contents"/> — each slot by position, carrying its own real
    /// Charge (see SlotSaveData). Empty for a save predating this; SaveManager.Load falls back to
    /// replaying the legacy Contents dict in that case.</summary>
    public List<SlotSaveData?> Slots { get; set; } = new();
}

/// <summary>Everything a ShipBuildTarget places dynamically — no fixed scene node to hang the
/// simpler bool-only ISaveable off, so this captures its whole placed-tile/edge state instead.
/// Plain DTOs, deliberately decoupled from Scavengineers.Sim.Grid.CellCoord (docs/architecture/
/// save-schema.md's own "per-tile / per-edge" framing for the save format).</summary>
public sealed class BuildTargetSaveData
{
    public List<TileCoord> Conduits { get; set; } = new();

    /// <summary>Wall-mounted conduits — same tile-addressed fixture as <see cref="Conduits"/>,
    /// just rendered against a specific wall face instead of the floor, so which neighbor cell
    /// (i.e. which of up to 4 walls) it's mounted against is saved too (needed to reconstruct
    /// the right visual on load, and a tile can carry more than one of these at once).</summary>
    public List<WallConduitCoord> WallConduits { get; set; } = new();

    /// <summary>Covers both player-built interior partitions and repaired hull breaches — both
    /// already land in ShipBuildTarget's own _placedWalls dictionary today.</summary>
    public List<EdgeCoord> Walls { get; set; } = new();

    /// <summary>Tiles whose floor/ceiling panel is currently missing (breached) — the ship's
    /// default layout starts with none, so these only ever record player-scrapped tiles.</summary>
    public List<TileCoord> FloorBreaches { get; set; } = new();

    public List<TileCoord> CeilingBreaches { get; set; } = new();

    /// <summary>Battery/Switch/RechargeStation — at most one of each Type ("battery"/"switch"/
    /// "recharge_station") at a time. Purely additive: defaults to empty for any save predating
    /// these being player-install/uninstall-able rather than fixed scene nodes.</summary>
    public List<MachineCoord> Machines { get; set; } = new();

    /// <summary>Cells added beyond the ship's default footprint via the Extend Floor verb —
    /// distinct from every other list here, which all describe state *on* an existing cell.
    /// Replayed first (see ShipBuildTarget.ApplyBuildState) since Walls/FloorBreaches/
    /// CeilingBreaches entries may reference one of these cells. Purely additive: empty for any
    /// save predating dynamic ship expansion.</summary>
    public List<TileCoord> ExtendedCells { get; set; } = new();

    /// <summary>Only entries below full health (1.0) are saved — missing means default full
    /// health, same "absence is the default" convention Deck.FloorHealth/CeilingHealth/WallHealth
    /// already use (see WearSystem/ShipBuildTarget's Maintain/Repair verbs). Purely additive:
    /// empty for any save predating structural wear.</summary>
    public List<TileHealthCoord> FloorHealthEntries { get; set; } = new();

    public List<TileHealthCoord> CeilingHealthEntries { get; set; } = new();

    public List<EdgeHealthCoord> WallHealthEntries { get; set; } = new();

    /// <summary>A placed conduit fixture's own Condition (wear), keyed by its ConduitFixtureId —
    /// a plain string key serializes natively (unlike Scavengineers.Sim.Grid.CellCoord, which
    /// can't back a JSON dictionary key without a custom converter — the reason
    /// FloorHealthEntries/CeilingHealthEntries/WallHealthEntries above are lists of coord+health
    /// structs instead of dictionaries). Only entries below full health are saved, same
    /// convention as those lists.</summary>
    public Dictionary<string, float> ConduitConditions { get; set; } = new();
}

/// <summary>One ship's live sim state (see <see cref="SaveData.Ships"/>). Plain DTOs, deliberately
/// decoupled from Scavengineers.Sim's own CellCoord/AtmosphereVolume, same convention as
/// <see cref="BuildTargetSaveData"/>.</summary>
public sealed class ShipStateSaveData
{
    /// <summary>Every modelled cell's pressure/O2/temperature. Written in full rather than
    /// diffed against a default: unlike structural health, there's no single "unmodified" value to
    /// omit against — a breathable cell and a vacuum cell are equally ordinary, and which one a
    /// given ship *should* start at is exactly the thing this is here to stop guessing at.</summary>
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
/// the freshly spawned instance on load, not through the generic "saveable" group scan (a
/// dynamically spawned machine is never in that group). Condition is a separate field (not part
/// of State) since it's the machine's own Deck fixture wear, not its ApplySaveState string —
/// Battery is excluded from wear entirely (its own Condition already means charge, tracked via
/// State), so Condition defaults to 1f (full health) and is only meaningful for Switch/
/// RechargeStation.</summary>
public readonly record struct MachineCoord(string Type, int EdgeAX, int EdgeAY, int EdgeBX, int EdgeBY, string? State, float Condition = 1f);

/// <summary>A draggable HUD window's top-left position (see Scripts/Player/DraggableWindow.cs).
/// Nullable on PlayerSaveData rather than defaulted to a sentinel — a dragged window can
/// legitimately end up at a negative X/Y (partly off the top/left edge), so only "the JSON key is
/// absent" unambiguously means "never moved / predates this feature."</summary>
public readonly record struct WindowPosition(float X, float Y);

/// <summary>One inventory slot's full save state — mirrors SlotContainer's own per-slot
/// (ItemId, Count, Charge) shape, so a battery's real remaining charge (or any future per-item
/// state) survives save/load instead of collapsing into a bare itemId-count pair. Charge is 1f
/// (full) and meaningless for every item except "battery", same as the live slot's own field.</summary>
public sealed class SlotSaveData
{
    public string ItemId { get; set; } = "";

    public int Count { get; set; }

    public float Charge { get; set; } = 1f;
}

/// <summary>Converts between a live SlotContainer and its saved List&lt;SlotSaveData?&gt; form —
/// shared by PlayerSaveData's hand/backpack slots and DroppedContainerSaveData's own contents, so
/// the capture/restore logic isn't duplicated across Player.cs and SaveManager.cs.</summary>
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

    /// <summary>Defaulted to 100f — an old save predating Health has no way to have died, so it
    /// loads as fully healthy, same "missing field = safe default" convention as HungerPercent
    /// etc. below.</summary>
    public float HealthPercent { get; set; } = 100f;

    /// <summary>Defaulted to 100f, not 0 — an old save missing this field (System.Text.Json
    /// only overwrites properties actually present in the JSON) loads as "fully rested" rather
    /// than instantly starving.</summary>
    public float HungerPercent { get; set; } = 100f;

    public float ThirstPercent { get; set; } = 100f;

    public float EnergyPercent { get; set; } = 100f;

    /// <summary>Legacy pre-per-item-state format (itemId → aggregate count, no hand position, no
    /// per-slot Charge) — no longer written by CapturePlayerState, kept only so a save from
    /// before <see cref="HandSlots"/> existed can still be read (ApplyPlayerState falls back to
    /// this when HandSlots is empty).</summary>
    public Dictionary<string, int> Inventory { get; set; } = new();

    /// <summary>Replaces <see cref="Inventory"/> — index 0/1 = left/right hand (see
    /// PlayerInventory.LeftHandSlotIndex/RightHandSlotIndex), each carrying its own real Charge
    /// (see SlotSaveData). A real capture always has exactly PlayerInventory.HandCount entries
    /// (even all-null for empty hands), so an empty list here unambiguously means "predates this
    /// field" — ApplyPlayerState falls back to replaying the legacy Inventory dict in that case.</summary>
    public List<SlotSaveData?> HandSlots { get; set; } = new();

    public int Credits { get; set; }

    /// <summary>Null when no backpack is worn — the body's own Inventory dict above stays
    /// backpack-contents-free either way (see Player.CapturePlayerState/ApplyPlayerState).</summary>
    public string? BackpackItemId { get; set; }

    /// <summary>Which backpack-type item ("backpack" or "debug_backpack") the player possesses
    /// at all — worn, merely held, or stored inside another backpack — independent of
    /// <see cref="BackpackItemId"/> (which only means "currently worn"). Null for an old save
    /// predating the persistent-contents model (falls back to BackpackItemId at restore, so a
    /// pre-existing worn backpack still round-trips) or one with no backpack owned anywhere.
    /// Identifies which item <see cref="BackpackSlots"/> below actually belongs to.</summary>
    public string? OwnedBackpackItemId { get; set; }

    /// <summary>Legacy pre-per-item-state format — same story as <see cref="Inventory"/>, just
    /// for the worn backpack's own contents.</summary>
    public Dictionary<string, int> BackpackContents { get; set; } = new();

    /// <summary>Replaces <see cref="BackpackContents"/> — same shape/fallback story as
    /// <see cref="HandSlots"/>. Captures whichever backpack-type item is owned at all (see
    /// <see cref="OwnedBackpackItemId"/>), not just a currently-worn one.</summary>
    public List<SlotSaveData?> BackpackSlots { get; set; } = new();

    /// <summary>Defaulted to the normal backpack's own size — an old save missing this field
    /// (predates the debug backpack's different slot count) reconstructs at the size every
    /// backpack used to always be. Captured from the actual worn container's own slot count at
    /// save time (Player.CapturePlayerState), not assumed, so a debug backpack's real 24 slots
    /// round-trip correctly instead of being silently truncated back to 8.</summary>
    public int BackpackSlotCount { get; set; } = PlayerInventory.BackpackSlotCount;

    /// <summary>False for a save from before the power drill existed — distinct from
    /// DrillHasBattery/DrillCharge's own JSON-missing defaults (false/0f) so an old save
    /// doesn't get misread as "owns an empty drill" (see Player.ApplyPlayerState).</summary>
    public bool HasDrill { get; set; }

    public bool DrillHasBattery { get; set; }

    public float DrillCharge { get; set; }

    /// <summary>False for a save from before the flashlight had its own battery — same
    /// HasDrill/DrillHasBattery/DrillCharge shape and reasoning.</summary>
    public bool HasFlashlight { get; set; }

    public bool FlashlightHasBattery { get; set; }

    public float FlashlightCharge { get; set; }

    public WindowPosition? InventoryWindow { get; set; }

    public WindowPosition? DrillWindow { get; set; }

    public WindowPosition? FlashlightWindow { get; set; }

    public WindowPosition? BackpackWindow { get; set; }

    /// <summary>Null when no helmet is worn — same nullable-item-id shape as
    /// <see cref="BackpackItemId"/>.</summary>
    public string? HeadItemId { get; set; }

    /// <summary>Null when no EVA suit torso piece is worn — same nullable-item-id shape as
    /// <see cref="BackpackItemId"/>.</summary>
    public string? TorsoItemId { get; set; }

    /// <summary>Whether the player possesses an EVA suit at all — worn, merely held, or stored —
    /// independent of <see cref="TorsoItemId"/> (which only means "currently worn"). False for an
    /// old save predating the persistent-contents model (falls back to TorsoItemId at restore,
    /// so a pre-existing worn suit still round-trips) or one with no suit owned anywhere. Gates
    /// whether <see cref="TorsoSlots"/>/HasSuitO2Tank/etc. below should be restored even when the
    /// suit isn't currently equipped.</summary>
    public bool HasEvaSuit { get; set; }

    /// <summary>The torso's own 2 pocket slots — same SlotSaveData shape as
    /// <see cref="HandSlots"/>/<see cref="BackpackSlots"/>, no legacy-dict fallback needed since
    /// this is a brand-new field (nothing predates it to fall back from). Captures the suit's
    /// contents whenever it's owned at all (see <see cref="HasEvaSuit"/>), not just worn.</summary>
    public List<SlotSaveData?> TorsoSlots { get; set; } = new();

    public int TorsoSlotCount { get; set; } = PlayerInventory.TorsoSlotCount;

    /// <summary>Whether a tank/filter/battery is actually loaded in the corresponding suit
    /// sub-slot — same DrillHasBattery/DrillCharge shape and reasoning, once per sub-slot.
    /// Meaningless (both default false/0) unless <see cref="HasEvaSuit"/> (or, for an old save,
    /// <see cref="TorsoItemId"/>) is also set.</summary>
    public bool HasSuitO2Tank { get; set; }

    public float SuitO2Charge { get; set; }

    public bool HasSuitN2Tank { get; set; }

    public float SuitN2Charge { get; set; }

    public bool HasSuitFilter { get; set; }

    public float SuitFilterCharge { get; set; }

    public bool HasSuitBattery { get; set; }

    public float SuitBatteryCharge { get; set; }

    /// <summary>Defaults to 0 — no old save ever had CO2 buildup, since the stat didn't exist
    /// before the EVA suit.</summary>
    public float CO2Percent { get; set; }

    public WindowPosition? SuitWindow { get; set; }

    /// <summary>Null when the PDA isn't currently worn — same nullable-item-id shape as
    /// <see cref="TorsoItemId"/>.</summary>
    public string? PdaItemId { get; set; }

    /// <summary>Whether the player possesses a PDA at all — worn, merely held, or stored —
    /// independent of <see cref="PdaItemId"/> (which only means "currently worn"). Same
    /// owned-anywhere-vs-worn shape as <see cref="HasEvaSuit"/>/<see cref="TorsoItemId"/>. False
    /// for an old save predating the PDA (falls back to PdaItemId at restore) or one with no PDA
    /// owned anywhere.</summary>
    public bool HasPda { get; set; }

    /// <summary>The PDA's one cartridge pocket — same SlotSaveData shape as <see
    /// cref="TorsoSlots"/>, no legacy-dict fallback needed (brand-new field). Captures contents
    /// whenever the PDA is owned at all (see <see cref="HasPda"/>), not just worn.</summary>
    public List<SlotSaveData?> PdaSlots { get; set; } = new();

    public int PdaSlotCount { get; set; } = PlayerInventory.PdaSlotCount;

    public WindowPosition? PdaWindow { get; set; }

    public WindowPosition? ThrusterWindow { get; set; }

    /// <summary>Accepted-but-not-yet-resolved contracts (see Player._activeContracts) — purely
    /// additive: empty for any save predating the contract system, same "missing = default"
    /// convention as every other collection field here.</summary>
    public List<ContractSaveData> ActiveContracts { get; set; } = new();

    /// <summary>Owed from a missed contract deadline, paid down capped at what's affordable
    /// whenever the ship docks at any Station (see Player.SettlePendingDebt) — 0 for any save
    /// predating the contract system, same "missing = default" convention as everything else
    /// here.</summary>
    public int PendingDebt { get; set; }
}

/// <summary>An accepted Contract's full save state — plain-DTO twin of the live Contract record,
/// same "domain type needs a save-safe copy" pattern DroppedContainerSaveData already uses for
/// ContainerPickupItem.</summary>
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

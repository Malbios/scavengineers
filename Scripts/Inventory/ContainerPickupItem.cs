using System.Collections.Generic;

using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Inventory;

/// <summary>
/// A full backpack sitting in the world (inventory arc Stage 3) — dropped by
/// Player.TryUnequipBackpack when a non-empty backpack is taken off, since its contents have no
/// fungible slot representation to fall back into. Structurally parallel to PickupItem, but
/// carries a live <see cref="SlotContainer"/> instead of a flat count — the same instance that
/// was equipped, contents intact.
/// </summary>
public partial class ContainerPickupItem : RigidBody3D, IVerbTarget
{
    private static readonly Verb PickUpVerb = new("pick_up", "VERB_PICK_UP", DurationSeconds: 0f);

    // See PickupItem's own ZeroGSettleDamp for why — same reasoning applies here.
    private const float ZeroGSettleDamp = 0.4f;

    [Export]
    public string ItemId { get; set; } = "";

    public SlotContainer? Contents { get; set; }

    public override void _Ready()
    {
        AddToGroup("dropped_container");
        LinearDamp = ZeroGSettleDamp;
        AngularDamp = ZeroGSettleDamp;

        // See PickupItem's own Freeze default for why — same reasoning applies here.
        Freeze = true;
    }

    public override void _PhysicsProcess(double delta) => ShipAtmosphereZone.UpdateFreezeState(this);

    public float? CurrentVerbProgress => null; // instant, never "in progress"

    public string? DisplayNameKey => "ITEM_" + ItemId.ToUpperInvariant();

    /// <summary>Pick Up only offered while the player's Back slot is free — a full backpack has
    /// no fungible slot to fall back into, so there's nowhere else for it to go.</summary>
    public IReadOnlyList<Verb> AvailableVerbs =>
        GetPlayer() is { } player && player.Inventory.Backpack is null ? [PickUpVerb] : [];

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != PickUpVerb.Id || Contents is null)
        {
            return;
        }

        if (inventory.EquipContainerDirectly(ItemId, Contents))
        {
            QueueFree();
        }
    }

    public void CancelVerb()
    {
        // Instant, already complete by the time anyone could cancel it — nothing to do.
    }

    // Resolved fresh on every access rather than cached in _Ready — see StationConsoleVerbTarget's
    // own GetPlayer for why (scene-tree "player" group membership order isn't guaranteed yet).
    private PlayerScript? GetPlayer() => GetTree().GetFirstNodeInGroup("player") as PlayerScript;
}

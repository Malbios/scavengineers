using System.Collections.Generic;

using Godot;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Verbs;
using Scavengineers.Sim.Grid;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Inventory;

/// <summary>A full backpack sitting in the world — dropped when a non-empty backpack is taken
/// off, since its contents have no fungible slot representation to fall back into. Structurally
/// parallel to PickupItem, but carries a live <see cref="SlotContainer"/> instead of a flat count.</summary>
public partial class ContainerPickupItem : RigidBody3D, IVerbTarget, IPhysicsPresenceAware
{
    private static readonly Verb PickUpVerb = new("pick_up", "VERB_PICK_UP", DurationSeconds: 0f);

    // See PickupItem's own ZeroGSettleDamp for why — same reasoning applies here.
    private const float ZeroGSettleDamp = 0.4f;

    // See PickupItem's own DecompressionPullRange/DecompressionPullAcceleration/
    // BreachEjectDistance/BreachEjectOffset for why — same reasoning applies here.
    private const float DecompressionPullRange = 5f;
    private const float DecompressionPullAcceleration = 4f;
    private const float BreachEjectDistance = 0.3f;
    private const float BreachEjectOffset = 1f;

    private bool _hasSettled;
    private bool _ejected;

    [Export]
    public string ItemId { get; set; } = "";

    /// <summary>Which equip slot this container re-equips into on pickup — "back" for a dropped
    /// backpack, "torso" for a dropped EVA suit torso piece.</summary>
    [Export]
    public string EquipSlotName { get; set; } = "back";

    public SlotContainer? Contents { get; set; }

    /// <summary>The EVA suit torso's tank/filter/battery sub-slot state at the moment it was
    /// discarded — null for anything that isn't the suit. Restored via AttachSpecializedSlot on
    /// pickup so a discarded suit's tanks travel with it into the world and back.</summary>
    public (bool HasItem, float Charge)? SuitO2 { get; set; }

    public (bool HasItem, float Charge)? SuitN2 { get; set; }

    public (bool HasItem, float Charge)? SuitFilter { get; set; }

    public (bool HasItem, float Charge)? SuitBattery { get; set; }

    public override void _Ready()
    {
        var shapeKind = ItemCatalog.ShapeKind(ItemId);
        AddChild(ItemVisualBuilder.BuildVisual(ItemId, shapeKind));
        AddChild(new CollisionShape3D { Shape = ItemVisualBuilder.BuildCollisionShape(shapeKind) });

        AddToGroup("dropped_container");
        LinearDamp = ZeroGSettleDamp;
        AngularDamp = ZeroGSettleDamp;

        // See PickupItem's own Freeze default for why — same reasoning applies here.
        Freeze = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_ejected)
        {
            return; // see PickupItem's own _PhysicsProcess for why
        }

        if (!_hasSettled)
        {
            _hasSettled = true;
            Freeze = false;
            return; // one-time startup-race dodge only — see PickupItem's own _PhysicsProcess
        }

        UpdateBreachPull(delta);
    }

    // See PickupItem's own UpdateBreachPull for why — same reasoning applies here.
    private void UpdateBreachPull(double delta)
    {
        var zone = ShipAtmosphereZone.FindZoneAt(GetWorld3D(), GlobalPosition);
        if (zone?.BuildTargetRef is not { } buildTarget)
        {
            return;
        }

        var tile = zone.TileAt(GlobalPosition);
        var cell = new CellCoord(tile.X, tile.Y);
        var roomVolume = zone.ShipSimRef?.VolumeAt(cell);
        var inZeroG = (roomVolume?.O2Fraction ?? 0.21) <= ShipAtmosphereZone.ZeroGO2Threshold;
        if (!inZeroG)
        {
            return;
        }

        // See PickupItem's own IsConnectedToOutside check for why — same reasoning applies here.
        if (!(zone.ShipSimRef?.Atmosphere?.IsConnectedToOutside(cell) ?? false))
        {
            return;
        }

        foreach (var breachPosition in buildTarget.ActiveBreachPositions())
        {
            // See PickupItem's own same-zone check for why — same reasoning applies here.
            if (!ReferenceEquals(ShipAtmosphereZone.FindZoneAt(GetWorld3D(), breachPosition), zone))
            {
                continue;
            }

            var toBreach = breachPosition - GlobalPosition;
            var distance = toBreach.Length();

            if (distance < BreachEjectDistance)
            {
                Eject(breachPosition, toBreach);
                return;
            }

            if (distance > DecompressionPullRange)
            {
                continue;
            }

            LinearVelocity += toBreach.Normalized() * DecompressionPullAcceleration * (float)delta;
        }
    }

    // See PickupItem's own Eject for why — same reasoning applies here.
    private void Eject(Vector3 breachPosition, Vector3 toBreach)
    {
        _ejected = true;
        var direction = toBreach.LengthSquared() > 0.0001f ? toBreach.Normalized() : Vector3.Up;
        GlobalPosition = breachPosition + direction * BreachEjectOffset;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        Freeze = true;
        SetPhysicsProcess(false);
    }

    // See PickupItem's own SetPhysicsPresence for why — same reasoning applies here.
    public void SetPhysicsPresence(bool present)
    {
        if (_ejected)
        {
            return;
        }

        Freeze = true;

        if (present)
        {
            _hasSettled = false;
            SetPhysicsProcess(true);
        }
        else
        {
            LinearVelocity = Vector3.Zero;
            AngularVelocity = Vector3.Zero;
            SetPhysicsProcess(false); // don't let the one-shot _PhysicsProcess undo this freeze
        }
    }

    public float? CurrentVerbProgress => null; // instant, never "in progress"

    public string? DisplayNameKey => "ITEM_" + ItemId.ToUpperInvariant();

    /// <summary>Only offered while the target equip slot is free — a full container has nowhere
    /// else to go.</summary>
    public IReadOnlyList<Verb> AvailableVerbs =>
        GetPlayer() is { } player && player.Inventory.IsContainerSlotFree(EquipSlotName) ? [PickUpVerb] : [];

    public void ExecuteVerb(Verb verb, PlayerInventory inventory)
    {
        if (verb.Id != PickUpVerb.Id || Contents is null)
        {
            return;
        }

        if (inventory.EquipContainerDirectly(EquipSlotName, ItemId, Contents))
        {
            if (SuitO2 is { } o2) inventory.AttachSpecializedSlot("suit_o2", o2.HasItem, o2.Charge);
            if (SuitN2 is { } n2) inventory.AttachSpecializedSlot("suit_n2", n2.HasItem, n2.Charge);
            if (SuitFilter is { } filter) inventory.AttachSpecializedSlot("suit_filter", filter.HasItem, filter.Charge);
            if (SuitBattery is { } battery) inventory.AttachSpecializedSlot("suit_battery", battery.HasItem, battery.Charge);

            QueueFree();
        }
    }

    public void CancelVerb()
    {
        // Instant, already complete by the time anyone could cancel it — nothing to do.
    }

    // Resolved fresh on every access rather than cached in _Ready — scene-tree "player" group
    // membership order isn't guaranteed yet at that point.
    private PlayerScript? GetPlayer() => GetTree().GetFirstNodeInGroup("player") as PlayerScript;
}

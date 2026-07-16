using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Ship;
using Scavengineers.Scripts.Shop;
using Scavengineers.Scripts.Travel;
using Scavengineers.Scripts.Verbs;

namespace Scavengineers.Scripts.Player;

public partial class Player : CharacterBody3D
{
    private const float MoveSpeed = 4.0f;
    private const float Gravity = 9.8f;
    private const float MouseSensitivity = 0.0025f;
    private const float MaxPitchRadians = Mathf.Pi / 2 - 0.05f;

    // Placeholder/tunable — 25 wall/floor/ceiling actions per full battery.
    private const float DrillChargeDrainPerUse = 0.04f;

    // Placeholder/tunable — ~15 min of continuous use per full battery, same pacing the
    // flashlight's old generic-Power drain used before Power was removed as a player stat.
    private const float FlashlightChargeDrainPerSecond = 1f / 900f;

    // Placeholder/tunable — the Hunger/Thirst/Energy-at-0% debuff (a slowdown, not a failure
    // state; that's O2/Health's job — see SuitResources).
    private const float NeedsDebuffMoveMultiplier = 0.5f;

    // Zero-g movement (placeholder/tunable) — triggers whenever the room's own O2 reads at or
    // below ShipAtmosphereZone.ZeroGO2Threshold (see ShipSimRef.VolumeAt), same "vacuum" threshold
    // spirit as AtmosphereVolume.Vacuum's O2Fraction of 0, with a little slack so the switch
    // doesn't flicker right at the exact boundary while a room is still venting/equalizing. Shared
    // with ShipAtmosphereZone's own real physics zero-g override for loose items — one definition
    // of "this room counts as vacuum" for both.
    private const float ZeroGThrustAcceleration = 6f;
    private const float ZeroGDrag = 2f; // passive deceleration per second, always applied
    private const float ZeroGMaxSpeed = 3.5f;

    // How close a wall/floor/ceiling/device has to be for zero-g thrust to work at all — no
    // jetpack yet, so control only comes from pushing off something within reach.
    private const float ZeroGControlRadius = 1.2f;

    // Placeholder/tunable — CharacterBody3D doesn't push RigidBody3D obstacles on its own
    // (MoveAndSlide resolves the character's own motion but never applies a reciprocal impulse to
    // whatever it collided with), so a loose item the player walks into would otherwise never
    // move (see PickupItem). Only a briefly-frozen item (its own one-tick startup grace right
    // after spawning) ignores the impulse — everything else is a live physics body, whether
    // resting under normal gravity or drifting in zero-g.
    private const float ItemPushImpulse = 2f;

    // Decompression pull (placeholder/tunable) — an open floor/ceiling breach's own "unsecured
    // near a hole" hazard, on top of (not instead of) the slow O2/pressure drain. Pulls toward
    // the breach's own position rather than a fixed direction, so it settles near the hole
    // instead of launching you straight through it — ZeroGDrag keeps fighting it the whole time.
    private const float DecompressionPullRange = 5f;
    private const float DecompressionPullAcceleration = 4f;

    // Placeholder/tunable — comfortably more than any current room's floor-to-ceiling height
    // (3m), so a normal room can never trip this; revisit once multi-deck verticality exists.
    private const float FreefallRaycastDistance = 15f;

    /// <summary>Which ship (and which of its tiles) currently governs the player's ambient O2
    /// reading — set at runtime by whichever <see cref="Scavengineers.Scripts.Ship.ShipAtmosphereZone"/>
    /// the player is standing in. Both ships (and both of a ship's rooms) are loaded and
    /// simulated simultaneously, and a room can now be sealed off from the rest of its own
    /// ship, so this has to follow the player's actual room, not just which ship they're on.</summary>
    public ShipSim? ShipSimRef { get; private set; }

    private Vector2I _ambientTile;

    /// <summary>The current room's floor/ceiling breach tracker, if it has one — null on ships
    /// without floor/ceiling construction (see ShipAtmosphereZone.BuildTargetRef). Drives the
    /// decompression-pull hazard in the zero-g branch of _PhysicsProcess.</summary>
    private ShipBuildTarget? _ambientBuildTarget;

    /// <summary>Queries physics space for whichever ShipAtmosphereZone currently contains the
    /// player, every physics frame, rather than caching whatever the last BodyEntered signal
    /// said. That signal-based approach missed real transitions in practice — crossing a shared
    /// zone boundary within a single physics tick never fires "entered" at all — leaving the
    /// ambient O2 reading stuck on a stale ship/room (permanently wrong if that ship never
    /// regenerates air). A live query can't go stale: if no zone is found (e.g. a true gap
    /// between two rooms), the previous reading is deliberately left alone rather than cleared,
    /// same "hold the last room's reading" behavior as before.
    ///
    /// Reads the zone's TileAt(GlobalPosition) — the player's own actual current cell — rather
    /// than the zone's fixed representative Tile: atmosphere now diffuses per-cell instead of
    /// equalizing a whole room instantly, so a cell right next to a fresh breach can genuinely
    /// read very differently from one across the same room.
    ///
    /// Returns the zone actually found THIS frame (as opposed to the cached fields above, which
    /// may still hold an older room's data) — the decompression-pull hazard needs this live
    /// signal specifically: once the player has actually drifted out through a breach and left
    /// the room's zone, continuing to pull toward that same fixed breach position from the far
    /// side would pull them back inside, causing a pendulum (out, back in, out again) instead of
    /// a clean exit. It also lets the pull tell "a breach in MY room" apart from "a breach in some
    /// other room the atmosphere sim happens to still consider connected" (e.g. through a door
    /// that's since been opened) — see the pull block's own same-zone check. Every other use of
    /// the cached fields (O2 readout, suit drain) intentionally keeps holding the last known room
    /// even outside a zone, so this doesn't change that.</summary>
    private ShipAtmosphereZone? UpdateAmbientShipSim()
    {
        if (ShipAtmosphereZone.FindZoneAt(GetWorld3D(), GlobalPosition) is { } zone)
        {
            ShipSimRef = zone.ShipSimRef;
            _ambientTile = zone.TileAt(GlobalPosition);
            _ambientBuildTarget = zone.BuildTargetRef;
            return zone;
        }

        return null;
    }

    private Node3D? _head;
    private RayCast3D? _interactRay;
    private Camera3D? _camera;
    private Label? _targetNameLabel;
    private Label? _verbLabel;
    private ProgressBar? _verbProgressBar;
    private ProgressBar? _o2Bar;
    private ProgressBar? _healthBar;
    private Label? _roomO2Label;
    private Label? _leftHandLabel;
    private Label? _rightHandLabel;
    private Label? _creditsLabel;
    private Control? _inventoryPanel;
    private Control? _drillWindow;
    private Control? _flashlightWindow;
    private Control? _backpackWindow;
    private TravelMapPanel? _travelMapPanel;
    private TravelConsoleVerbTarget? _openTravelConsole;
    private ShopPanel? _shopPanel;
    private StationConsoleVerbTarget? _openShopConsole;
    private WorldDropZone? _worldDropZone;
    private Control? _backpackGrid;
    private InventorySlotUI? _backpackSlotTemplate;
    private readonly List<InventorySlotUI> _backpackSlotUIs = new();

    /// <summary>Slot count the backpack grid was last built for — <see cref="UpdateInventoryHud"/>
    /// only rebuilds the grid's <see cref="InventorySlotUI"/> children when this changes (equip/
    /// unequip/load), not every frame.</summary>
    private int _backpackSlotUICount = -1;
    private readonly SuitResources _suitResources = new();
    private readonly PlayerNeeds _needs = new();
    private readonly PlayerInventory _inventory = new();
    private float _pitch;

    private ProgressBar? _hungerBar;
    private ProgressBar? _thirstBar;
    private ProgressBar? _energyBar;
    private Label? _drillLabel;
    private ProgressBar? _drillBar;
    private Label? _flashlightLabel;
    private ProgressBar? _flashlightBar;
    private SpotLight3D? _flashlightSpot;
    private bool _flashlightOn;
    private ColorRect? _smokeOverlay;
    private ColorRect? _coldOverlay;
    private ColorRect? _burnOverlay;

    /// <summary>The generic dropped-item visual (same box mesh + per-item material-override
    /// pattern InventoryOverflow.DropAt and ShipBuildTarget's own refunds already use) — reused
    /// here for a dropped, contents-full backpack (see SpawnDroppedBackpack). Wired in World.tscn
    /// to the same shared sub-resources.</summary>
    [Export]
    public Mesh? DroppedItemMesh { get; set; }

    [Export]
    public Shape3D? DroppedItemShape { get; set; }

    [Export]
    public Material? DroppedItemMaterial { get; set; }

    /// <summary>The death fallback's reload target — SaveManager already holds the reverse
    /// PlayerRef (World.tscn), this is the same reference the other way round.</summary>
    [Export]
    public SaveManager? SaveManagerRef { get; set; }

    /// <summary>Whether the mouse-driven inventory panel (Tab) is currently open — while true,
    /// look/Interact/verb-cycling/mouse-recapture are all suppressed so clicking and dragging in
    /// the panel doesn't also fire world interactions or yank the mouse back into captured mode
    /// mid-drag.</summary>
    private bool _inventoryOpen;

    /// <summary>Whether the travel console's map screen is currently open — same "suppress
    /// look/Interact/verb-cycling/mouse-recapture" gating as _inventoryOpen, via <see
    /// cref="AnyPanelOpen"/>, so opening the map mid-game doesn't also let the player walk off or
    /// interact with something else behind it.</summary>
    private bool _travelMapOpen;

    /// <summary>Whether the station trade console's shop screen is currently open — same
    /// suppress-everything gating as _inventoryOpen/_travelMapOpen, via <see cref="AnyPanelOpen"/>.
    /// Interact() is already gated by AnyPanelOpen, so Shop and the travel map can never both be
    /// open at once — there's no ordering to get wrong between them.</summary>
    private bool _shopOpen;

    /// <summary>Any full-screen-ish HUD panel that should suppress normal gameplay input while
    /// open — shared gate for every _Input branch that used to check _inventoryOpen alone.</summary>
    private bool AnyPanelOpen => _inventoryOpen || _travelMapOpen || _shopOpen;

    /// <summary>The game's whole known item catalog, doubling as the hotbar slots (keys 1-9, 0) —
    /// also reused by StationConsoleVerbTarget as the set of things Buy can offer, since there's
    /// no separate item-definition data yet. "backpack" is an ordinary holdable/purchasable item
    /// right up until it's actually equipped via drag-and-drop onto the Back slot (see
    /// TryEquipBackpackFromHand) — no dedicated verb needed to buy or hold one. "ration_bar"/
    /// "water_bottle" are likewise ordinary holdable items until F consumes whichever's held
    /// (see UseHeldItem) — no dedicated equip path either.</summary>
    public static readonly string[] HotbarItems = ["scrap_metal", "spare_parts", "wall_panel", "power_cell", "battery", "switch", "recharge_station", "backpack", "ration_bar", "water_bottle"];

    private enum Hand { Left, Right }

    /// <summary>Which hand was filled most recently — the only piece of state hands need beyond
    /// PlayerInventory's own slots, since "which hand to replace when both are full" can't be
    /// derived from the slots themselves.</summary>
    private Hand? _lastFilledHand;

    public string? LeftHandItemId => _inventory.Slots[PlayerInventory.LeftHandSlotIndex]?.ItemId;

    public string? RightHandItemId => _inventory.Slots[PlayerInventory.RightHandSlotIndex]?.ItemId;

    public PlayerInventory Inventory => _inventory;

    // Placeholder starting stipend — tunable. Not modeled as an ItemRequirement (those pull
    // from PlayerInventory, a different resource) — StationConsoleVerbTarget checks/spends
    // this directly via the Player reference it already resolves.
    private int _credits = 20;

    public int Credits => _credits;

    public void AddCredits(int amount) => _credits += amount;

    public bool TrySpendCredits(int amount)
    {
        if (_credits < amount)
        {
            return false;
        }

        _credits -= amount;
        return true;
    }

    /// <summary>The build target whose ghost we last turned on — tracked so we can turn it back
    /// off the moment the player looks somewhere else (SetGhostVisible is only ever called on
    /// whichever build target is the *current* raycast target).</summary>
    private ShipBuildTarget? _activeBuildTarget;

    /// <summary>Set while a timed verb we started is still running — occupies the player,
    /// locking movement/look, until <see cref="IVerbTarget.CurrentVerbProgress"/> goes back
    /// to null (reuses the existing progress signal instead of a separate completion event).</summary>
    private IVerbTarget? _busyTarget;

    /// <summary>The verb that made us busy — kept so a cancel can refund its Requirements.
    /// Naturally finishing does NOT refund; only an explicit cancel does.</summary>
    private Verb? _busyVerb;

    /// <summary>Which of the current target's affordable verbs is highlighted — cycled by the
    /// scroll wheel, executed by right-click. Reset to 0 whenever the aimed target changes so
    /// a stale selection from a different object never carries over.</summary>
    private int _selectedVerbIndex;

    private IVerbTarget? _lastTarget;

    private bool IsBusy => _busyTarget is not null;

    public override void _Ready()
    {
        _head = GetNode<Node3D>("Head");
        _interactRay = GetNode<RayCast3D>("Head/Camera3D/InteractRay");
        _camera = GetNode<Camera3D>("Head/Camera3D");
        _targetNameLabel = GetNode<Label>("HUD/TargetNameLabel");
        _verbLabel = GetNode<Label>("HUD/VerbLabel");
        _verbProgressBar = GetNode<ProgressBar>("HUD/VerbProgressBar");
        _o2Bar = GetNode<ProgressBar>("HUD/ResourcesPanel/O2Bar");
        _healthBar = GetNode<ProgressBar>("HUD/ResourcesPanel/HealthBar");
        _hungerBar = GetNode<ProgressBar>("HUD/ResourcesPanel/HungerBar");
        _thirstBar = GetNode<ProgressBar>("HUD/ResourcesPanel/ThirstBar");
        _energyBar = GetNode<ProgressBar>("HUD/ResourcesPanel/EnergyBar");
        _roomO2Label = GetNode<Label>("HUD/ResourcesPanel/RoomO2Label");
        _drillLabel = GetNode<Label>("HUD/ResourcesPanel/DrillLabel");
        _drillBar = GetNode<ProgressBar>("HUD/ResourcesPanel/DrillBar");
        _flashlightLabel = GetNode<Label>("HUD/ResourcesPanel/FlashlightLabel");
        _flashlightBar = GetNode<ProgressBar>("HUD/ResourcesPanel/FlashlightBar");
        _flashlightSpot = GetNode<SpotLight3D>("Head/Camera3D/FlashlightSpot");
        _smokeOverlay = GetNode<ColorRect>("HUD/SmokeOverlay");
        _coldOverlay = GetNode<ColorRect>("HUD/ColdOverlay");
        _burnOverlay = GetNode<ColorRect>("HUD/BurnOverlay");
        _leftHandLabel = GetNode<Label>("HUD/LeftHandLabel");
        _rightHandLabel = GetNode<Label>("HUD/RightHandLabel");
        _creditsLabel = GetNode<Label>("HUD/CreditsLabel");
        _inventoryPanel = GetNode<Control>("HUD/InventoryPanel");
        _drillWindow = GetNode<Control>("HUD/DrillWindow");
        _flashlightWindow = GetNode<Control>("HUD/FlashlightWindow");
        _backpackWindow = GetNode<Control>("HUD/BackpackWindow");
        _backpackGrid = GetNode<Control>("HUD/BackpackWindow/Layout/BackpackGrid");
        _travelMapPanel = GetNode<TravelMapPanel>("HUD/TravelMapPanel");
        _travelMapPanel.PlayerRef = this;
        _shopPanel = GetNode<ShopPanel>("HUD/ShopPanel");
        _shopPanel.PlayerRef = this;
        _worldDropZone = GetNode<WorldDropZone>("HUD/WorldDropZone");
        _worldDropZone.PlayerRef = this;

        foreach (var child in GetNode("HUD/InventoryPanel/Layout/EquipSlots").GetChildren())
        {
            if (child is InventorySlotUI slot)
            {
                slot.Container = _inventory.Hands;
                slot.PlayerRef = this;
            }
        }

        GetNode<InventorySlotUI>("HUD/DrillWindow/Layout/DrillBatterySlot").PlayerRef = this;
        GetNode<InventorySlotUI>("HUD/FlashlightWindow/Layout/FlashlightBatterySlot").PlayerRef = this;

        _backpackSlotTemplate = GetNode<InventorySlotUI>("HUD/BackpackWindow/Layout/BackpackGrid/SlotTemplate");

        // Placeholder/tunable starting stipend for testing the free-form conduit wiring
        // extensively — same "don't wait on it" spirit as the near-instant verb durations.
        // Overwritten by ApplyPlayerState on load, same as every other fresh-start default.
        // The debug backpack is attached first (dev convenience, not something you "found" —
        // bypasses the normal hand-then-equip flow entirely) so the stipend below spills into
        // its 24 slots instead of overflowing two bare hands.
        _inventory.EquipContainerDirectly("back", "debug_backpack", new SlotContainer(24));
        _inventory.Add("scrap_metal", 50);
        _inventory.Add("ration_bar", 3);
        _inventory.Add("water_bottle", 3);
        _inventory.Add("crowbar", 1);
        _inventory.Add("power_drill", 1);
        _inventory.AttachSpecializedSlot("drill_battery", hasItem: true, charge: 1f);
        _inventory.Add("flashlight", 1);
        _inventory.AttachSpecializedSlot("flashlight_battery", hasItem: true, charge: 1f);
        _inventory.Add("debug_flashlight", 1);
        _flashlightOn = true; // starts on, same as before this was toggleable, but F now turns it off too

        CaptureMouse();
        // Setting MouseMode here alone is unreliable if the window doesn't yet have OS
        // input focus at this exact point in startup — reapply whenever focus is (re)gained.
        GetWindow().FocusEntered += CaptureMouse;

        AddToGroup("player");
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured && !IsBusy && !AnyPanelOpen)
        {
            RotateY(-mouseMotion.Relative.X * MouseSensitivity);

            _pitch = Mathf.Clamp(_pitch - mouseMotion.Relative.Y * MouseSensitivity, -MaxPitchRadians, MaxPitchRadians);
            if (_head is not null)
            {
                _head.Rotation = new Vector3(_pitch, 0, 0);
            }
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && !IsBusy && !AnyPanelOpen)
        {
            Interact();
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } && !IsBusy && !AnyPanelOpen)
        {
            CycleSelectedVerb(1);
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } && !IsBusy && !AnyPanelOpen)
        {
            CycleSelectedVerb(-1);
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && IsBusy)
        {
            _busyTarget!.CancelVerb();

            foreach (var requirement in _busyVerb!.Requirements.Where(r => r.Consumed))
            {
                _inventory.Add(requirement.ItemId, requirement.Count);
            }

            _busyTarget = null;
            _busyVerb = null;
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }
                 && Input.MouseMode != Input.MouseModeEnum.Captured && !AnyPanelOpen)
        {
            CaptureMouse();
        }
        else if (@event is InputEventKey { Keycode: Key.Tab, Pressed: true } && !IsBusy && !_travelMapOpen && !_shopOpen)
        {
            if (_inventoryOpen)
            {
                CloseInventory();
            }
            else
            {
                OpenInventory();
            }
        }
        else if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            if (_travelMapOpen)
            {
                CloseTravelMap();
            }
            else if (_shopOpen)
            {
                CloseShop();
            }
            else if (_inventoryOpen)
            {
                CloseInventory();
            }
            else
            {
                Input.MouseMode = Input.MouseModeEnum.Visible;
            }
        }
        else if (@event is InputEventKey { Keycode: Key.F, Pressed: true } && !IsBusy && !AnyPanelOpen)
        {
            UseHeldItem();
        }
        else if (@event is InputEventKey { Pressed: true } hotbarKey && !IsBusy)
        {
            var index = hotbarKey.Keycode switch
            {
                Key.Key1 => 0,
                Key.Key2 => 1,
                Key.Key3 => 2,
                Key.Key4 => 3,
                Key.Key5 => 4,
                Key.Key6 => 5,
                Key.Key7 => 6,
                Key.Key8 => 7,
                Key.Key9 => 8,
                Key.Key0 => 9,
                _ => -1,
            };

            if (index >= 0 && index < HotbarItems.Length && _inventory.Has(HotbarItems[index], 1))
            {
                ToggleHeldItem(HotbarItems[index]);
            }
        }
    }

    /// <summary>Direct hotbar-style action on whatever's held, not a raycast-targeted verb, since
    /// there's no world object involved. Eats/drinks a consumable (left hand first, then right)
    /// if one's held; otherwise toggles the flashlight on/off — a held real flashlight, or the
    /// debug flashlight if it's in inventory (it's never "held," so it has no hand-slot check of
    /// its own). A no-op if neither applies.</summary>
    private void UseHeldItem()
    {
        if (TryConsumeHand(PlayerInventory.LeftHandSlotIndex) || TryConsumeHand(PlayerInventory.RightHandSlotIndex))
        {
            return;
        }

        if (LeftHandItemId == "flashlight" || RightHandItemId == "flashlight" || _inventory.HasAny(ItemCatalog.IsToggleableLight))
        {
            _flashlightOn = !_flashlightOn;
        }
    }

    private bool TryConsumeHand(int handIndex)
    {
        if (_inventory.Hands.Slots[handIndex]?.ItemId is not { } itemId)
        {
            return false;
        }

        var hunger = ItemCatalog.HungerRestore(itemId);
        var thirst = ItemCatalog.ThirstRestore(itemId);
        if (hunger <= 0 && thirst <= 0)
        {
            return false;
        }

        if (!_inventory.TryRemoveFromHand(handIndex, 1))
        {
            return false;
        }

        _needs.Eat(hunger);
        _needs.Drink(thirst);
        return true;
    }

    /// <summary>Hotbar-key toggle, extended from a single held-item slot to two hands: already
    /// held in a hand -> unequip that hand (toggle off, same as today's single-hand behavior).
    /// Otherwise -> fill whichever hand is empty, or if both are full, replace whichever was
    /// filled most recently (Equip's own MoveBetween already swaps the displaced hand contents
    /// back into the backpack the new item came from, so no separate unequip step is needed for
    /// the replace case).</summary>
    private void ToggleHeldItem(string itemId)
    {
        if (LeftHandItemId == itemId)
        {
            _inventory.Unequip(PlayerInventory.LeftHandSlotIndex);
            return;
        }

        if (RightHandItemId == itemId)
        {
            _inventory.Unequip(PlayerInventory.RightHandSlotIndex);
            return;
        }

        if (LeftHandItemId is null)
        {
            _inventory.Equip(itemId, PlayerInventory.LeftHandSlotIndex);
            _lastFilledHand = Hand.Left;
            return;
        }

        if (RightHandItemId is null)
        {
            _inventory.Equip(itemId, PlayerInventory.RightHandSlotIndex);
            _lastFilledHand = Hand.Right;
            return;
        }

        var targetHand = _lastFilledHand ?? Hand.Left;
        var targetHandIndex = targetHand == Hand.Left ? PlayerInventory.LeftHandSlotIndex : PlayerInventory.RightHandSlotIndex;
        _inventory.Equip(itemId, targetHandIndex);
        _lastFilledHand = targetHand;
    }

    /// <summary>Called by InventorySlotUI's Back slot when something is dragged onto it — equips
    /// a backpack only if the dragged hand slot really is one (a sensible drag gesture check;
    /// PlayerInventory.EquipBackpackFromHand doesn't care which exact hand it came from, since
    /// the item is fungible right up until the moment it's equipped).</summary>
    public void TryEquipBackpackFromHand(int fromSlotIndex)
    {
        if (fromSlotIndex < 0 || fromSlotIndex >= _inventory.Hands.Slots.Count)
        {
            return;
        }

        if (_inventory.Hands.Slots[fromSlotIndex]?.ItemId != "backpack")
        {
            return;
        }

        _inventory.EquipBackpackFromHand();
    }

    /// <summary>Called by an ordinary hand slot's InventorySlotUI when the equipped backpack
    /// itself (dragged from the Back slot) is dropped onto it. An empty backpack becomes a
    /// fungible item again if a hand fits it (staying equipped if both hands are full —
    /// "nothing vanishes"); a non-empty one can't fall back into a hand at all, so it's dropped
    /// in the world instead, contents intact.</summary>
    public void TryUnequipBackpack()
    {
        if (_inventory.Backpack is not { } backpack)
        {
            return;
        }

        var isEmpty = backpack.Contents.Slots.All(s => s is null);
        if (isEmpty)
        {
            if (_inventory.Hands.Add(backpack.ItemId, 1) == 1)
            {
                _inventory.ClearBackpack();
            }

            return;
        }

        _inventory.ClearBackpack();
        SpawnDroppedContainer(backpack.ItemId, backpack.Contents, GlobalPosition);
    }

    /// <summary>Spawns a full container's world representation at `position` — used both for an
    /// unequip-while-full drop (see TryUnequipBackpack) and by SaveManager to respawn dropped
    /// containers on load. Reuses the same generic dropped-item visual
    /// (Mesh/Shape/Material pattern InventoryOverflow.DropAt and ShipBuildTarget's own refunds
    /// already use) via this Player's own wired exports.</summary>
    public void SpawnDroppedContainer(string itemId, SlotContainer contents, Vector3 position)
    {
        var pickup = new ContainerPickupItem { ItemId = itemId, Contents = contents };
        GetParent()?.AddChild(pickup);
        pickup.GlobalPosition = position;

        var meshInstance = new MeshInstance3D { Mesh = DroppedItemMesh };
        meshInstance.SetSurfaceOverrideMaterial(0, ItemCatalog.TintedMaterial(itemId, DroppedItemMaterial));
        pickup.AddChild(meshInstance);

        pickup.AddChild(new CollisionShape3D { Shape = DroppedItemShape });
    }

    // Placeholder/tunable — how far you can reach to place a dropped item.
    private const float MaxDropReachMeters = 3.0f;

    /// <summary>Called by the world-drop zone (see WorldDropZone._DropData) once a drag ends
    /// outside every existing panel/slot Control. A no-op if nothing solid is within reach in the
    /// drop's screen direction.</summary>
    public void TryDropInWorld(InventorySlotUI source, Vector2 screenPosition)
    {
        if (ResolveWorldDropPosition(screenPosition) is not { } position)
        {
            return;
        }

        if (source.SpecializedSlotKey.Length > 0)
        {
            if (_inventory.EjectSpecializedSlotForWorld(source.SpecializedSlotKey) is { } charge
                && PlayerInventory.SpecializedSlotAcceptedItemId(source.SpecializedSlotKey) is { } itemId)
            {
                InventoryOverflow.DropAt(this, itemId, 1, DroppedItemMesh!, DroppedItemShape!, DroppedItemMaterial, position, charge);
            }

            return;
        }

        if (source.IsBackSlot)
        {
            DropBackpackInWorld(position);
            return;
        }

        if (source.Container is not { } container || source.SlotIndex < 0 || source.SlotIndex >= container.Slots.Count
            || container.Slots[source.SlotIndex] is not { } slot)
        {
            return;
        }

        container.SetSlot(source.SlotIndex, null);
        InventoryOverflow.DropAt(this, slot.ItemId, slot.Count, DroppedItemMesh!, DroppedItemShape!, DroppedItemMaterial, position, slot.Charge);
    }

    /// <summary>Always drops the backpack (empty or not) at `position` — unlike
    /// TryUnequipBackpack's onto-a-hand-slot gesture, which prefers stashing an empty backpack
    /// back into a hand instead. Dragging it out into the world is a deliberate "put it down," so
    /// it always ends up loose.</summary>
    private void DropBackpackInWorld(Vector3 position)
    {
        if (_inventory.Backpack is not { } backpack)
        {
            return;
        }

        _inventory.ClearBackpack();
        SpawnDroppedContainer(backpack.ItemId, backpack.Contents, position);
    }

    /// <summary>Projects a ray from the camera through the drop's screen position — the first
    /// mouse-position-based raycast in this codebase (InteractRay is a fixed forward crosshair
    /// ray). Returns null (refuse the drop) if nothing solid is within MaxDropReachMeters, e.g.
    /// aiming out an open breach or off a platform's edge.</summary>
    private Vector3? ResolveWorldDropPosition(Vector2 screenPosition)
    {
        if (_camera is null)
        {
            return null;
        }

        var from = _camera.ProjectRayOrigin(screenPosition);
        var direction = _camera.ProjectRayNormal(screenPosition);
        var query = PhysicsRayQueryParameters3D.Create(from, from + direction * MaxDropReachMeters, _interactRay!.CollisionMask,
            new Godot.Collections.Array<Rid> { GetRid() });
        var result = GetWorld3D().DirectSpaceState.IntersectRay(query);

        if (result.Count == 0)
        {
            return null;
        }

        // Nudge off the surface along its normal so the item doesn't spawn half-embedded.
        return (Vector3)result["position"] + (Vector3)result["normal"] * 0.05f;
    }

    /// <summary>Test harnesses that instantiate a real Player (see
    /// Scavengineers.NodeTests/PlayerTestHarness.cs) run inside a real, non-headless Godot
    /// window (GdUnit4's own test runner) — without this, every such test would capture the
    /// developer's actual OS mouse into that window for the run's duration. Off by default; only
    /// ever set by test code.</summary>
    public static bool SuppressMouseCaptureForTests { get; set; }

    // Instance method, not static: the FocusEntered connection below is then tied to this
    // Player's lifetime, so Godot auto-disconnects it when this instance is freed (e.g. on a
    // scene change). A static method's connection has no instance to track, so travelling
    // between scenes would try to reconnect the exact same callable and hit "already connected."
    private void CaptureMouse()
    {
        if (SuppressMouseCaptureForTests)
        {
            return;
        }

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void OpenInventory()
    {
        _inventoryOpen = true;
        if (_inventoryPanel is not null)
        {
            _inventoryPanel.Visible = true;
        }

        _worldDropZone!.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void CloseInventory()
    {
        _inventoryOpen = false;
        if (_inventoryPanel is not null)
        {
            _inventoryPanel.Visible = false;
        }

        // None of the item windows are useful without the main panel open to drag items to/from.
        _drillWindow!.Visible = false;
        _flashlightWindow!.Visible = false;
        _backpackWindow!.Visible = false;
        _worldDropZone!.Visible = false;

        CaptureMouse();
    }

    /// <summary>Called by TravelConsoleVerbTarget.ExecuteVerb — opens the map instead of that
    /// verb starting travel directly, same shape as OpenInventory but triggered from a world
    /// interaction rather than a hotkey.</summary>
    public void OpenTravelMap(TravelConsoleVerbTarget console)
    {
        _travelMapOpen = true;
        _openTravelConsole = console;
        _travelMapPanel!.Populate(console.BuildMapEntries(), console.CurrentDestinationId);
        _travelMapPanel.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Called by TravelMapPanel's Travel button — hands the chosen destination back to
    /// whichever console opened the map, then closes it regardless of whether travel actually
    /// started (BeginTravel itself no-ops for an already-current/out-of-range destination).</summary>
    public void ConfirmTravel(int destinationId)
    {
        _openTravelConsole?.BeginTravel(destinationId);
        CloseTravelMap();
    }

    public void CloseTravelMap()
    {
        _travelMapOpen = false;
        _openTravelConsole = null;
        _travelMapPanel!.Visible = false;
        CaptureMouse();
    }

    /// <summary>Called by StationConsoleVerbTarget.ExecuteVerb — opens the shop panel instead of
    /// the old per-item Buy/Sell verb-cycling, same shape as OpenTravelMap.</summary>
    public void OpenShop(StationConsoleVerbTarget console)
    {
        _shopOpen = true;
        _openShopConsole = console;
        RefreshShop();
        _shopPanel!.Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Called by ShopPanel's row buttons — buys/sells one unit and immediately refreshes
    /// the panel's rows (price/affordability/ownership all shift after every transaction), rather
    /// than closing the panel like a one-shot travel confirmation would.</summary>
    public void BuyItem(string itemId)
    {
        _openShopConsole?.TryBuy(itemId);
        RefreshShop();
    }

    public void SellItem(string itemId)
    {
        _openShopConsole?.TrySell(itemId);
        RefreshShop();
    }

    private void RefreshShop()
    {
        if (_openShopConsole is not null)
        {
            _shopPanel!.Populate(_openShopConsole.BuildBuyEntries(), _openShopConsole.BuildSellEntries());
        }
    }

    public void CloseShop()
    {
        _shopOpen = false;
        _openShopConsole = null;
        _shopPanel!.Visible = false;
        CaptureMouse();
    }

    /// <summary>Right-click-on-inventory-item entry point (see InventorySlotUI) — toggles open/
    /// closed whichever window (if any) represents that item's own inventory. A no-op for any
    /// item that doesn't have one.</summary>
    public void ToggleItemWindow(string itemId)
    {
        switch (itemId)
        {
            case "power_drill":
                _drillWindow!.Visible = !_drillWindow.Visible;
                break;
            case "flashlight":
                _flashlightWindow!.Visible = !_flashlightWindow.Visible;
                break;
            case "backpack" or "debug_backpack" when _inventory.Backpack is not null:
                _backpackWindow!.Visible = !_backpackWindow.Visible;
                break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsBusy && _busyTarget!.CurrentVerbProgress is null)
        {
            // The task we started has finished naturally — no refund, unlike an explicit cancel.
            _busyTarget = null;
            _busyVerb = null;
        }

        var ambientZone = UpdateAmbientShipSim();
        var currentCell = new CellCoord(_ambientTile.X, _ambientTile.Y);

        // A vented/breached room reads as vacuum — read up front since it now also decides which
        // movement mode applies below, not just the suit-resource drain further down.
        var roomVolume = ShipSimRef?.VolumeAt(currentCell);
        var inZeroG = (roomVolume?.O2Fraction ?? 0.21) <= ShipAtmosphereZone.ZeroGO2Threshold
            || (!IsOnFloor() && NoFloorBelow());
        MotionMode = inZeroG ? MotionModeEnum.Floating : MotionModeEnum.Grounded;

        // A ship with no life support (e.g. the Derelict) never regenerates air, so a room's
        // O2Fraction can stay at "reads as vacuum" forever even after its own breach is patched
        // and it's properly sealed off again — inZeroG alone can't tell "still actively venting"
        // apart from "permanently dead air, but sealed and safe now." The decompression-pull
        // hazard specifically needs the real graph check instead, or it keeps pulling toward some
        // other, unrelated breach elsewhere on the ship (through a closed, sealed door) just
        // because it's within raw range — see the pull block below.
        var isConnectedToOutside = ShipSimRef?.Atmosphere?.IsConnectedToOutside(currentCell) ?? false;

        // Read before this frame's _needs.Tick further down — one frame of staleness against a
        // drain that only moves per-second is irrelevant, and movement needs this before the
        // grounded/zero-g branches run.
        var needsDebuffActive = _needs.HungerPercent <= 0f || _needs.ThirstPercent <= 0f || _needs.EnergyPercent <= 0f;
        var moveMultiplier = needsDebuffActive ? NeedsDebuffMoveMultiplier : 1f;

        var velocity = Velocity;

        if (inZeroG)
        {
            // Thrust-based, not direct-velocity — you drift and have to counter-thrust to stop,
            // a first real taste of the "precise maneuvering is earned" framing
            // docs/architecture/locomotion.md describes for the complete game's free-float mode.
            velocity = velocity.MoveToward(Vector3.Zero, ZeroGDrag * (float)delta);

            if (!IsBusy)
            {
                var thrust = Vector3.Zero;
                if (Input.IsPhysicalKeyPressed(Key.W))
                {
                    thrust -= _head!.GlobalTransform.Basis.Z;
                }

                if (Input.IsPhysicalKeyPressed(Key.S))
                {
                    thrust += _head!.GlobalTransform.Basis.Z;
                }

                if (Input.IsPhysicalKeyPressed(Key.A))
                {
                    thrust -= Transform.Basis.X;
                }

                if (Input.IsPhysicalKeyPressed(Key.D))
                {
                    thrust += Transform.Basis.X;
                }

                if (Input.IsPhysicalKeyPressed(Key.Space))
                {
                    thrust += Vector3.Up;
                }

                if (Input.IsPhysicalKeyPressed(Key.Ctrl))
                {
                    thrust += Vector3.Down;
                }

                // No jetpack yet — without one, real EVA control only comes from pushing off a
                // nearby surface, not thrusting freely through open space. Once launched, you
                // drift (drag still applies above) until you reach something else to push off.
                if (thrust != Vector3.Zero && IsNearSurface())
                {
                    velocity += thrust.Normalized() * ZeroGThrustAcceleration * moveMultiplier * (float)delta;
                }
            }
            else
            {
                velocity = Vector3.Zero;
            }

            // Not gated on IsNearSurface/IsBusy like thrust above — decompression isn't
            // something you opt into, it applies to anyone unsecured near an open breach. Full
            // DecompressionPullAcceleration applies at a constant rate any time inZeroG is true
            // (i.e. the room is actively vented) — NOT scaled by the room's current Pressure:
            // whole-component venting (AtmosphereSystem.Vent) now drives Pressure to near-zero
            // within about a second and it never recovers, so a Pressure-scaled pull would decay
            // to an imperceptible force almost immediately rather than a sustained rushing-air
            // pull for as long as the breach stays open.
            //
            // Also requires ambientZone (this frame's live lookup, not the cached fields below) —
            // once actually through the hole and out of the room's zone, the cached breach
            // position/room data would otherwise keep pulling from the far side, back inside,
            // causing a pendulum instead of a clean exit (see UpdateAmbientShipSim's own doc).
            //
            // Also requires isConnectedToOutside (the real graph check, not just inZeroG) — on a
            // ship with no life support, a sealed, already-patched room can still read as zero-g
            // forever, and without this check the loop below would keep pulling toward some
            // *other* breach elsewhere on the ship — including one behind a closed, sealed door —
            // just because ActiveBreachPositions() lists every breach on the whole ship and the
            // range check alone can't tell "actually reachable by air from here" apart from
            // "happens to be within 5m in a straight line through solid walls."
            if (_ambientBuildTarget is not null && ambientZone is not null && isConnectedToOutside)
            {
                foreach (var breachPosition in _ambientBuildTarget.ActiveBreachPositions())
                {
                    // Same reasoning as the range check below, but for room topology instead of
                    // raw distance: atmosphere connectivity can span multiple rooms through an
                    // open (unsealed) door, so a breach can be "connected" from here while still
                    // being physically in a DIFFERENT room. A straight-line pull toward it would
                    // cut through the dividing wall instead of following the doorway — only pull
                    // toward a breach actually inside the same room/zone the player is standing
                    // in right now.
                    if (!ReferenceEquals(ShipAtmosphereZone.FindZoneAt(GetWorld3D(), breachPosition), ambientZone))
                    {
                        continue;
                    }

                    var toBreach = breachPosition - GlobalPosition;
                    var distance = toBreach.Length();
                    if (distance > DecompressionPullRange || distance < 0.01f)
                    {
                        continue;
                    }

                    velocity += toBreach.Normalized() * DecompressionPullAcceleration * (float)delta;
                }
            }

            if (velocity.Length() > ZeroGMaxSpeed * moveMultiplier)
            {
                velocity = velocity.Normalized() * ZeroGMaxSpeed * moveMultiplier;
            }
        }
        else
        {
            if (!IsOnFloor())
            {
                velocity.Y -= Gravity * (float)delta;
            }

            if (IsBusy)
            {
                velocity.X = 0;
                velocity.Z = 0;
            }
            else
            {
                var inputDirection = Vector2.Zero;
                if (Input.IsPhysicalKeyPressed(Key.W))
                {
                    inputDirection.Y -= 1;
                }

                if (Input.IsPhysicalKeyPressed(Key.S))
                {
                    inputDirection.Y += 1;
                }

                if (Input.IsPhysicalKeyPressed(Key.A))
                {
                    inputDirection.X -= 1;
                }

                if (Input.IsPhysicalKeyPressed(Key.D))
                {
                    inputDirection.X += 1;
                }

                inputDirection = inputDirection.Normalized();

                var moveDirection = (Transform.Basis * new Vector3(inputDirection.X, 0, inputDirection.Y)).Normalized();
                velocity.X = moveDirection.X * MoveSpeed * moveMultiplier;
                velocity.Z = moveDirection.Z * MoveSpeed * moveMultiplier;
            }
        }

        Velocity = velocity;
        MoveAndSlide();

        // Shove any loose item the player collided with this frame — see ItemPushImpulse's own
        // doc comment for why this doesn't happen automatically.
        for (var i = 0; i < GetSlideCollisionCount(); i++)
        {
            if (GetSlideCollision(i).GetCollider() is RigidBody3D { Freeze: false } rigidBody)
            {
                rigidBody.ApplyCentralImpulse(-GetSlideCollision(i).GetNormal() * ItemPushImpulse);
            }
        }

        // The real beam only exists while the flashlight is both held and toggled on, and its own
        // battery still has charge — unequipping/swapping it out of hand turns it off
        // automatically without touching _flashlightOn, and an empty battery just as silently
        // stops it lighting (same "Charge: > 0f" gating IsAffordable already uses for the drill).
        // The debug flashlight (see fresh-game stipend) shares the same _flashlightOn toggle so it
        // can be switched off too, but skips the held/battery gating — it's a testing convenience,
        // not something you hold, and it has no battery to drain.
        var holdingFlashlight = LeftHandItemId == "flashlight" || RightHandItemId == "flashlight";
        var realFlashlightOn = _flashlightOn && holdingFlashlight && _inventory.Flashlight is { HasItem: true, Charge: > 0f };
        // Deliberately still keyed to the literal debug item, not ItemCatalog.IsToggleableLight —
        // that flag only means "the F-key toggle applies to this item," not "bypasses hand/battery
        // gating entirely, on merely carrying it." Those are different properties that happen to
        // both be true for this one dev-only item; generalizing this specific check would make an
        // unheld real flashlight sitting in the backpack incorrectly project a beam too.
        var debugFlashlightOn = _flashlightOn && _inventory.Has("debug_flashlight", 1);
        _flashlightSpot!.Visible = realFlashlightOn || debugFlashlightOn;

        if (realFlashlightOn)
        {
            _inventory.Flashlight!.Charge = Mathf.Max(0f, _inventory.Flashlight.Charge - FlashlightChargeDrainPerSecond * (float)delta);
        }

        // A burning cell is real smoke, not just a number — it drains O2 faster (see
        // SuitResources.Tick's inSmoke case) and gets a screen overlay below so it's actually
        // felt, not just read off the O2 bar. Checked across the player's whole current room
        // (ComponentContaining), not just _ambientTile's own exact cell — _ambientTile is a
        // zone's fixed representative tile for reading the room's lumped O2/pressure (correct for
        // that, since the whole room shares one value), but a fire is a specific cell, which
        // could easily be a different tile in the same room from the one the zone happens to use.
        // currentCell itself is computed once, up top, and reused here — same tile all tick.
        var inSmoke = ShipSimRef?.Atmosphere?.ComponentContaining(currentCell).Any(ShipSimRef.Deck.IsOnFire) ?? false;
        if (_smokeOverlay is not null)
        {
            _smokeOverlay.Visible = inSmoke;
        }

        // Suit resources keep draining while busy performing a verb — a task's duration is a
        // real elapsed-time cost, not a pause (docs/project-plan.md's "time acceleration ...
        // pays the full bill" framing). A breached room's dropping O2 burns the suit's own
        // reserve faster on top of the flat drain, and smoke burns O2 faster too (see
        // SuitResources.Tick). Once O2 bottoms out, it starts draining Health instead — a hard
        // 0-Health death, handled below. Extreme ambient temperature (a breach gone properly
        // cold, or standing in an active fire's heat) drains Health too, compounding with O2
        // depletion instead of being a separate stat (see SuitResources.Tick).
        var ambientTemperature = roomVolume?.Temperature ?? AtmosphereVolume.Breathable.Temperature;
        _suitResources.Tick(delta, roomVolume?.O2Fraction ?? 0.21, ambientTemperature, inSmoke);
        _o2Bar!.Value = _suitResources.O2Percent;
        _healthBar!.Value = _suitResources.HealthPercent;
        _coldOverlay!.Visible = _suitResources.IsFreezing;
        _burnOverlay!.Visible = _suitResources.IsBurning;

        if (_suitResources.HealthPercent <= 0f)
        {
            Die();
        }

        _needs.Tick(delta);
        _hungerBar!.Value = _needs.HungerPercent;
        _thirstBar!.Value = _needs.ThirstPercent;
        _energyBar!.Value = _needs.EnergyPercent;

        if (roomVolume is not null)
        {
            _roomO2Label!.Visible = true;
            _roomO2Label.Text = Tr("HUD_ROOM_O2") + $": {roomVolume.O2Fraction * 100:F0}%";
        }
        else
        {
            _roomO2Label!.Visible = false;
        }

        // Only shown while actually holding the drill — feedback on "loses power with usage"
        // matters mid-task, not just when the inventory panel (with the battery slot) is open.
        var holdingDrill = LeftHandItemId == "power_drill" || RightHandItemId == "power_drill";
        _drillLabel!.Visible = holdingDrill;
        _drillBar!.Visible = holdingDrill;
        if (holdingDrill)
        {
            _drillBar.Value = _inventory.Drill is { HasItem: true } drill ? drill.Charge * 100 : 0;
        }

        // Same "only while holding it" feedback as the drill above.
        _flashlightLabel!.Visible = holdingFlashlight;
        _flashlightBar!.Visible = holdingFlashlight;
        if (holdingFlashlight)
        {
            _flashlightBar.Value = _inventory.Flashlight is { HasItem: true } flashlight ? flashlight.Charge * 100 : 0;
        }

        UpdateVerbHud();
    }

    /// <summary>Whether any solid geometry (wall, floor, ceiling, device — anything with a
    /// collider) is within <see cref="ZeroGControlRadius"/> — the zero-g "is there something to
    /// push off of" check. A single sphere overlap query, cheap enough to run only when the
    /// player is actually trying to thrust (see the zero-g branch of _PhysicsProcess).</summary>
    private bool IsNearSurface()
    {
        var spaceState = GetWorld3D().DirectSpaceState;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = ZeroGControlRadius },
            Transform = new Transform3D(Basis.Identity, GlobalPosition),
            Exclude = new Godot.Collections.Array<Rid> { GetRid() },
            CollideWithAreas = false,
            CollideWithBodies = true,
            // Layer 1 only — excludes the "build_aim_only" layer (project.godot) the Home
            // Ship's floor/ceiling aim-helper bodies live on, which never blocks movement
            // and would otherwise make a genuine hole look like a nearby surface to push off.
            CollisionMask = 1,
        };

        return spaceState.IntersectShape(query, maxResults: 1).Count > 0;
    }

    /// <summary>No ship structure at all within a generous distance straight down — you've
    /// walked/fallen off the edge of a ship (a removed wall or floor) into open space, not just
    /// lost your footing inside a room that still has a floor nearby. Independent of room O2:
    /// atmosphere venting is gradual (AtmosphereSystem.Vent's Lerp), so a freshly-breached room
    /// can take several seconds to read as vacuum — too slow to save you from an immediate fall
    /// through a hole you just made.</summary>
    private bool NoFloorBelow()
    {
        var spaceState = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(
            GlobalPosition, GlobalPosition + Vector3.Down * FreefallRaycastDistance);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        // Layer 1 only, same reasoning as IsNearSurface — the Home Ship's floor aim-helper
        // body (layer "build_aim_only") is deliberately non-blocking and must not register
        // as real floor here, or a genuine hole through it would never trigger zero-g.
        query.CollisionMask = 1;

        // Matches IsNearSurface's own IntersectShape(...).Count > 0 idiom — IntersectRay
        // returns an empty Dictionary on a miss, populated (position/normal/collider/...) on a hit.
        return spaceState.IntersectRay(query).Count == 0;
    }

    private IVerbTarget? GetCurrentVerbTarget()
    {
        if (_interactRay is null || !_interactRay.IsColliding())
        {
            return null;
        }

        var collider = _interactRay.GetCollider();

        if (collider is ShipBuildAimForwarder { BuildTarget: { } forwardedTarget } forwarder)
        {
            if (forwarder.IsCeiling)
            {
                forwardedTarget.SetCeilingAimPoint(_interactRay.GetCollisionPoint());
            }
            else
            {
                forwardedTarget.SetAimPoint(_interactRay.GetCollisionPoint());
            }

            return forwardedTarget;
        }

        if (collider is ShipBuildTarget floorTarget)
        {
            floorTarget.SetAimPoint(_interactRay.GetCollisionPoint());
            return floorTarget;
        }

        return collider as IVerbTarget;
    }

    private void Interact()
    {
        if (IsBusy)
        {
            return;
        }

        var target = GetCurrentVerbTarget();
        var verbs = SelectableVerbs(target);
        if (target is null || verbs.Count == 0)
        {
            return;
        }

        var verb = verbs[_selectedVerbIndex % verbs.Count];
        if (verb.Disabled)
        {
            return;
        }

        foreach (var requirement in verb.Requirements.Where(r => r.Consumed))
        {
            _inventory.TryRemove(requirement.ItemId, requirement.Count);
        }

        if (verb.Requirements.Any(r => r.ItemId == "power_drill"))
        {
            _inventory.Drill!.Charge = Mathf.Max(0f, _inventory.Drill.Charge - DrillChargeDrainPerUse);
        }

        target.ExecuteVerb(verb, _inventory);

        if (verb.DurationSeconds > 0)
        {
            _busyTarget = target;
            _busyVerb = verb;
        }
    }

    /// <summary>Scrolling cycles which of the current target's affordable verbs is highlighted
    /// (e.g. Repair vs Scrap on a damaged conduit) — right-click executes whichever is
    /// currently selected. A no-op for the common case of a target with 0 or 1 verbs.</summary>
    private void CycleSelectedVerb(int direction)
    {
        var count = SelectableVerbs(GetCurrentVerbTarget()).Count;
        if (count == 0)
        {
            return;
        }

        _selectedVerbIndex = ((_selectedVerbIndex + direction) % count + count) % count;
    }

    /// <summary>A verb with no item Requirements is always affordable. One that does have
    /// Requirements needs the player to actually be holding that exact item in either hand
    /// (real PlayerInventory slots — see LeftHandItemId/RightHandItemId) with enough of it in
    /// the inventory — this is the single place every item-gated verb (repair hull breach,
    /// repair damaged conduit, install conduit, ...) is gated, so a target never needs its own
    /// affordability logic. One extra clause is specific to the power drill (the only stateful
    /// tool so far, see PlayerInventory.SpecializedSlot) — holding it isn't enough, it also needs an
    /// installed battery with real charge left.</summary>
    private bool IsAffordable(Verb verb) =>
        verb.Requirements.Count == 0 ||
        verb.Requirements.All(r =>
            (r.ItemId == LeftHandItemId || r.ItemId == RightHandItemId) &&
            _inventory.Has(r.ItemId, r.Count) &&
            (r.ItemId != "power_drill" || _inventory.Drill is { HasItem: true, Charge: > 0f }));

    /// <summary>The single place a target's verbs are filtered to affordable ones and ordered
    /// for cycling/selection — every caller (Interact, CycleSelectedVerb, UpdateVerbHud) must
    /// see the exact same list, since _selectedVerbIndex indexes into whatever this returns.
    /// Creating/using verbs always sort before deconstruction/scrapping ones (a stable sort, so
    /// relative order within each group is otherwise unchanged) — see Verb.IsDestructive.</summary>
    private List<Verb> SelectableVerbs(IVerbTarget? target) =>
        target?.AvailableVerbs.Where(IsAffordable).OrderBy(v => v.IsDestructive).ToList() ?? [];

    private void UpdateVerbHud()
    {
        // No depletion check needed here anymore: a hand is a real PlayerInventory slot now, so
        // TryRemove draining it to 0 already clears it back to null on its own (see
        // PlayerInventory.TryRemove) — nothing to poll for.
        var target = GetCurrentVerbTarget();

        if (target != _lastTarget)
        {
            _selectedVerbIndex = 0;
            _lastTarget = target;
        }

        // A verb already in progress on this exact target keeps showing/counting down as-is —
        // its Requirements were already deducted to start it, so re-checking affordability here
        // would hide the HUD partway through an already-succeeding action.
        var verbs = SelectableVerbs(target);
        var verb = IsBusy && _busyTarget == target
            ? _busyVerb
            : verbs.Count > 0 ? verbs[_selectedVerbIndex % verbs.Count] : null;

        var buildTarget = target as ShipBuildTarget;
        if (_activeBuildTarget is not null && _activeBuildTarget != buildTarget)
        {
            _activeBuildTarget.SetPreviewVerb(null);
        }

        buildTarget?.SetPreviewVerb(verb);
        _activeBuildTarget = buildTarget;

        // The name label identifies whatever you're looking at, independent of whether you can
        // currently afford its verb — e.g. a damaged conduit should still read as "Damaged
        // Conduit" even while you're not holding spare parts, not go blank.
        if (target?.DisplayNameKey is { } displayNameKey)
        {
            // A loose battery's real charge is worth surfacing here too, same "label (suffix)"
            // shape the verb label below already uses for Verb.DisplaySuffix.
            _targetNameLabel!.Text = target is PickupItem { ItemId: "battery" } batteryPickup
                ? $"{Tr(displayNameKey)} ({Mathf.RoundToInt(batteryPickup.Charge * 100)}%)"
                : Tr(displayNameKey);
            _targetNameLabel.Visible = true;
        }
        else
        {
            _targetNameLabel!.Visible = false;
        }

        if (verb is not null)
        {
            var progress = target!.CurrentVerbProgress;

            var label = verb.DisplaySuffix is { } suffix ? $"{Tr(verb.LocalizationKey)} ({suffix})" : Tr(verb.LocalizationKey);
            _verbLabel!.Text = verbs is { Count: > 1 }
                ? $"{label} ({_selectedVerbIndex % verbs.Count + 1}/{verbs.Count})"
                : label;
            _verbLabel.Visible = true;
            _verbLabel.Modulate = verb.Disabled ? Colors.Red : Colors.White;

            _verbProgressBar!.Visible = progress is not null;
            if (progress is not null)
            {
                _verbProgressBar.Value = progress.Value * 100;
            }
        }
        else
        {
            _verbLabel!.Visible = false;
            _verbProgressBar!.Visible = false;
        }

        UpdateInventoryHud();
    }

    public PlayerSaveData CapturePlayerState()
    {
        return new PlayerSaveData
        {
            PosX = Position.X,
            PosY = Position.Y,
            PosZ = Position.Z,
            Yaw = Rotation.Y,
            Pitch = _pitch,
            O2Percent = _suitResources.O2Percent,
            HealthPercent = _suitResources.HealthPercent,
            HungerPercent = _needs.HungerPercent,
            ThirstPercent = _needs.ThirstPercent,
            EnergyPercent = _needs.EnergyPercent,
            HandSlots = SlotSaveDataConverter.Capture(_inventory.Hands),
            Credits = _credits,
            BackpackItemId = _inventory.Backpack?.ItemId,
            BackpackSlots = _inventory.Backpack is { } backpack
                ? SlotSaveDataConverter.Capture(backpack.Contents)
                : new List<SlotSaveData?>(),
            BackpackSlotCount = _inventory.Backpack?.Contents.Slots.Count ?? PlayerInventory.BackpackSlotCount,
            HasDrill = _inventory.Drill is not null,
            DrillHasBattery = _inventory.Drill?.HasItem ?? false,
            DrillCharge = _inventory.Drill?.Charge ?? 0f,
            HasFlashlight = _inventory.Flashlight is not null,
            FlashlightHasBattery = _inventory.Flashlight?.HasItem ?? false,
            FlashlightCharge = _inventory.Flashlight?.Charge ?? 0f,
            InventoryWindow = new WindowPosition(_inventoryPanel!.Position.X, _inventoryPanel.Position.Y),
            DrillWindow = new WindowPosition(_drillWindow!.Position.X, _drillWindow.Position.Y),
            FlashlightWindow = new WindowPosition(_flashlightWindow!.Position.X, _flashlightWindow.Position.Y),
            BackpackWindow = new WindowPosition(_backpackWindow!.Position.X, _backpackWindow.Position.Y),
        };
    }

    public void ApplyPlayerState(PlayerSaveData data)
    {
        Position = new Vector3(data.PosX, data.PosY, data.PosZ);
        Rotation = new Vector3(0, data.Yaw, 0);

        _pitch = data.Pitch;
        if (_head is not null)
        {
            _head.Rotation = new Vector3(_pitch, 0, 0);
        }

        _suitResources.RestoreFrom(data.O2Percent, data.HealthPercent);
        _needs.RestoreFrom(data.HungerPercent, data.ThirstPercent, data.EnergyPercent);

        _inventory.Clear();
        if (data.HandSlots.Count > 0)
        {
            SlotSaveDataConverter.Restore(_inventory.Hands, data.HandSlots);
        }
        else
        {
            // Legacy save predating per-slot state (see PlayerSaveData.HandSlots) — replay the
            // old aggregate dict instead.
            foreach (var (itemId, count) in data.Inventory)
            {
                _inventory.Add(itemId, count);
            }
        }

        // Backpack is reconstructed after the body replay above (still null at that point, so
        // those Add calls only ever fill body slots) and filled directly into its own fresh
        // SlotContainer, keeping body-refill and backpack-refill from cross-contaminating.
        if (data.BackpackItemId is { } backpackItemId)
        {
            var contents = new SlotContainer(data.BackpackSlotCount);
            if (data.BackpackSlots.Count > 0)
            {
                SlotSaveDataConverter.Restore(contents, data.BackpackSlots);
            }
            else
            {
                // Legacy save predating per-slot state (see PlayerSaveData.BackpackSlots).
                foreach (var (itemId, count) in data.BackpackContents)
                {
                    contents.Add(itemId, count);
                }
            }

            _inventory.EquipContainerDirectly("back", backpackItemId, contents);
        }

        if (data.HasDrill)
        {
            _inventory.AttachSpecializedSlot("drill_battery", data.DrillHasBattery, data.DrillCharge);
        }

        if (data.HasFlashlight)
        {
            _inventory.AttachSpecializedSlot("flashlight_battery", data.FlashlightHasBattery, data.FlashlightCharge);
        }

        // Only applied when present — an old save predating this feature leaves every window at
        // its scene-authored default position.
        if (data.InventoryWindow is { } inventoryWindowPos)
        {
            _inventoryPanel!.Position = new Vector2(inventoryWindowPos.X, inventoryWindowPos.Y);
        }

        if (data.DrillWindow is { } drillWindowPos)
        {
            _drillWindow!.Position = new Vector2(drillWindowPos.X, drillWindowPos.Y);
        }

        if (data.FlashlightWindow is { } flashlightWindowPos)
        {
            _flashlightWindow!.Position = new Vector2(flashlightWindowPos.X, flashlightWindowPos.Y);
        }

        if (data.BackpackWindow is { } backpackWindowPos)
        {
            _backpackWindow!.Position = new Vector2(backpackWindowPos.X, backpackWindowPos.Y);
        }

        _credits = data.Credits;
    }

    public void RefillSuitResources() => _suitResources.RestoreFrom(100f, 100f);

    /// <summary>Hard death from 0 Health (see the O2-driven drain in SuitResources.Tick) —
    /// reloads the last save so the player picks back up from wherever they last saved.
    /// Falls back to a full O2/Health restore in place (no position/inventory reset — there's
    /// nothing more specific to reset to yet) if there's no save to reload, e.g. a fresh game
    /// that died before ever pressing F5. No dedicated "you died" screen yet — a later polish
    /// pass, not this one.</summary>
    private void Die()
    {
        GD.Print("[Player] Died — reloading last save.");
        if (SaveManagerRef is not { } saveManager || !saveManager.Load())
        {
            RefillSuitResources();
        }
    }

    /// <summary>The Bunk's Sleep-completion hook — a full night's rest.</summary>
    public void RestEnergy() => _needs.Rest(100f);

    private void UpdateInventoryHud()
    {
        // Re-pointed every frame rather than only on equip/unequip: equipping/unequipping
        // creates a new SlotContainer instance, and this is the cheapest way to keep the panel's
        // backpack section always addressing whichever one (if any) is currently worn.
        _backpackGrid!.Visible = _inventory.Backpack is not null;
        if (_inventory.Backpack is null)
        {
            // Unequipping while its contents window happens to be open closes it instead of
            // leaving an empty window floating with nothing left to show.
            _backpackWindow!.Visible = false;
        }

        var backpackSlotCount = _inventory.Backpack?.Contents.Slots.Count ?? 0;
        if (backpackSlotCount != _backpackSlotUICount)
        {
            RebuildBackpackSlotUIs(backpackSlotCount);
        }

        foreach (var slot in _backpackSlotUIs)
        {
            slot.Container = _inventory.Backpack?.Contents;
        }

        _creditsLabel!.Text = Tr("HUD_CREDITS") + $": {_credits}";

        _leftHandLabel!.Text = Tr("HUD_LEFT_HAND") + ": " + (LeftHandItemId is { } leftItem
            ? Tr("ITEM_" + leftItem.ToUpperInvariant())
            : Tr("HUD_HOLDING_EMPTY"));

        _rightHandLabel!.Text = Tr("HUD_RIGHT_HAND") + ": " + (RightHandItemId is { } rightItem
            ? Tr("ITEM_" + rightItem.ToUpperInvariant())
            : Tr("HUD_HOLDING_EMPTY"));
    }

    /// <summary>Rebuilds BackpackGrid's InventorySlotUI children to match the worn backpack's
    /// actual slot count (8 for the normal backpack, 24 for the debug backpack, 0 while
    /// unequipped) by duplicating <see cref="_backpackSlotTemplate"/> — the scene itself only
    /// ever carries that one hidden template node, not a fixed slot count.</summary>
    private void RebuildBackpackSlotUIs(int slotCount)
    {
        foreach (var slot in _backpackSlotUIs)
        {
            slot.QueueFree();
        }

        _backpackSlotUIs.Clear();

        for (var i = 0; i < slotCount; i++)
        {
            var slot = (InventorySlotUI)_backpackSlotTemplate!.Duplicate();
            slot.Visible = true;
            slot.SlotIndex = i;
            slot.PlayerRef = this;
            _backpackGrid!.AddChild(slot);
            _backpackSlotUIs.Add(slot);
        }

        _backpackSlotUICount = slotCount;
    }
}

using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Sim.Grid;
using Scavengineers.Scripts.Inventory;
using Scavengineers.Scripts.SaveLoad;
using Scavengineers.Scripts.Ship;
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

    // Zero-g movement (placeholder/tunable) — triggers whenever the room's own O2 reads at or
    // below this fraction (see ShipSimRef.VolumeAt), same "vacuum" threshold spirit as
    // AtmosphereVolume.Vacuum's O2Fraction of 0, with a little slack so the switch doesn't
    // flicker right at the exact boundary while a room is still venting/equalizing.
    private const float ZeroGO2Threshold = 0.01f;
    private const float ZeroGThrustAcceleration = 6f;
    private const float ZeroGDrag = 2f; // passive deceleration per second, always applied
    private const float ZeroGMaxSpeed = 3.5f;

    // How close a wall/floor/ceiling/device has to be for zero-g thrust to work at all — no
    // jetpack yet, so control only comes from pushing off something within reach.
    private const float ZeroGControlRadius = 1.2f;

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

    public void SetAmbientShipSim(ShipSim? shipSim, Vector2I tile)
    {
        ShipSimRef = shipSim;
        _ambientTile = tile;
    }

    private Node3D? _head;
    private RayCast3D? _interactRay;
    private Label? _targetNameLabel;
    private Label? _verbLabel;
    private ProgressBar? _verbProgressBar;
    private ProgressBar? _o2Bar;
    private ProgressBar? _powerBar;
    private Label? _roomO2Label;
    private Label? _leftHandLabel;
    private Label? _rightHandLabel;
    private Label? _creditsLabel;
    private Control? _inventoryPanel;
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
    private SpotLight3D? _flashlightSpot;
    private bool _flashlightOn;

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

    /// <summary>Whether the mouse-driven inventory panel (Tab) is currently open — while true,
    /// look/Interact/verb-cycling/mouse-recapture are all suppressed so clicking and dragging in
    /// the panel doesn't also fire world interactions or yank the mouse back into captured mode
    /// mid-drag.</summary>
    private bool _inventoryOpen;

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
        _targetNameLabel = GetNode<Label>("HUD/TargetNameLabel");
        _verbLabel = GetNode<Label>("HUD/VerbLabel");
        _verbProgressBar = GetNode<ProgressBar>("HUD/VerbProgressBar");
        _o2Bar = GetNode<ProgressBar>("HUD/ResourcesPanel/O2Bar");
        _powerBar = GetNode<ProgressBar>("HUD/ResourcesPanel/PowerBar");
        _hungerBar = GetNode<ProgressBar>("HUD/ResourcesPanel/HungerBar");
        _thirstBar = GetNode<ProgressBar>("HUD/ResourcesPanel/ThirstBar");
        _energyBar = GetNode<ProgressBar>("HUD/ResourcesPanel/EnergyBar");
        _roomO2Label = GetNode<Label>("HUD/ResourcesPanel/RoomO2Label");
        _drillLabel = GetNode<Label>("HUD/ResourcesPanel/DrillLabel");
        _drillBar = GetNode<ProgressBar>("HUD/ResourcesPanel/DrillBar");
        _flashlightSpot = GetNode<SpotLight3D>("Head/Camera3D/FlashlightSpot");
        _leftHandLabel = GetNode<Label>("HUD/LeftHandLabel");
        _rightHandLabel = GetNode<Label>("HUD/RightHandLabel");
        _creditsLabel = GetNode<Label>("HUD/CreditsLabel");
        _inventoryPanel = GetNode<Control>("HUD/InventoryPanel");
        _backpackGrid = GetNode<Control>("HUD/InventoryPanel/Layout/BackpackGrid");

        foreach (var child in GetNode("HUD/InventoryPanel/Layout/EquipSlots").GetChildren())
        {
            if (child is InventorySlotUI slot)
            {
                slot.Container = _inventory.Hands;
                slot.PlayerRef = this;
            }
        }

        _backpackSlotTemplate = GetNode<InventorySlotUI>("HUD/InventoryPanel/Layout/BackpackGrid/SlotTemplate");

        // Placeholder/tunable starting stipend for testing the free-form conduit wiring
        // extensively — same "don't wait on it" spirit as the near-instant verb durations.
        // Overwritten by ApplyPlayerState on load, same as every other fresh-start default.
        // The debug backpack is attached first (dev convenience, not something you "found" —
        // bypasses the normal hand-then-equip flow entirely) so the stipend below spills into
        // its 24 slots instead of overflowing two bare hands.
        _inventory.EquipContainerDirectly("debug_backpack", new SlotContainer(24));
        _inventory.Add("scrap_metal", 50);
        _inventory.Add("ration_bar", 3);
        _inventory.Add("water_bottle", 3);
        _inventory.Add("crowbar", 1);
        _inventory.Add("power_drill", 1);
        _inventory.AttachDrill(hasBattery: true, charge: 1f);
        _inventory.Add("flashlight", 1);

        CaptureMouse();
        // Setting MouseMode here alone is unreliable if the window doesn't yet have OS
        // input focus at this exact point in startup — reapply whenever focus is (re)gained.
        GetWindow().FocusEntered += CaptureMouse;

        AddToGroup("player");
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured && !IsBusy && !_inventoryOpen)
        {
            RotateY(-mouseMotion.Relative.X * MouseSensitivity);

            _pitch = Mathf.Clamp(_pitch - mouseMotion.Relative.Y * MouseSensitivity, -MaxPitchRadians, MaxPitchRadians);
            if (_head is not null)
            {
                _head.Rotation = new Vector3(_pitch, 0, 0);
            }
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && !IsBusy && !_inventoryOpen)
        {
            Interact();
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } && !IsBusy && !_inventoryOpen)
        {
            CycleSelectedVerb(1);
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } && !IsBusy && !_inventoryOpen)
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
                 && Input.MouseMode != Input.MouseModeEnum.Captured && !_inventoryOpen)
        {
            CaptureMouse();
        }
        else if (@event is InputEventKey { Keycode: Key.Tab, Pressed: true } && !IsBusy)
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
            if (_inventoryOpen)
            {
                CloseInventory();
            }
            else
            {
                Input.MouseMode = Input.MouseModeEnum.Visible;
            }
        }
        else if (@event is InputEventKey { Keycode: Key.F, Pressed: true } && !IsBusy && !_inventoryOpen)
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
    /// if one's held; otherwise toggles a held flashlight on/off. A no-op if neither hand holds
    /// either.</summary>
    private void UseHeldItem()
    {
        if (TryConsumeHand(PlayerInventory.LeftHandSlotIndex) || TryConsumeHand(PlayerInventory.RightHandSlotIndex))
        {
            return;
        }

        if (LeftHandItemId == "flashlight" || RightHandItemId == "flashlight")
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
        meshInstance.SetSurfaceOverrideMaterial(0, DroppedItemMaterial);
        pickup.AddChild(meshInstance);

        pickup.AddChild(new CollisionShape3D { Shape = DroppedItemShape });
    }

    // Instance method, not static: the FocusEntered connection below is then tied to this
    // Player's lifetime, so Godot auto-disconnects it when this instance is freed (e.g. on a
    // scene change). A static method's connection has no instance to track, so travelling
    // between scenes would try to reconnect the exact same callable and hit "already connected."
    private void CaptureMouse() => Input.MouseMode = Input.MouseModeEnum.Captured;

    private void OpenInventory()
    {
        _inventoryOpen = true;
        if (_inventoryPanel is not null)
        {
            _inventoryPanel.Visible = true;
        }

        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void CloseInventory()
    {
        _inventoryOpen = false;
        if (_inventoryPanel is not null)
        {
            _inventoryPanel.Visible = false;
        }

        CaptureMouse();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsBusy && _busyTarget!.CurrentVerbProgress is null)
        {
            // The task we started has finished naturally — no refund, unlike an explicit cancel.
            _busyTarget = null;
            _busyVerb = null;
        }

        // A vented/breached room reads as vacuum — read up front since it now also decides which
        // movement mode applies below, not just the suit-resource drain further down.
        var roomVolume = ShipSimRef?.VolumeAt(new CellCoord(_ambientTile.X, _ambientTile.Y));
        var inZeroG = (roomVolume?.O2Fraction ?? 0.21) <= ZeroGO2Threshold
            || (!IsOnFloor() && NoFloorBelow());
        MotionMode = inZeroG ? MotionModeEnum.Floating : MotionModeEnum.Grounded;

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
                    velocity += thrust.Normalized() * ZeroGThrustAcceleration * (float)delta;
                }
            }
            else
            {
                velocity = Vector3.Zero;
            }

            if (velocity.Length() > ZeroGMaxSpeed)
            {
                velocity = velocity.Normalized() * ZeroGMaxSpeed;
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
                velocity.X = moveDirection.X * MoveSpeed;
                velocity.Z = moveDirection.Z * MoveSpeed;
            }
        }

        Velocity = velocity;
        MoveAndSlide();

        // The beam only exists while the flashlight is both held and toggled on — unequipping
        // or swapping it out of hand turns it off automatically without touching _flashlightOn.
        var holdingFlashlight = LeftHandItemId == "flashlight" || RightHandItemId == "flashlight";
        _flashlightSpot!.Visible = _flashlightOn && holdingFlashlight;

        // Suit resources keep draining while busy performing a verb — a task's duration is a
        // real elapsed-time cost, not a pause (docs/project-plan.md's "time acceleration ...
        // pays the full bill" framing). A breached room's dropping O2 burns the suit's own
        // reserve faster on top of the flat drain, and a lit flashlight burns suit power faster
        // too (see SuitResources.Tick).
        _suitResources.Tick(delta, roomVolume?.O2Fraction ?? 0.21, _flashlightSpot.Visible);
        _o2Bar!.Value = _suitResources.O2Percent;
        _powerBar!.Value = _suitResources.PowerPercent;

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
            _drillBar.Value = _inventory.Drill is { HasBattery: true } drill ? drill.Charge * 100 : 0;
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
    /// tool so far, see PlayerInventory.DrillState) — holding it isn't enough, it also needs an
    /// installed battery with real charge left.</summary>
    private bool IsAffordable(Verb verb) =>
        verb.Requirements.Count == 0 ||
        verb.Requirements.All(r =>
            (r.ItemId == LeftHandItemId || r.ItemId == RightHandItemId) &&
            _inventory.Has(r.ItemId, r.Count) &&
            (r.ItemId != "power_drill" || _inventory.Drill is { HasBattery: true, Charge: > 0f }));

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
            _targetNameLabel!.Text = Tr(displayNameKey);
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
            PowerPercent = _suitResources.PowerPercent,
            HungerPercent = _needs.HungerPercent,
            ThirstPercent = _needs.ThirstPercent,
            EnergyPercent = _needs.EnergyPercent,
            Inventory = new Dictionary<string, int>(_inventory.Hands.Counts),
            Credits = _credits,
            BackpackItemId = _inventory.Backpack?.ItemId,
            BackpackContents = _inventory.Backpack is { } backpack
                ? new Dictionary<string, int>(backpack.Contents.Counts)
                : new Dictionary<string, int>(),
            BackpackSlotCount = _inventory.Backpack?.Contents.Slots.Count ?? PlayerInventory.BackpackSlotCount,
            HasDrill = _inventory.Drill is not null,
            DrillHasBattery = _inventory.Drill?.HasBattery ?? false,
            DrillCharge = _inventory.Drill?.Charge ?? 0f,
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

        _suitResources.RestoreFrom(data.O2Percent, data.PowerPercent);
        _needs.RestoreFrom(data.HungerPercent, data.ThirstPercent, data.EnergyPercent);

        _inventory.Clear();
        foreach (var (itemId, count) in data.Inventory)
        {
            _inventory.Add(itemId, count);
        }

        // Backpack is reconstructed after the body replay above (still null at that point, so
        // those Add calls only ever fill body slots) and filled directly into its own fresh
        // SlotContainer, keeping body-refill and backpack-refill from cross-contaminating.
        if (data.BackpackItemId is { } backpackItemId)
        {
            var contents = new SlotContainer(data.BackpackSlotCount);
            foreach (var (itemId, count) in data.BackpackContents)
            {
                contents.Add(itemId, count);
            }

            _inventory.EquipContainerDirectly(backpackItemId, contents);
        }

        if (data.HasDrill)
        {
            _inventory.AttachDrill(data.DrillHasBattery, data.DrillCharge);
        }

        _credits = data.Credits;
    }

    public void RefillSuitResources() => _suitResources.RestoreFrom(100f, 100f);

    /// <summary>The Bunk's Sleep-completion hook — a full night's rest.</summary>
    public void RestEnergy() => _needs.Rest(100f);

    private void UpdateInventoryHud()
    {
        // Re-pointed every frame rather than only on equip/unequip: equipping/unequipping
        // creates a new SlotContainer instance, and this is the cheapest way to keep the panel's
        // backpack section always addressing whichever one (if any) is currently worn.
        _backpackGrid!.Visible = _inventory.Backpack is not null;

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

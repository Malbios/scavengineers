using System.Collections.Generic;
using System.Linq;

using Godot;
using Scavengineers.Sim.Atmosphere;
using Scavengineers.Sim.Grid;
using Scavengineers.Scripts.Contracts;
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

    /// <summary>Tools with real durability — worn down by actual use (see Interact), not by
    /// WearSystem's passive tick (that's ship fixtures only). Reuses the held item's own
    /// SlotContainer/PickupItem Charge field to mean "durability" here — a fresh tool already
    /// defaults to Charge 1f, so nothing changes for an old save.</summary>
    private static readonly string[] DurableToolIds = ["crowbar", "power_drill", "wrench"];

    // Placeholder/tunable — roughly 50 uses to fully wear down, same order of magnitude as
    // DrillChargeDrainPerUse's own battery-drain rate.
    private const float ToolWearPerUse = 0.02f;

    // Placeholder/tunable — ~15 min of continuous use per full battery, same pacing the
    // flashlight's old generic-Power drain used before Power was removed as a player stat.
    private const float FlashlightChargeDrainPerSecond = 1f / 900f;

    // Placeholder/tunable — a full O2 tank lasts ~5 minutes sealed, matching SuitResources'
    // own flat-counter pace (O2DrainPerSecond = 100/300) so donning the suit doesn't change the
    // felt EVA budget, just makes it swappable.
    private const float O2TankDrainPerSecond = 1f / 300f;

    // Placeholder/tunable — a full filter lasts ~10 minutes, matching SuitResources' own
    // Co2RiseWithFilterPerSecond pace (so the filter runs out right around when unfiltered CO2
    // buildup would have anyway).
    private const float SuitFilterDrainPerSecond = 1f / 600f;

    // Placeholder/tunable — a full suit battery lasts ~20 minutes of "tiny bit of overall usage"
    // (heating/cooling plus baseline life-support draw), much longer than the acute O2/filter
    // budgets since it's a background drain, not the primary EVA-time limiter.
    private const float SuitBatteryDrainPerSecond = 1f / 1200f;

    // Placeholder/tunable — the Hunger/Thirst/Energy-at-0% debuff (a slowdown, not a failure
    // state; that's O2/Health's job — see SuitResources).
    private const float NeedsDebuffMoveMultiplier = 0.5f;

    // Zero-g movement (placeholder/tunable) — triggers whenever the room's O2 reads at or below
    // ShipAtmosphereZone.ZeroGO2Threshold, with a little slack so the switch doesn't flicker right
    // at the boundary while a room is still venting/equalizing. Shared with
    // ShipAtmosphereZone's own real physics zero-g override for loose items.
    private const float ZeroGThrustAcceleration = 6f;
    private const float ZeroGDrag = 2f; // passive deceleration per second, always applied
    private const float ZeroGMaxSpeed = 3.5f;

    // Placeholder/tunable — a full N2 tank gives ~2.5 minutes of continuous thrust. A flat
    // per-second rate while any thrust input is held, not scaled by how many direction keys are
    // pressed at once: the actual acceleration applied is always ZeroGThrustAcceleration
    // (thrust.Normalized() makes it direction-only), so the fuel cost should match that constant
    // effort, not reward holding fewer keys for the same resulting push.
    private const float N2DrainPerSecondWhileThrusting = 1f / 150f;

    // Placeholder/tunable — CharacterBody3D doesn't push RigidBody3D obstacles on its own
    // (MoveAndSlide resolves the character's own motion but never applies a reciprocal impulse),
    // so a loose item the player walks into would otherwise never move.
    private const float ItemPushImpulse = 2f;

    // Decompression pull (placeholder/tunable) — an open floor/ceiling breach's "unsecured near a
    // hole" hazard, on top of the slow O2/pressure drain. Pulls toward the breach's own position
    // rather than a fixed direction, so it settles near the hole instead of launching you
    // straight through it — ZeroGDrag keeps fighting it the whole time.
    private const float DecompressionPullRange = 5f;
    private const float DecompressionPullAcceleration = 4f;

    // Placeholder/tunable — comfortably more than any current room's floor-to-ceiling height
    // (3m). A 2-deck ship's own worst-case gap is exactly ShipBuildTarget.DeckYOffset (2.05m) —
    // still comfortably inside this. A much taller aligned stack (~7+ decks) would need this
    // revisited.
    private const float FreefallRaycastDistance = 15f;

    // Placeholder/tunable — vertical speed while climbing a LadderVerbTarget.
    private const float ClimbSpeed = 2.5f;

    // Placeholder/tunable — how strongly horizontal position is pulled back toward the ladder's
    // rail each tick (a soft snap, not a teleport).
    private const float ClimbSnapStrength = 8f;

    // Placeholder/tunable — how far past either anchor (world Y) climbing auto-releases, so
    // release lands right at the destination deck's own floor rather than partway through it.
    private const float ClimbReleaseMargin = 0.15f;

    private bool _climbing;
    private float _climbBottomY;
    private float _climbTopY;
    private Vector2 _climbAnchorXZ;

    /// <summary>Which ship (and which of its tiles) currently governs the player's ambient O2
    /// reading — set at runtime by whichever <see cref="Scavengineers.Scripts.Ship.ShipAtmosphereZone"/>
    /// the player is standing in. A room can be sealed off from the rest of its own ship, so this
    /// follows the player's actual room, not just which ship they're on.</summary>
    public ShipSim? ShipSimRef { get; private set; }

    private Vector2I _ambientTile;

    /// <summary>The current room's floor/ceiling breach tracker, if it has one — null on ships
    /// without floor/ceiling construction (see ShipAtmosphereZone.BuildTargetRef). Drives the
    /// decompression-pull hazard in the zero-g branch of _PhysicsProcess.</summary>
    private ShipBuildTarget? _ambientBuildTarget;

    /// <summary>Queries physics space for whichever ShipAtmosphereZone currently contains the
    /// player, every physics frame, rather than caching whatever the last BodyEntered signal
    /// said — that signal-based approach missed real transitions in practice, since crossing a
    /// shared zone boundary within a single physics tick never fires "entered" at all. If no zone
    /// is found (e.g. a true gap between two rooms), the previous reading is deliberately left
    /// alone rather than cleared.
    ///
    /// Reads the zone's TileAt(GlobalPosition) — the player's actual current cell — rather than
    /// the zone's fixed representative Tile, since per-cell diffusion means a cell next to a
    /// fresh breach can read very differently from one across the same room.
    ///
    /// Returns the zone actually found THIS frame, not the cached fields above — the
    /// decompression-pull hazard needs this live signal: once the player has drifted out through
    /// a breach and left the room's zone, continuing to pull toward that breach position from the
    /// far side would pull them back inside, causing a pendulum instead of a clean exit. It also
    /// lets the pull tell "a breach in MY room" apart from one in some other room the atmosphere
    /// sim still considers connected (e.g. through a door since opened).</summary>
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

    // PDA scan-mode highlight (see IVerbTarget.HighlightVisual) — a dedicated SubViewport/Camera3D
    // pair renders ONLY whatever currently has ScanHighlightLayerBit set (a silhouette mask),
    // which a full-screen shader on _scanHighlightOverlay turns into a pulsing outline. Reserved
    // render layer 20 (1-indexed in the editor, hence the -1 below) — a layer nothing else in
    // this project uses, so setting/clearing it never affects normal rendering.
    private const int ScanHighlightLayer = 20;
    private static readonly uint ScanHighlightLayerBit = 1u << (ScanHighlightLayer - 1);
    private SubViewport? _scanHighlightViewport;
    private Camera3D? _scanHighlightCamera;
    private ColorRect? _scanHighlightOverlay;
    private IReadOnlyList<VisualInstance3D> _highlightedVisuals = [];

    /// <summary>Everything the HUD draws (bars, labels, overlays, formatting, Tr()) — see
    /// <see cref="PlayerHudView"/>. Player computes state and pushes a snapshot; it holds no
    /// Label/ProgressBar references of its own anymore.</summary>
    private PlayerHudView? _hud;

    /// <summary>Test-only observability — lets a NodeTest assert on rendered HUD state without
    /// reaching through a private field.</summary>
    public PlayerHudView? Hud => _hud;

    /// <summary>Every modal HUD panel — which are open, their nodes, and the world object each was
    /// opened from. See <see cref="PanelController"/>.</summary>
    private PanelController? _panels;

    public PanelController? Panels => _panels;

    /// <summary>The right-click-on-an-item sub-windows (drill/flashlight/backpack/suit/PDA) plus
    /// the thruster and storage slot grids — see <see cref="InventoryWindowView"/>. Distinct from
    /// <see cref="_panels"/>: these don't suppress gameplay input and aren't in
    /// <see cref="AnyPanelOpen"/>.</summary>
    private InventoryWindowView? _windows;

    public InventoryWindowView? Windows => _windows;

    /// <summary>Toggled by the scan-mode key (see _Input) — only ever true while
    /// <see cref="CanScan"/> also holds; forced back off the moment it stops holding, so taking
    /// off the PDA/cartridge/helmet mid-scan turns it off automatically.</summary>
    private bool _scanModeOn;

    /// <summary>Test-only observability for scan mode's toggle state — matches this codebase's
    /// existing narrow test-accessor pattern (see <see cref="SuppressMouseCaptureForTests"/>).</summary>
    public bool ScanModeOn => _scanModeOn;

    /// <summary>Test-only override for scan mode's toggle state — <see cref="CanScan"/> requires a
    /// helmet resolved via ItemCatalog.EquipSlot, which the isolated NodeTests catalog can't
    /// resolve (see PlayerScanModeTest's own doc comment), so scan mode can't be turned on through
    /// the real input gate in that project. Same narrow test-accessor pattern as
    /// <see cref="SuppressMouseCaptureForTests"/>/<see cref="ScanModeOn"/>.</summary>
    public void SetScanModeOnForTests(bool on) => _scanModeOn = on;

    /// <summary>Gates scan mode: the PDA worn, its one cartridge pocket holding the health-scan
    /// cartridge, and *any* helmet worn (checked via ItemCatalog.EquipSlot rather than hardcoding
    /// "eva_helmet" — a future helmet type qualifies automatically).</summary>
    private bool CanScan =>
        _inventory.GetEquippedContainer("pda") is { } pda
        && pda.Contents.CountOf("health_scan_cartridge") > 0
        && _inventory.Head is { ItemId: { } headItemId }
        && ItemCatalog.EquipSlot(headItemId) == "head";

    /// <summary>Toggled by the power-info key (see _Input) — same "forced back off the moment its
    /// gate stops holding" shape as <see cref="_scanModeOn"/>. Only needs the PDA worn with its
    /// power cartridge loaded — unlike CanScan, no helmet requirement: checking your own ship's
    /// power grid isn't an EVA/environmental concern the way the health scan is.</summary>
    private bool _powerInfoOn;

    private bool CanShowPowerInfo =>
        _inventory.GetEquippedContainer("pda") is { } pda
        && pda.Contents.CountOf("power_scan_cartridge") > 0;

    private readonly SuitResources _suitResources = new();
    private readonly PlayerNeeds _needs = new();
    private readonly PlayerInventory _inventory = new();
    private float _pitch;

    private SpotLight3D? _flashlightSpot;
    private bool _flashlightOn;

    /// <summary>The death fallback's reload target — SaveManager already holds the reverse
    /// PlayerRef (World.tscn), this is the same reference the other way round.</summary>
    [Export]
    public SaveManager? SaveManagerRef { get; set; }

    /// <summary>Any full-screen-ish HUD panel that should suppress normal gameplay input while
    /// open — shared gate for every _Input branch and both movement branches. The two with an
    /// extra movement freeze on top (Docking, because WASD becomes minigame thrust, and Death,
    /// because a 0-Health player shouldn't drift around behind the screen) are checked
    /// individually where that matters.</summary>
    private bool AnyPanelOpen => _panels?.AnyOpen ?? false;

    /// <summary>The game's whole known item catalog, doubling as the hotbar slots (keys 1-9, 0) —
    /// also reused by VendorVerbTarget as the set of things Buy can offer. "backpack" is an
    /// ordinary holdable/purchasable item until it's equipped via drag-and-drop onto the Back
    /// slot; "ration_bar"/"water_bottle" are likewise ordinary until F consumes whichever's held.</summary>
    public static readonly string[] HotbarItems = ["scrap_metal", "spare_parts", "wall_panel", "power_cell", "battery", "switch", "recharge_station", "thruster", "n2_tank", "backpack", "ration_bar", "water_bottle", "wrench", "pda", "health_scan_cartridge", "power_scan_cartridge", "small_bin", "shelf", "large_shelf"];

    private enum Hand { Left, Right }

    /// <summary>Which hand was filled most recently — the only piece of state hands need beyond
    /// PlayerInventory's own slots, since "which hand to replace when both are full" can't be
    /// derived from the slots themselves.</summary>
    private Hand? _lastFilledHand;

    public string? LeftHandItemId => _inventory.Slots[PlayerInventory.LeftHandSlotIndex]?.ItemId;

    public string? RightHandItemId => _inventory.Slots[PlayerInventory.RightHandSlotIndex]?.ItemId;

    public PlayerInventory Inventory => _inventory;

    // Placeholder starting stipend — tunable. Not modeled as an ItemRequirement (those pull
    // from PlayerInventory, a different resource) — VendorVerbTarget checks/spends
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

    /// <summary>Accepted-but-not-yet-resolved contracts — ticked down every physics frame (see
    /// TickActiveContracts) and checked for arrival-triggered completion (see
    /// OnArrivedAtDestination). Owned by Player, not by whichever ContractGiverVerbTarget the job
    /// came from — a contract is the player's own obligation.</summary>
    private readonly List<Contract> _activeContracts = new();

    // Placeholder/tunable starting value (never owed until a contract is actually missed) —
    // accrued by TickActiveContracts on expiry, paid down by SettlePendingDebt on arrival at any
    // Station.
    private int _pendingDebt;

    public int PendingDebt => _pendingDebt;

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

        _scanHighlightViewport = GetNode<SubViewport>("ScanHighlightViewport");
        _scanHighlightViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        _scanHighlightCamera = GetNode<Camera3D>("ScanHighlightViewport/ScanHighlightCamera");
        _scanHighlightCamera.CullMask = ScanHighlightLayerBit;
        _scanHighlightOverlay = GetNode<ColorRect>("HUD/ScanHighlightOverlay");
        (_scanHighlightOverlay.Material as ShaderMaterial)?.SetShaderParameter("mask_texture", _scanHighlightViewport.GetTexture());

        // Constructed here rather than authored into Player.tscn, so the HUD split stays invisible
        // to the scene file — no new node_paths wiring to resolve.
        _hud = new PlayerHudView { Name = "HudView" };
        AddChild(_hud);
        _hud.Bind(GetNode("HUD"));

        _panels = new PanelController { Name = "Panels" };
        AddChild(_panels);
        _panels.Bind(GetNode("HUD"), this, CaptureMouse);
        _panels.InventoryClosed += CloseItemWindows;

        _windows = new InventoryWindowView { Name = "InventoryWindows" };
        AddChild(_windows);
        _windows.Bind(GetNode("HUD"), this);

        _flashlightSpot = GetNode<SpotLight3D>("Head/Camera3D/FlashlightSpot");

        // The main panel's own equip slots all share the Hands container — the one set of slots
        // that isn't a sub-window, so it stays here rather than in InventoryWindowView.
        foreach (var child in GetNode("HUD/InventoryPanel/Layout/EquipSlots").GetChildren())
        {
            if (child is InventorySlotUI slot)
            {
                slot.Container = _inventory.Hands;
                slot.PlayerRef = this;
            }
        }

        // X button / right-click-on-background. The item sub-windows wire their own (see
        // InventoryWindowView.Bind); these three are the modal panels' equivalents.
        _panels.InventoryWindow!.CloseRequested += CloseInventory;
        _panels.ThrusterWindow!.CloseRequested += CloseThrusterInventory;
        _panels.StorageWindow!.CloseRequested += CloseStorageInventory;

        // Placeholder/tunable starting stipend, overwritten by ApplyPlayerState on load. The
        // debug backpack is attached first (dev convenience) so the stipend below spills into
        // its 24 slots instead of overflowing two bare hands.
        _inventory.EquipContainerDirectly("back", "debug_backpack", new SlotContainer(24));
        _inventory.Add("scrap_metal", 50);
        _inventory.Add("ration_bar", 3);
        _inventory.Add("water_bottle", 3);
        _inventory.Add("crowbar", 1);
        _inventory.Add("power_drill", 1);
        _inventory.AttachSpecializedSlot("drill_battery", hasItem: true, charge: 1f);
        _inventory.Add("debug_flashlight", 1);

        // Starter EVA suit, fully suited and fully charged — dev convenience, overwritten by
        // ApplyPlayerState on load like everything else here.
        _inventory.EquipContainerDirectly("torso", "eva_torso_suit", new SlotContainer(PlayerInventory.TorsoSlotCount));
        _inventory.AttachSpecializedSlot("suit_o2", hasItem: true, charge: 1f);
        _inventory.AttachSpecializedSlot("suit_n2", hasItem: true, charge: 1f);
        _inventory.AttachSpecializedSlot("suit_filter", hasItem: true, charge: 1f);
        _inventory.AttachSpecializedSlot("suit_battery", hasItem: true, charge: 1f);
        _inventory.EquipContainerDirectly("head", "eva_helmet", new SlotContainer(0));

        // Wrench (Maintain/Repair tool) plus a fully-loaded PDA — dev convenience so scan mode is
        // testable immediately rather than needing a shopping trip first.
        _inventory.Add("wrench", 1);
        _inventory.EquipContainerDirectly("pda", "pda", new SlotContainer(PlayerInventory.PdaSlotCount));
        _inventory.GetPersistentContents("pda")?.Add("health_scan_cartridge", 1);
        _inventory.GetPersistentContents("pda")?.Add("power_scan_cartridge", 1);

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
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && !IsBusy && !AnyPanelOpen && !_climbing)
        {
            Interact();
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } && !IsBusy && !AnyPanelOpen && !_climbing)
        {
            CycleSelectedVerb(1);
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } && !IsBusy && !AnyPanelOpen && !_climbing)
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
        // Every panel *except* the inventory blocks Tab — you can always toggle the inventory shut
        // again, but never open it over a shop/board/docking/death screen.
        else if (@event is InputEventKey { Keycode: Key.Tab, Pressed: true } && !IsBusy && !_panels!.AnyOpenExcept(PanelId.Inventory))
        {
            if (_panels!.IsOpen(PanelId.Inventory))
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
            // Closes whichever panel is topmost, or — with nothing open — releases the mouse.
            // Docking and Death deliberately have no Escape path (see PanelController's own
            // CloseTopmostClosable): neither offers a silent abandon.
            if (!_panels!.CloseTopmostClosable())
            {
                Input.MouseMode = Input.MouseModeEnum.Visible;
            }
        }
        else if (@event is InputEventKey { Keycode: Key.F, Pressed: true } && !IsBusy && !AnyPanelOpen)
        {
            UseHeldItem();
        }
        else if (@event is InputEventKey { Keycode: Key.V, Pressed: true } && !IsBusy && !AnyPanelOpen)
        {
            // Always allowed to turn off; only turns on if the gate (PDA worn + cartridge loaded
            // + any helmet worn) actually passes — see CanScan.
            _scanModeOn = !_scanModeOn && CanScan;
        }
        else if (@event is InputEventKey { Keycode: Key.P, Pressed: true } && !IsBusy && !AnyPanelOpen)
        {
            // Same "always allowed to turn off, only turns on if the gate passes" shape as V/CanScan.
            _powerInfoOn = !_powerInfoOn && CanShowPowerInfo;
        }
        else if (@event is InputEventKey { Pressed: true } hotbarKey && !IsBusy && !_climbing)
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

    /// <summary>Direct hotbar-style action on whatever's held, not a raycast-targeted verb.
    /// Eats/drinks a consumable (left hand first, then right) if one's held; otherwise toggles
    /// the flashlight on/off — a held real flashlight, or the debug flashlight if it's in
    /// inventory (it's never "held," so it has no hand-slot check of its own).</summary>
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

    /// <summary>Already held in a hand -> unequip that hand. Otherwise -> fill whichever hand is
    /// empty, or if both are full, replace whichever was filled most recently — Equip's own
    /// MoveBetween already swaps the displaced hand contents back into the backpack the new item
    /// came from, so no separate unequip step is needed for the replace case.</summary>
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

    /// <summary>Equips a backpack only if the dragged hand slot really is one.</summary>
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

    /// <summary>Places a bare, non-fungible item token (a worn container just taken off) directly
    /// into `destination` if it's a valid, ordinary, currently-empty slot compatible with the
    /// item — used by TryUnequipItem/TryUnequipBackpack so an unequip drop respects wherever the
    /// player actually dropped it, instead of always aiming for a hand. False (caller falls back
    /// to trying any hand) for a non-ordinary destination, an already-occupied slot, or an
    /// incompatible destination for an item that doesn't fit storage.</summary>
    private bool TryPlaceAt(InventorySlotUI? destination, string itemId)
    {
        if (destination is null
            || destination.IsBackSlot
            || destination.EquippedSlotName.Length > 0
            || destination.SpecializedSlotKey.Length > 0
            || destination.IsUnusedBodySlot
            || destination.Container is not { } container
            || container.Slots[destination.SlotIndex] is not null)
        {
            return false;
        }

        if (!ReferenceEquals(container, _inventory.Hands) && !ItemCatalog.FitsInStorage(itemId))
        {
            return false;
        }

        container.SetSlot(destination.SlotIndex, (itemId, 1, 1f));
        return true;
    }

    /// <summary>Its contents are permanent and never travel with this decision, so unequipping
    /// is just placing the bare backpack token: at the actual drop destination if it's a valid,
    /// empty, compatible ordinary slot; else in any hand with room; else it stays equipped
    /// ("nothing vanishes").</summary>
    public void TryUnequipBackpack(InventorySlotUI? destination = null)
    {
        if (_inventory.Backpack is not { } backpack)
        {
            return;
        }

        if (TryPlaceAt(destination, backpack.ItemId) || _inventory.Hands.Add(backpack.ItemId, 1) == 1)
        {
            _inventory.ClearBackpack();
        }
    }

    /// <summary>Equips only if the dragged item's ItemCatalog.EquipSlot actually declares this
    /// slot name, generalizing <see cref="TryEquipBackpackFromHand"/>'s hardcoded-"backpack" check
    /// into a tag-driven one. Reads/clears the dragged item from whatever real container it's
    /// currently sitting in — a hand, a worn backpack's contents, even the torso's own pocket
    /// slots. `containerSlotCount` is the inner inventory size to give the freshly-equipped item
    /// (0 for a container-less item like the helmet).</summary>
    public void TryEquipItemFrom(SlotContainer sourceContainer, int fromSlotIndex, string slotName, int containerSlotCount)
    {
        if (fromSlotIndex < 0 || fromSlotIndex >= sourceContainer.Slots.Count)
        {
            return;
        }

        if (sourceContainer.Slots[fromSlotIndex] is not { } occupied || ItemCatalog.EquipSlot(occupied.ItemId) != slotName)
        {
            return;
        }

        // Clears exactly this slot (not "the first slot anywhere holding this item id", the way
        // an aggregate TryRemove would) — correct regardless of stacking, and matches dragging a
        // specific slot's contents rather than a fungible count.
        sourceContainer.SetSlot(fromSlotIndex, occupied.Count > 1 ? (occupied.ItemId, occupied.Count - 1, occupied.Charge) : null);

        _inventory.EquipContainerDirectly(slotName, occupied.ItemId, new SlotContainer(containerSlotCount));

        if (slotName == "torso")
        {
            // The suit's 4 tank/filter/battery sub-slots are per-suit persistent state — they
            // don't come and go with worn state, so re-equipping a suit merely taken off (not
            // genuinely discarded) must NOT wipe whatever's already loaded. Only attach
            // fresh-empty ones the first time the suit is ever acquired.
            if (_inventory.SuitO2 is null) _inventory.AttachSpecializedSlot("suit_o2", hasItem: false, charge: 0f);
            if (_inventory.SuitN2 is null) _inventory.AttachSpecializedSlot("suit_n2", hasItem: false, charge: 0f);
            if (_inventory.SuitFilter is null) _inventory.AttachSpecializedSlot("suit_filter", hasItem: false, charge: 0f);
            if (_inventory.SuitBattery is null) _inventory.AttachSpecializedSlot("suit_battery", hasItem: false, charge: 0f);
        }
    }

    /// <summary>Called by an ordinary hand slot's InventorySlotUI when a worn Torso/Head item
    /// (dragged from its own equip slot) is dropped onto it — or, when `destination` is null, by
    /// a path with no specific drop target. Generalizes <see cref="TryUnequipBackpack"/>'s shape
    /// to any equip-slot name: its contents (and, for the suit, its tank/filter/battery sub-slots)
    /// are permanent and never travel with this decision (see PlayerInventory's
    /// persistent-contents model), so unequipping is just placing the bare item token: at the
    /// actual drop destination if it's a valid, empty, compatible ordinary slot; else in any hand
    /// with room; else it stays equipped ("nothing vanishes").</summary>
    public void TryUnequipItem(string slotName, InventorySlotUI? destination = null)
    {
        if (_inventory.GetEquippedContainer(slotName) is not { } equipped)
        {
            return;
        }

        if (TryPlaceAt(destination, equipped.ItemId) || _inventory.Hands.Add(equipped.ItemId, 1) == 1)
        {
            _inventory.ClearEquippedContainer(slotName);
        }
    }

    /// <summary>Which equip slot a container-carrying item's ContainerPickupItem should re-equip
    /// into on pickup, for a caller that only knows the item id, not the slot it came from — the
    /// backpack's is hardcoded by item id, since its equip slot was never expressed as an
    /// items.json <c>equipSlot</c>; everything else defers to ItemCatalog.EquipSlot.</summary>
    private static string EquipSlotNameFor(string itemId) =>
        itemId is "backpack" or "debug_backpack" ? "back" : ItemCatalog.EquipSlot(itemId) ?? "back";

    /// <summary>Captures the EVA suit torso's tank/filter/battery sub-slot state and fully
    /// detaches all four — the one place this state is actually removed from the player, used by
    /// every genuine-discard path so it travels onto the dropped world item (see
    /// ContainerPickupItem) instead of the "eject back into general inventory" behavior a mere
    /// unequip uses. No-op (all null) unless `isSuit` is true.</summary>
    private ((bool, float)? O2, (bool, float)? N2, (bool, float)? Filter, (bool, float)? Battery) CaptureAndDetachSuitTanks(bool isSuit)
    {
        if (!isSuit)
        {
            return (null, null, null, null);
        }

        var o2 = _inventory.SuitO2 is { } s1 ? ((bool, float)?)(s1.HasItem, s1.Charge) : null;
        var n2 = _inventory.SuitN2 is { } s2 ? ((bool, float)?)(s2.HasItem, s2.Charge) : null;
        var filter = _inventory.SuitFilter is { } s3 ? ((bool, float)?)(s3.HasItem, s3.Charge) : null;
        var battery = _inventory.SuitBattery is { } s4 ? ((bool, float)?)(s4.HasItem, s4.Charge) : null;

        _inventory.DetachSpecializedSlot("suit_o2");
        _inventory.DetachSpecializedSlot("suit_n2");
        _inventory.DetachSpecializedSlot("suit_filter");
        _inventory.DetachSpecializedSlot("suit_battery");

        return (o2, n2, filter, battery);
    }

    /// <summary>Spawns a full container's world representation at `position` — used both for an
    /// unequip-while-full drop (see TryUnequipBackpack) and by SaveManager to respawn dropped
    /// containers on load. `equipSlotName` defaults to a catalog-derived guess (see
    /// EquipSlotNameFor) for a caller with no better information — pass it explicitly wherever
    /// it's already known. The 4 tank params are only ever non-null for a genuinely-discarded EVA
    /// suit.</summary>
    public void SpawnDroppedContainer(string itemId, SlotContainer contents, Vector3 position, string? equipSlotName = null,
        (bool HasItem, float Charge)? suitO2 = null, (bool HasItem, float Charge)? suitN2 = null,
        (bool HasItem, float Charge)? suitFilter = null, (bool HasItem, float Charge)? suitBattery = null)
    {
        var pickup = new ContainerPickupItem
        {
            ItemId = itemId,
            Contents = contents,
            EquipSlotName = equipSlotName ?? EquipSlotNameFor(itemId),
            SuitO2 = suitO2,
            SuitN2 = suitN2,
            SuitFilter = suitFilter,
            SuitBattery = suitBattery,
        };
        GetParent()?.AddChild(pickup);
        pickup.GlobalPosition = position;
    }

    // Placeholder/tunable — how far you can reach to place a dropped item.
    private const float MaxDropReachMeters = 3.0f;

    /// <summary>Called by the world-drop zone (see WorldDropZone._DropData) once a drag ends
    /// outside every existing panel/slot Control. A no-op if nothing solid is within reach in the
    /// drop's screen direction.</summary>
    public void TryDropInWorld(InventorySlotUI source, Vector2 screenPosition)
    {
        if (ResolveWorldDropPosition(screenPosition) is not { } dropTarget)
        {
            return;
        }

        var (position, normal) = dropTarget;

        if (source.SpecializedSlotKey.Length > 0)
        {
            if (_inventory.EjectSpecializedSlotForWorld(source.SpecializedSlotKey) is { } charge
                && PlayerInventory.SpecializedSlotAcceptedItemId(source.SpecializedSlotKey) is { } itemId)
            {
                InventoryOverflow.DropAt(this, itemId, 1, RestingDropPosition(position, normal, itemId), charge);
            }

            return;
        }

        if (source.IsBackSlot)
        {
            DropBackpackInWorld(position, normal);
            return;
        }

        if (source.EquippedSlotName.Length > 0)
        {
            DropEquippedItemInWorld(source.EquippedSlotName, position, normal);
            return;
        }

        if (source.Container is not { } container || source.SlotIndex < 0 || source.SlotIndex >= container.Slots.Count
            || container.Slots[source.SlotIndex] is not { } slot)
        {
            return;
        }

        // A bare, non-fungible container token (a backpack or suit merely being carried, not
        // worn) has permanent contents of its own that must travel with it into the world, same
        // "genuine discard" treatment a worn one gets below — otherwise they'd be silently
        // orphaned in the player's persistent-contents map, unreachable but never freed.
        if (_inventory.GetPersistentContents(slot.ItemId) is { } persistentContents)
        {
            container.SetSlot(source.SlotIndex, null);
            _inventory.DiscardPersistentContents(slot.ItemId);
            var (o2, n2, filter, battery) = CaptureAndDetachSuitTanks(ItemCatalog.EquipSlot(slot.ItemId) == "torso");
            SpawnDroppedContainer(slot.ItemId, persistentContents, RestingDropPosition(position, normal, slot.ItemId), null, o2, n2, filter, battery);
            return;
        }

        container.SetSlot(source.SlotIndex, null);
        InventoryOverflow.DropAt(this, slot.ItemId, slot.Count, RestingDropPosition(position, normal, slot.ItemId), slot.Charge);
    }

    /// <summary>Always drops the backpack (empty or not) at `position` — unlike
    /// TryUnequipBackpack's onto-a-hand-slot gesture, which prefers stashing an empty backpack
    /// back into a hand instead. A genuine discard, so its persistent contents leave the player
    /// entirely rather than just being unworn.</summary>
    private void DropBackpackInWorld(Vector3 position, Vector3 normal)
    {
        if (_inventory.Backpack is not { } backpack)
        {
            return;
        }

        _inventory.ClearBackpack();
        _inventory.DiscardPersistentContents(backpack.ItemId);
        SpawnDroppedContainer(backpack.ItemId, backpack.Contents, RestingDropPosition(position, normal, backpack.ItemId), "back");
    }

    /// <summary>Same shape as <see cref="DropBackpackInWorld"/>, generalized to any Torso/Head/Pda
    /// equip slot.</summary>
    private void DropEquippedItemInWorld(string slotName, Vector3 position, Vector3 normal)
    {
        if (_inventory.GetEquippedContainer(slotName) is not { } equipped)
        {
            return;
        }

        _inventory.ClearEquippedContainer(slotName);
        _inventory.DiscardPersistentContents(equipped.ItemId);
        var (o2, n2, filter, battery) = CaptureAndDetachSuitTanks(slotName == "torso");

        SpawnDroppedContainer(equipped.ItemId, equipped.Contents, RestingDropPosition(position, normal, equipped.ItemId), slotName, o2, n2, filter, battery);
    }

    /// <summary>Projects a ray from the camera through the drop's screen position — the first
    /// mouse-position-based raycast in this codebase (InteractRay is a fixed forward crosshair
    /// ray). Returns null (refuse the drop) if nothing solid is within MaxDropReachMeters, e.g.
    /// aiming out an open breach or off a platform's edge.</summary>
    private (Vector3 Position, Vector3 Normal)? ResolveWorldDropPosition(Vector2 screenPosition)
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

        var normal = (Vector3)result["normal"];

        // Nudge off the surface along its normal so the item doesn't spawn half-embedded — a
        // small fixed amount, enough to dodge z-fighting. NOT enough clearance on its own for a
        // bulkier item to avoid spawning past the surface's own collision shape (see
        // RestingDropPosition, which every spawn call site adds on top of this).
        return ((Vector3)result["position"] + normal * 0.05f, normal);
    }

    /// <summary>Adds this item's own collision half-extent on top of ResolveWorldDropPosition's
    /// small fixed surface nudge, along the surface's real hit normal rather than always assuming
    /// "up" — a fixed up-nudge worked fine for a floor hit but did nothing to clear a WALL hit (a
    /// near-horizontal normal), so a dropped item aimed at a wall spawned still embedded in it.</summary>
    private static Vector3 RestingDropPosition(Vector3 surfacePosition, Vector3 surfaceNormal, string itemId) =>
        surfacePosition + surfaceNormal * ItemVisualBuilder.RestingHalfHeight(ItemCatalog.ShapeKind(itemId));

    /// <summary>Test harnesses that instantiate a real Player (see
    /// Scavengineers.NodeTests/PlayerTestHarness.cs) run inside a real, non-headless Godot
    /// window (GdUnit4's own test runner) — without this, every such test would capture the
    /// developer's actual OS mouse into that window for the run's duration. Off by default; only
    /// ever set by test code.</summary>
    public static bool SuppressMouseCaptureForTests { get; set; }

    // Instance method, not static: the FocusEntered connection below is then tied to this
    // Player's lifetime, so Godot auto-disconnects it when this instance is freed. A static
    // method's connection has no instance to track, so travelling between scenes would try to
    // reconnect the exact same callable and hit "already connected." Not mechanically covered by
    // an automated test — SuppressMouseCaptureForTests makes CaptureMouse() a no-op for the whole
    // NodeTests suite, so it can't observe the AnyPanelOpen branch either. Verified by code
    // review only: regaining OS window focus used to unconditionally recapture the mouse even
    // with a panel open, yanking it into FPS-look mode with e.g. the shop still visible underneath.
    private void CaptureMouse()
    {
        if (SuppressMouseCaptureForTests || AnyPanelOpen)
        {
            return;
        }

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void OpenInventory() => _panels!.Open(PanelId.Inventory);

    private void CloseInventory() => _panels!.Close(PanelId.Inventory);

    /// <summary>Drops every right-click-on-item sub-window — none of them are useful without the
    /// main inventory panel open to drag items to and from. Subscribed to
    /// PanelController.InventoryClosed rather than called from CloseInventory directly, so it also
    /// fires when the panel is closed via its own X button or the Escape chain.</summary>
    private void CloseItemWindows() => _windows!.CloseAll();

    /// <summary>Called by TravelConsoleVerbTarget.ExecuteVerb — opens the map instead of that
    /// verb starting travel directly, same shape as OpenInventory but triggered from a world
    /// interaction rather than a hotkey.</summary>
    public void OpenTravelMap(TravelConsoleVerbTarget console) => _panels!.OpenTravelMap(console);

    /// <summary>Called by TravelMapPanel's Travel button — hands the chosen destination back to
    /// whichever console opened the map, then closes it regardless of whether travel actually
    /// started (BeginTravel itself no-ops for an already-current/out-of-range destination).</summary>
    public void ConfirmTravel(int destinationId)
    {
        _panels!.OpenTravelConsole?.BeginTravel(destinationId);
        CloseTravelMap();
    }

    public void CloseTravelMap() => _panels!.Close(PanelId.TravelMap);

    /// <summary>Called by TravelConsoleVerbTarget.OnTravelComplete — opens automatically once the
    /// travel timer elapses (not requiring a second console interaction, since the player already
    /// committed via the travel map) and starts the minigame's first attempt.</summary>
    public void OpenDockingMinigame(TravelConsoleVerbTarget console) => _panels!.OpenDocking(console);

    /// <summary>Called by DockingMinigamePanel's Dock button once it succeeds — resolves arrival
    /// on whichever console opened the minigame, then closes it. No cancel/Escape path exists
    /// (see _dockingOpen's own doc comment) — this is the only way the panel ever closes.</summary>
    public void CompleteDocking()
    {
        _panels!.OpenDockingConsole?.CompleteDocking();
        CloseDockingMinigame();
    }

    public void CloseDockingMinigame() => _panels!.Close(PanelId.Docking);

    /// <summary>Called by VendorVerbTarget.ExecuteVerb — opens the shop panel instead of
    /// the old per-item Buy/Sell verb-cycling, same shape as OpenTravelMap.</summary>
    public void OpenShop(VendorVerbTarget vendor)
    {
        _panels!.OpenShop(vendor);
        RefreshShop();
    }

    /// <summary>Called by ShopPanel's row buttons — buys/sells one unit and immediately refreshes
    /// the panel's rows (price/affordability/ownership all shift after every transaction), rather
    /// than closing the panel like a one-shot travel confirmation would.</summary>
    public void BuyItem(string itemId)
    {
        _panels!.OpenVendor?.TryBuy(itemId);
        RefreshShop();
    }

    public void SellItem(string itemId)
    {
        _panels!.OpenVendor?.TrySell(itemId);
        RefreshShop();
    }

    private void RefreshShop()
    {
        if (_panels!.OpenVendor is { } vendor)
        {
            _panels.Shop!.Populate(vendor.BuildBuyEntries(), vendor.BuildSellEntries());
        }
    }

    public void CloseShop() => _panels!.Close(PanelId.Shop);

    /// <summary>Called by ContractGiverVerbTarget.ExecuteVerb — same shape as OpenShop.</summary>
    public void OpenContractBoard(ContractGiverVerbTarget giver)
    {
        _panels!.OpenContractBoard(giver);
        RefreshContractBoard();
    }

    /// <summary>Moves the named offer from the giver's board into this player's active list so it
    /// can't be double-accepted, seeding its countdown from the rolled Contract.RemainingSeconds.</summary>
    public void AcceptContract(string instanceId)
    {
        if (_panels!.OpenContractGiver?.TryTakeOffer(instanceId) is { } contract)
        {
            _activeContracts.Add(contract);
        }

        RefreshContractBoard();
    }

    /// <summary>Only ever wired up as actionable for RetrieveItem/SalvageQuota (turn in anywhere)
    /// and CargoDelivery (turn in only at the destination Station's contract-giver); Survey
    /// completes automatically on arrival instead (see OnArrivedAtDestination). The type/location
    /// check below is a defensive backstop, not the primary gate.</summary>
    public void TryTurnInContract(string instanceId)
    {
        var contract = _activeContracts.FirstOrDefault(c => c.InstanceId == instanceId);
        if (contract is { } found
            && CanTurnIn(found)
            && found.ItemId is { } itemId
            && _inventory.Has(itemId, found.Count)
            && _inventory.TryRemove(itemId, found.Count))
        {
            AddCredits(found.Reward);
            _activeContracts.Remove(found);
        }

        RefreshContractBoard();
    }

    /// <summary>Whether `contract` is even the *kind* that turns in via TryTurnInContract, and —
    /// for CargoDelivery — whether the currently open contract-giver's own Station is the
    /// contract's destination.</summary>
    private bool CanTurnIn(Contract contract) => contract.Type switch
    {
        ContractType.RetrieveItem or ContractType.SalvageQuota => true,
        ContractType.CargoDelivery => _panels!.OpenContractGiver?.ConsoleRef?.CurrentDestinationId == contract.DestinationStationId,
        _ => false,
    };

    private void RefreshContractBoard()
    {
        if (_panels!.OpenContractGiver is not { } giver)
        {
            return;
        }

        var activeEntries = _activeContracts
            .Select(c => new ContractBoardEntry(
                c.InstanceId,
                giver.Describe(c),
                ActionAvailable: CanTurnIn(c) && c.ItemId is { } itemId && _inventory.Has(itemId, c.Count)))
            .ToList();

        _panels.ContractBoard!.Populate(giver.BuildAvailableEntries(), activeEntries);
    }

    public void CloseContractBoard() => _panels!.Close(PanelId.ContractBoard);

    /// <summary>A contract's deadline keeps running whether or not the board is on screen. Expiry
    /// removes it and adds its FailureFee to PendingDebt rather than quietly vanishing.</summary>
    private void TickActiveContracts(double delta)
    {
        foreach (var contract in _activeContracts.ToList())
        {
            contract.RemainingSeconds -= (float)delta;
            if (contract.RemainingSeconds <= 0f)
            {
                _pendingDebt += contract.FailureFee;
                _activeContracts.Remove(contract);
            }
        }
    }

    /// <summary>Called right after a real arrival (not a save-load restore, which calls
    /// ApplyCurrentLocation directly without this hook) — settles any owed debt the instant the
    /// ship docks at any Station, and auto-completes Survey contracts targeting wherever it just
    /// arrived. RetrieveItem/SalvageQuota/CargoDelivery complete via TryTurnInContract instead.</summary>
    public void OnArrivedAtDestination(int destinationId, bool isStation)
    {
        if (isStation)
        {
            SettlePendingDebt();
        }

        foreach (var contract in _activeContracts.ToList())
        {
            var completed = contract.Type switch
            {
                ContractType.Survey => contract.TargetDestinationId == destinationId && HasCartridgeEquipped(contract.ItemId),
                _ => false,
            };

            if (completed)
            {
                AddCredits(contract.Reward);
                _activeContracts.Remove(contract);
            }
        }
    }

    private bool HasCartridgeEquipped(string? cartridgeId) =>
        cartridgeId is not null && _inventory.GetEquippedContainer("pda") is { } pda && pda.Contents.CountOf(cartridgeId) > 0;

    /// <summary>Pays down PendingDebt by whatever's affordable right now — any shortfall just
    /// carries over to the next Station arrival.</summary>
    private void SettlePendingDebt()
    {
        var payment = System.Math.Min(_pendingDebt, _credits);
        if (payment > 0 && TrySpendCredits(payment))
        {
            _pendingDebt -= payment;
        }
    }

    public void OpenThrusterInventory(ThrusterVerbTarget thruster) => _panels!.OpenThrusterInventory(thruster);

    public void CloseThrusterInventory() => _panels!.Close(PanelId.Thruster);

    public void OpenStorageInventory(StorageVerbTarget storage) => _panels!.OpenStorageInventory(storage);

    public void CloseStorageInventory() => _panels!.Close(PanelId.Storage);

    public void ToggleItemWindow(string itemId) => _windows!.ToggleItemWindow(_inventory, itemId);

    public override void _PhysicsProcess(double delta)
    {
        TickActiveContracts(delta);

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
        // Climbing overrides the normal grounded/zero-g fork for this tick regardless of the
        // room's atmosphere/floor reading — a mid-climb player between two decks needs Floating
        // (no floor-snap fighting the rail) either way.
        MotionMode = _climbing || inZeroG ? MotionModeEnum.Floating : MotionModeEnum.Grounded;

        // A ship with no life support never regenerates air, so a room's O2Fraction can stay at
        // "reads as vacuum" even after its own breach is patched and resealed — inZeroG alone
        // can't tell "still actively venting" apart from "permanently dead air, but safe now."
        // The decompression-pull hazard needs the real graph check instead, or it keeps pulling
        // toward an unrelated breach elsewhere on the ship through a closed, sealed door.
        var isConnectedToOutside = ShipSimRef?.Atmosphere?.IsConnectedToOutside(currentCell) ?? false;

        var needsDebuffActive = _needs.HungerPercent <= 0f || _needs.ThirstPercent <= 0f || _needs.EnergyPercent <= 0f;
        var moveMultiplier = needsDebuffActive ? NeedsDebuffMoveMultiplier : 1f;

        var velocity = Velocity;

        if (_climbing)
        {
            if (IsBusy || AnyPanelOpen)
            {
                // Freeze movement rather than force an exit — grabbing on again resumes exactly
                // where you left off instead of losing your place on the ladder.
                velocity = Vector3.Zero;
            }
            else
            {
                var verticalInput = 0f;
                if (Input.IsPhysicalKeyPressed(Key.W))
                {
                    verticalInput += 1f;
                }

                if (Input.IsPhysicalKeyPressed(Key.S))
                {
                    verticalInput -= 1f;
                }

                velocity.X = (_climbAnchorXZ.X - GlobalPosition.X) * ClimbSnapStrength;
                velocity.Z = (_climbAnchorXZ.Y - GlobalPosition.Z) * ClimbSnapStrength;
                velocity.Y = verticalInput * ClimbSpeed;

                // Space lets go early; otherwise auto-release once past either anchor by
                // ClimbReleaseMargin — deliberately NOT Escape too, since Escape's own _Input
                // handler falls through to un-capturing the mouse when no panel is open, which
                // would be a jarring side effect of just letting go of a ladder.
                if (Input.IsPhysicalKeyPressed(Key.Space)
                    || GlobalPosition.Y >= _climbTopY + ClimbReleaseMargin
                    || GlobalPosition.Y <= _climbBottomY - ClimbReleaseMargin)
                {
                    _climbing = false;
                    velocity = Vector3.Zero;
                }
            }
        }
        else if (inZeroG)
        {
            // Thrust-based, not direct-velocity — you drift and have to counter-thrust to stop.
            velocity = velocity.MoveToward(Vector3.Zero, ZeroGDrag * (float)delta);

            // Computed once, reused by both the thrust block below and the decompression-pull
            // block further down — a real jetpack (torso worn, N2 tank charged) means you can
            // actively counter being sucked toward a breach, not just move under your own power.
            var hasWorkingThrusters = _inventory.Torso is not null && _inventory.SuitN2 is { HasItem: true, Charge: > 0f };

            if (!IsBusy && !_panels!.IsOpen(PanelId.Death) && !_panels.IsOpen(PanelId.Docking))
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

                // The EVA suit's N2 tank is the jetpack — sustained thrust only while the torso is
                // worn with a charged N2 tank. No suit/no N2/empty N2 means thrust input is
                // simply ignored (pure drift, ZeroGDrag above still applies). Torso worn is
                // checked explicitly, not just SuitN2 non-null, because the suit's tank state is
                // persistent/decoupled from worn state — a loaded N2 tank can exist while the
                // suit merely sits in a hand.
                if (thrust != Vector3.Zero && hasWorkingThrusters)
                {
                    velocity += thrust.Normalized() * ZeroGThrustAcceleration * moveMultiplier * (float)delta;
                    _inventory.DrainSpecializedSlot("suit_n2", N2DrainPerSecondWhileThrusting * (float)delta);
                }
            }
            else
            {
                velocity = Vector3.Zero;
            }

            // Not gated on IsNearSurface/IsBusy like thrust above — decompression isn't
            // something you opt into by pressing a key, it applies to anyone unsecured near an
            // open breach. It IS gated on hasWorkingThrusters, though: a real jetpack is precisely
            // the tool that counters rushing air, so a fully-suited player with a charged N2 tank
            // can hold position/fly against the pull instead of being dragged out regardless of
            // input — someone without a working suit (no torso, no/empty N2) has no way to resist
            // it at all, which is what makes an unsecured breach dangerous in the first place.
            // Full DecompressionPullAcceleration applies at a constant rate any time inZeroG is
            // true (i.e. the room is actively vented) — NOT scaled by the room's current Pressure:
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
            if (!hasWorkingThrusters && _ambientBuildTarget is not null && ambientZone is not null && isConnectedToOutside)
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

            if (IsBusy || _panels!.IsOpen(PanelId.Death) || _panels.IsOpen(PanelId.Docking))
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

        // Shove any loose item the player collided with this frame — see ItemPushImpulse for why
        // this doesn't happen automatically.
        for (var i = 0; i < GetSlideCollisionCount(); i++)
        {
            if (GetSlideCollision(i).GetCollider() is RigidBody3D { Freeze: false } rigidBody)
            {
                rigidBody.ApplyCentralImpulse(-GetSlideCollision(i).GetNormal() * ItemPushImpulse);
            }
        }

        // The real beam only exists while the flashlight is both held and toggled on, and its
        // battery still has charge — unequipping/swapping it out of hand turns it off
        // automatically without touching _flashlightOn, and an empty battery just as silently
        // stops it lighting.
        var holdingFlashlight = LeftHandItemId == "flashlight" || RightHandItemId == "flashlight";
        var realFlashlightOn = _flashlightOn && holdingFlashlight && _inventory.Flashlight is { HasItem: true, Charge: > 0f };
        // Deliberately still keyed to the literal debug item, not ItemCatalog.IsToggleableLight —
        // that flag only means "the F-key toggle applies," not "bypasses hand/battery gating on
        // merely carrying it." Generalizing this check would make an unheld real flashlight
        // sitting in the backpack incorrectly project a beam too.
        var debugFlashlightOn = _flashlightOn && _inventory.Has("debug_flashlight", 1);
        _flashlightSpot!.Visible = realFlashlightOn || debugFlashlightOn;

        if (realFlashlightOn)
        {
            _inventory.Flashlight!.Charge = Mathf.Max(0f, _inventory.Flashlight.Charge - FlashlightChargeDrainPerSecond * (float)delta);
        }

        // A burning cell is real smoke — it drains O2 faster and gets a screen overlay so it's
        // felt, not just read off the O2 bar. Checked across the player's whole current room
        // (ComponentContaining), not just _ambientTile's exact cell, since a fire is a specific
        // cell that could be a different tile in the same room from the zone's own representative.
        var inSmoke = ShipSimRef?.Atmosphere?.ComponentContaining(currentCell).Any(ShipSimRef.Deck.IsOnFire) ?? false;

        // Suit resources keep draining while busy performing a verb — a task's duration is a real
        // elapsed-time cost, not a pause. Once O2 bottoms out, it starts draining Health instead —
        // a hard 0-Health death, handled below.
        var ambientTemperature = roomVolume?.Temperature ?? AtmosphereVolume.Breathable.Temperature;

        // "Sealed" = the EVA suit's torso is worn; each tank/filter/battery reads as depleted
        // when it's missing entirely (no torso worn at all) or genuinely out of charge — an
        // installed-but-empty sub-slot is exactly as dangerous as no tank at all.
        var suitSealed = _inventory.Torso is not null;
        var o2TankDepleted = _inventory.SuitO2 is not { HasItem: true, Charge: > 0f };
        var n2TankDepleted = _inventory.SuitN2 is not { HasItem: true, Charge: > 0f };
        var filterDepleted = _inventory.SuitFilter is not { HasItem: true, Charge: > 0f };
        var batteryDepleted = _inventory.SuitBattery is not { HasItem: true, Charge: > 0f };

        _suitResources.Tick(delta, roomVolume?.O2Fraction ?? 0.21, ambientTemperature, inSmoke,
            suitSealed, o2TankDepleted, n2TankDepleted, filterDepleted, batteryDepleted);

        // Tank/filter/battery charge bookkeeping lives here, not in SuitResources itself — each
        // only drains while actually doing its job. N2 isn't drained here — it's input-driven by
        // sustained-thrust movement.
        if (suitSealed && !o2TankDepleted)
        {
            _inventory.DrainSpecializedSlot("suit_o2", O2TankDrainPerSecond * (float)delta);
        }

        if (suitSealed && !filterDepleted)
        {
            _inventory.DrainSpecializedSlot("suit_filter", SuitFilterDrainPerSecond * (float)delta);
        }

        if (suitSealed && !batteryDepleted)
        {
            _inventory.DrainSpecializedSlot("suit_battery", SuitBatteryDrainPerSecond * (float)delta);
        }

        if (_suitResources.HealthPercent <= 0f && !_panels!.IsOpen(PanelId.Death))
        {
            Die();
        }

        _needs.Tick(delta);

        // A tool's charge is only surfaced while it's actually in hand. Null means "not held",
        // which the view renders as hidden.
        var holdingDrill = LeftHandItemId == "power_drill" || RightHandItemId == "power_drill";

        _hud!.RenderVitals(new PlayerHudView.Vitals(
            O2Percent: _suitResources.O2Percent,
            CO2Percent: _suitResources.CO2Percent,
            SuitSealed: suitSealed,
            HealthPercent: _suitResources.HealthPercent,
            IsFreezing: _suitResources.IsFreezing,
            IsBurning: _suitResources.IsBurning,
            InSmoke: inSmoke,
            HungerPercent: _needs.HungerPercent,
            ThirstPercent: _needs.ThirstPercent,
            EnergyPercent: _needs.EnergyPercent,
            RoomO2Fraction: roomVolume?.O2Fraction,
            DrillCharge: holdingDrill ? _inventory.Drill is { HasItem: true } drill ? drill.Charge : 0f : null,
            FlashlightCharge: holdingFlashlight ? _inventory.Flashlight is { HasItem: true } flashlight ? flashlight.Charge : 0f : null));

        UpdateVerbHud();
        PhysicsFramesProcessed++;
    }

    /// <summary>Test-only observability: how many times this Player has completed a full
    /// _PhysicsProcess. Tests need this because SceneTree's PhysicsFrame signal fires at the
    /// *start* of a physics frame, before any node's _PhysicsProcess runs, so awaiting it and
    /// then asserting on HUD state is a race that loses under load.</summary>
    public long PhysicsFramesProcessed { get; private set; }

    /// <summary>Starts continuous climbing between the two given world points (each deck's own
    /// floor height at the ladder tile). Refused while busy with another verb or any HUD panel
    /// is open.</summary>
    public void BeginClimbing(Vector3 bottomWorld, Vector3 topWorld)
    {
        if (IsBusy || AnyPanelOpen)
        {
            return;
        }

        _climbing = true;
        _climbBottomY = bottomWorld.Y;
        _climbTopY = topWorld.Y;
        _climbAnchorXZ = new Vector2(bottomWorld.X, bottomWorld.Z);
    }

    public bool IsClimbing => _climbing;

    /// <summary>No ship structure within a generous distance straight down — you've walked/fallen
    /// off the edge of a ship into open space. Independent of room O2: atmosphere venting is
    /// gradual (AtmosphereSystem.Vent's Lerp), so a freshly-breached room can take several seconds
    /// to read as vacuum — too slow to save you from an immediate fall through a hole you just
    /// made.</summary>
    private bool NoFloorBelow()
    {
        var spaceState = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(
            GlobalPosition, GlobalPosition + Vector3.Down * FreefallRaycastDistance);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        // Layer 1 only — the Home Ship's floor aim-helper body (layer "build_aim_only") is
        // deliberately non-blocking and must not register as real floor here.
        query.CollisionMask = 1;

        return spaceState.IntersectRay(query).Count == 0;
    }

    /// <summary>Whether the player's interact ray is on this exact target right now — used by
    /// TravelConsoleVerbTarget to decide whether arrival should pop the docking panel open
    /// immediately or wait for the player to interact with the console again.</summary>
    public bool IsLookingAt(IVerbTarget target) => ReferenceEquals(GetCurrentVerbTarget(), target);

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

        foreach (var toolId in DurableToolIds)
        {
            if (verb.Requirements.Any(r => r.ItemId == toolId))
            {
                _inventory.DamageToolInHand(toolId, ToolWearPerUse);
            }
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
    /// Requirements needs the player to actually be holding that exact item in either hand with
    /// enough of it in the inventory — the single place every item-gated verb is gated, so a
    /// target never needs its own affordability logic. One extra clause is specific to the power
    /// drill — holding it isn't enough, it also needs an installed battery with real charge left.
    /// Another gates every DurableToolIds entry on its own held-slot durability — a worn-out
    /// crowbar/wrench/drill simply stops working until replaced.</summary>
    private bool IsAffordable(Verb verb) =>
        verb.Requirements.Count == 0 ||
        verb.Requirements.All(r =>
            (r.ItemId == LeftHandItemId || r.ItemId == RightHandItemId) &&
            _inventory.Has(r.ItemId, r.Count) &&
            (r.ItemId != "power_drill" || _inventory.Drill is { HasItem: true, Charge: > 0f }) &&
            (!DurableToolIds.Contains(r.ItemId) || ToolCharge(r.ItemId) > 0f));

    /// <summary>The Charge (durability) of whichever hand currently holds `itemId` — 0 if it
    /// isn't actually held right now.</summary>
    private float ToolCharge(string itemId) =>
        (LeftHandItemId == itemId ? _inventory.Slots[PlayerInventory.LeftHandSlotIndex]
            : RightHandItemId == itemId ? _inventory.Slots[PlayerInventory.RightHandSlotIndex]
            : null)?.Charge ?? 0f;

    /// <summary>The single place a target's verbs are filtered to affordable ones and ordered
    /// for cycling/selection — every caller must see the exact same list, since
    /// _selectedVerbIndex indexes into whatever this returns. Creating/using verbs always sort
    /// before deconstruction/scrapping ones (a stable sort — see Verb.IsDestructive).</summary>
    private List<Verb> SelectableVerbs(IVerbTarget? target) =>
        target?.AvailableVerbs.Where(IsAffordable).OrderBy(v => v.IsDestructive).ToList() ?? [];

    private void UpdateVerbHud()
    {
        var target = GetCurrentVerbTarget();

        if (target != _lastTarget)
        {
            _selectedVerbIndex = 0;
            _lastTarget = target;
        }

        UpdateScanHighlight(target);

        // A verb already in progress on this exact target keeps showing/counting down as-is —
        // its Requirements were already deducted, so re-checking affordability here would hide
        // the HUD partway through an already-succeeding action.
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

        _hud!.RenderTargetName(TargetNameText(target));

        if (verb is not null)
        {
            var label = verb.DisplaySuffix is { } suffix ? $"{Tr(verb.LocalizationKey)} ({suffix})" : Tr(verb.LocalizationKey);
            var text = verbs is { Count: > 1 }
                ? $"{label} ({_selectedVerbIndex % verbs.Count + 1}/{verbs.Count})"
                : label;

            _hud.RenderVerb(text, verb.Disabled, target!.CurrentVerbProgress);
        }
        else
        {
            _hud.RenderVerb(null, disabled: false, progress: null);
        }

        UpdateInventoryHud();
    }

    /// <summary>What the crosshair name label should read, or null to hide it. The name identifies
    /// whatever you're looking at independent of whether you can currently afford its verb — e.g.
    /// a damaged conduit should still read as "Damaged Conduit" while not holding spare parts.</summary>
    private string? TargetNameText(IVerbTarget? target)
    {
        if (target?.DisplayNameKey is { } displayNameKey)
        {
            var nameText = target is PickupItem { ItemId: "battery" } batteryPickup
                ? $"{Tr(displayNameKey)} ({Mathf.RoundToInt(batteryPickup.Charge * 100)}%)"
                : Tr(displayNameKey);

            // The exact condition number only while scan mode is active — the PDA's health-scan
            // cartridge is what makes it visible; the ambient "this looks damaged" material cue
            // stays visible to everyone regardless.
            return _scanModeOn && target.Condition is { } condition
                ? $"{nameText} ({Mathf.RoundToInt(condition * 100)}%)"
                : nameText;
        }

        // ShipBuildTarget (floor/ceiling/wall) deliberately has no DisplayNameKey — it's terrain,
        // not a discrete object — so just the scanned percentage.
        if (_scanModeOn && target?.Condition is { } bareCondition)
        {
            return $"{Mathf.RoundToInt(bareCondition * 100)}%";
        }

        return null;
    }

    /// <summary>Keeps the scan-mode highlight camera framing the same view as the real one every
    /// frame, and moves the highlight render layer bit onto whichever target's HighlightVisual is
    /// current — cleared entirely the moment scan mode is off or nothing valid is aimed at. A
    /// target can offer more than one mesh (e.g. an airlock's separate frame + slab) — every one
    /// gets the layer bit together.</summary>
    private void UpdateScanHighlight(IVerbTarget? target)
    {
        _scanHighlightCamera!.GlobalTransform = _camera!.GlobalTransform;
        _scanHighlightCamera.Fov = _camera.Fov;

        var desired = _scanModeOn ? target?.HighlightVisual ?? [] : [];
        if (desired.SequenceEqual(_highlightedVisuals))
        {
            return;
        }

        foreach (var visual in _highlightedVisuals)
        {
            // Guards against a target that was destroyed while still scan-highlighted — its
            // VisualInstance3D children are freed along with it, but this list isn't rebuilt
            // until the next differing `desired`, so a stale reference can reach here.
            if (GodotObject.IsInstanceValid(visual))
            {
                visual.SetLayerMaskValue(ScanHighlightLayer, false);
            }
        }

        foreach (var visual in desired)
        {
            visual.SetLayerMaskValue(ScanHighlightLayer, true);
        }

        _highlightedVisuals = desired;
    }

    /// <summary>Which backpack-type item (if any) the player owns anywhere — worn, merely held,
    /// or stored inside another backpack. Only one of "backpack"/"debug_backpack" is ever
    /// realistically owned at once, so this doesn't handle both existing simultaneously.</summary>
    private string? OwnedBackpackItemId =>
        _inventory.GetPersistentContents("backpack") is not null ? "backpack"
        : _inventory.GetPersistentContents("debug_backpack") is not null ? "debug_backpack"
        : null;

    public PlayerSaveData CapturePlayerState()
    {
        var ownedBackpackItemId = OwnedBackpackItemId;
        var ownedBackpackContents = ownedBackpackItemId is not null ? _inventory.GetPersistentContents(ownedBackpackItemId) : null;
        var ownedTorsoContents = _inventory.GetPersistentContents("eva_torso_suit");
        var ownedPdaContents = _inventory.GetPersistentContents("pda");

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
            OwnedBackpackItemId = ownedBackpackItemId,
            BackpackSlots = ownedBackpackContents is not null
                ? SlotSaveDataConverter.Capture(ownedBackpackContents)
                : new List<SlotSaveData?>(),
            BackpackSlotCount = ownedBackpackContents?.Slots.Count ?? PlayerInventory.BackpackSlotCount,
            HasDrill = _inventory.Drill is not null,
            DrillHasBattery = _inventory.Drill?.HasItem ?? false,
            DrillCharge = _inventory.Drill?.Charge ?? 0f,
            HasFlashlight = _inventory.Flashlight is not null,
            FlashlightHasBattery = _inventory.Flashlight?.HasItem ?? false,
            FlashlightCharge = _inventory.Flashlight?.Charge ?? 0f,
            InventoryWindow = new WindowPosition(_panels!.InventoryWindow!.Position.X, _panels.InventoryWindow.Position.Y),
            DrillWindow = new WindowPosition(_windows!.DrillWindow!.Position.X, _windows.DrillWindow.Position.Y),
            FlashlightWindow = new WindowPosition(_windows.FlashlightWindow!.Position.X, _windows.FlashlightWindow.Position.Y),
            BackpackWindow = new WindowPosition(_windows.BackpackWindow!.Position.X, _windows.BackpackWindow.Position.Y),
            HeadItemId = _inventory.Head?.ItemId,
            TorsoItemId = _inventory.Torso?.ItemId,
            HasEvaSuit = ownedTorsoContents is not null,
            TorsoSlots = ownedTorsoContents is not null
                ? SlotSaveDataConverter.Capture(ownedTorsoContents)
                : new List<SlotSaveData?>(),
            TorsoSlotCount = ownedTorsoContents?.Slots.Count ?? PlayerInventory.TorsoSlotCount,
            HasSuitO2Tank = _inventory.SuitO2?.HasItem ?? false,
            SuitO2Charge = _inventory.SuitO2?.Charge ?? 0f,
            HasSuitN2Tank = _inventory.SuitN2?.HasItem ?? false,
            SuitN2Charge = _inventory.SuitN2?.Charge ?? 0f,
            HasSuitFilter = _inventory.SuitFilter?.HasItem ?? false,
            SuitFilterCharge = _inventory.SuitFilter?.Charge ?? 0f,
            HasSuitBattery = _inventory.SuitBattery?.HasItem ?? false,
            SuitBatteryCharge = _inventory.SuitBattery?.Charge ?? 0f,
            CO2Percent = _suitResources.CO2Percent,
            SuitWindow = new WindowPosition(_windows.SuitWindow!.Position.X, _windows.SuitWindow.Position.Y),
            PdaItemId = _inventory.GetEquippedContainer("pda")?.ItemId,
            HasPda = ownedPdaContents is not null,
            PdaSlots = ownedPdaContents is not null
                ? SlotSaveDataConverter.Capture(ownedPdaContents)
                : new List<SlotSaveData?>(),
            PdaSlotCount = ownedPdaContents?.Slots.Count ?? PlayerInventory.PdaSlotCount,
            PdaWindow = new WindowPosition(_windows.PdaWindow!.Position.X, _windows.PdaWindow.Position.Y),
            ThrusterWindow = new WindowPosition(_panels.ThrusterWindow!.Position.X, _panels.ThrusterWindow.Position.Y),
            ActiveContracts = _activeContracts.Select(c => new ContractSaveData
            {
                InstanceId = c.InstanceId,
                TemplateId = c.TemplateId,
                Type = c.Type,
                ItemId = c.ItemId,
                Count = c.Count,
                TargetDestinationId = c.TargetDestinationId,
                OriginStationId = c.OriginStationId,
                DestinationStationId = c.DestinationStationId,
                Reward = c.Reward,
                FailureFee = c.FailureFee,
                RemainingSeconds = c.RemainingSeconds,
            }).ToList(),
            PendingDebt = _pendingDebt,
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

        _suitResources.RestoreFrom(data.O2Percent, data.HealthPercent, data.CO2Percent);
        _needs.RestoreFrom(data.HungerPercent, data.ThirstPercent, data.EnergyPercent);

        _inventory.Clear();
        if (data.HandSlots.Count > 0)
        {
            SlotSaveDataConverter.Restore(_inventory.Hands, data.HandSlots);
        }
        else
        {
            // Legacy save predating per-slot state — replay the old aggregate dict instead.
            foreach (var (itemId, count) in data.Inventory)
            {
                _inventory.Add(itemId, count);
            }
        }

        // Owned-anywhere persistent contents are restored first (independent of worn state) —
        // an old save predating OwnedBackpackItemId/HasEvaSuit falls back to the worn-only
        // markers. EquipContainerDirectly below reuses whichever entry was seeded here (see its
        // "first acquisition wins" contract) rather than double-restoring.
        var ownedBackpackItemId = data.OwnedBackpackItemId ?? data.BackpackItemId;
        if (ownedBackpackItemId is not null)
        {
            var backpackContents = new SlotContainer(data.BackpackSlotCount);
            if (data.BackpackSlots.Count > 0)
            {
                SlotSaveDataConverter.Restore(backpackContents, data.BackpackSlots);
            }
            else
            {
                // Legacy save predating per-slot state (see PlayerSaveData.BackpackSlots).
                foreach (var (itemId, count) in data.BackpackContents)
                {
                    backpackContents.Add(itemId, count);
                }
            }

            _inventory.RestorePersistentContents(ownedBackpackItemId, backpackContents);
        }

        // Backpack is reconstructed after the body replay above (still null at that point, so
        // those Add calls only ever fill body slots).
        if (data.BackpackItemId is { } backpackItemId)
        {
            _inventory.EquipContainerDirectly("back", backpackItemId, new SlotContainer(data.BackpackSlotCount));
        }

        if (data.HasDrill)
        {
            _inventory.AttachSpecializedSlot("drill_battery", data.DrillHasBattery, data.DrillCharge);
        }

        if (data.HasFlashlight)
        {
            _inventory.AttachSpecializedSlot("flashlight_battery", data.FlashlightHasBattery, data.FlashlightCharge);
        }

        // Same owned-anywhere-first shape as the backpack above — the suit's tank/filter/battery
        // sub-slots are restored unconditionally alongside its pocket contents, since they're
        // per-suit persistent state decoupled from worn state too.
        if (data.HasEvaSuit || data.TorsoItemId is not null)
        {
            var torsoContents = new SlotContainer(PlayerInventory.TorsoSlotCount);
            SlotSaveDataConverter.Restore(torsoContents, data.TorsoSlots);
            _inventory.RestorePersistentContents("eva_torso_suit", torsoContents);

            _inventory.AttachSpecializedSlot("suit_o2", data.HasSuitO2Tank, data.SuitO2Charge);
            _inventory.AttachSpecializedSlot("suit_n2", data.HasSuitN2Tank, data.SuitN2Charge);
            _inventory.AttachSpecializedSlot("suit_filter", data.HasSuitFilter, data.SuitFilterCharge);
            _inventory.AttachSpecializedSlot("suit_battery", data.HasSuitBattery, data.SuitBatteryCharge);
        }

        if (data.TorsoItemId is { } torsoItemId)
        {
            _inventory.EquipContainerDirectly("torso", torsoItemId, new SlotContainer(PlayerInventory.TorsoSlotCount));
        }

        if (data.HeadItemId is { } headItemId)
        {
            _inventory.EquipContainerDirectly("head", headItemId, new SlotContainer(0));
        }

        // Same owned-anywhere-first shape as the backpack/suit above. Math.Max, not the raw saved
        // value: a save from before the power-scan cartridge existed was captured with
        // PdaSlotCount == 1 — grow it to the current slot count on load instead of leaving it
        // stuck at its old, smaller size.
        var pdaSlotCount = System.Math.Max(data.PdaSlotCount, PlayerInventory.PdaSlotCount);

        if (data.HasPda || data.PdaItemId is not null)
        {
            var pdaContents = new SlotContainer(pdaSlotCount);
            SlotSaveDataConverter.Restore(pdaContents, data.PdaSlots);
            _inventory.RestorePersistentContents("pda", pdaContents);
        }

        if (data.PdaItemId is { } pdaItemId)
        {
            _inventory.EquipContainerDirectly("pda", pdaItemId, new SlotContainer(pdaSlotCount));
        }

        // Only applied when present — an old save predating this feature leaves every window at
        // its scene-authored default position.
        if (data.InventoryWindow is { } inventoryWindowPos)
        {
            _panels!.InventoryWindow!.Position = new Vector2(inventoryWindowPos.X, inventoryWindowPos.Y);
        }

        if (data.DrillWindow is { } drillWindowPos)
        {
            _windows!.DrillWindow!.Position = new Vector2(drillWindowPos.X, drillWindowPos.Y);
        }

        if (data.FlashlightWindow is { } flashlightWindowPos)
        {
            _windows!.FlashlightWindow!.Position = new Vector2(flashlightWindowPos.X, flashlightWindowPos.Y);
        }

        if (data.BackpackWindow is { } backpackWindowPos)
        {
            _windows!.BackpackWindow!.Position = new Vector2(backpackWindowPos.X, backpackWindowPos.Y);
        }

        if (data.PdaWindow is { } pdaWindowPos)
        {
            _windows!.PdaWindow!.Position = new Vector2(pdaWindowPos.X, pdaWindowPos.Y);
        }

        if (data.ThrusterWindow is { } thrusterWindowPos)
        {
            _panels!.ThrusterWindow!.Position = new Vector2(thrusterWindowPos.X, thrusterWindowPos.Y);
        }

        if (data.SuitWindow is { } suitWindowPos)
        {
            _windows!.SuitWindow!.Position = new Vector2(suitWindowPos.X, suitWindowPos.Y);
        }

        _credits = data.Credits;

        _activeContracts.Clear();
        _activeContracts.AddRange(data.ActiveContracts.Select(c => new Contract
        {
            InstanceId = c.InstanceId,
            TemplateId = c.TemplateId,
            Type = c.Type,
            ItemId = c.ItemId,
            Count = c.Count,
            TargetDestinationId = c.TargetDestinationId,
            OriginStationId = c.OriginStationId,
            DestinationStationId = c.DestinationStationId,
            Reward = c.Reward,
            FailureFee = c.FailureFee,
            RemainingSeconds = c.RemainingSeconds,
        }));
        _pendingDebt = data.PendingDebt;
    }

    public void RefillSuitResources() => _suitResources.RestoreFrom(100f, 100f);

    /// <summary>Called by SaveManager.Save() at the end of every successful save — F5 and
    /// autosave both funnel through that one method. A brief visible confirmation.</summary>
    public void ShowSavedFlash() => _hud!.ShowSavedFlash();

    /// <summary>Hard death from 0 Health — opens DeathPanel and waits for a Reload/Quit choice
    /// instead of reloading immediately. The call site guards this with a Death-panel-not-
    /// already-open check, so it only fires once per death.</summary>
    private void Die()
    {
        GD.Print("[Player] Died — awaiting reload/quit choice.");
        _panels!.Open(PanelId.Death);
    }

    /// <summary>Reloads the last save so the player picks back up from wherever they last saved.
    /// Falls back to a full O2/Health restore in place if there's no save to reload, e.g. a fresh
    /// game that died before ever pressing F5 or autosave firing once.</summary>
    public void ReloadAfterDeath()
    {
        GD.Print("[Player] Reload chosen — reloading last save.");
        if (SaveManagerRef is not { } saveManager || !saveManager.Load())
        {
            RefillSuitResources();
        }

        _panels!.Close(PanelId.Death);
    }

    /// <summary>DeathPanel's Quit button — no confirmation dialog; death is already the "are you
    /// sure" moment.</summary>
    public void QuitAfterDeath() => GetTree().Quit();

    /// <summary>True while the death screen is up, waiting on a Reload/Quit choice — checked by
    /// SaveManager.Save() so autosave/F5 can't silently overwrite the last good save with this
    /// 0-Health state before the player has chosen (which would make Reload immediately re-trigger
    /// death from the very save it just loaded).</summary>
    public bool IsAwaitingDeathChoice => _panels?.IsOpen(PanelId.Death) ?? false;

    public void RestEnergy() => _needs.Rest(100f);

    private void UpdateInventoryHud()
    {
        // Unlike the backpack/suit/pda contents (reached by itemId, always safe to dereference),
        // PanelController.OpenThruster/OpenStorage are live Node references that Uninstall/Scrap
        // can QueueFree() out from under their window while it's open.
        if (_panels!.IsOpen(PanelId.Thruster) && !GodotObject.IsInstanceValid(_panels.OpenThruster))
        {
            CloseThrusterInventory();
        }

        if (_panels.IsOpen(PanelId.Storage) && !GodotObject.IsInstanceValid(_panels.OpenStorage))
        {
            CloseStorageInventory();
        }

        _windows!.Refresh(
            _inventory,
            _panels.OpenThruster?.Contents,
            GodotObject.IsInstanceValid(_panels.OpenStorage) ? _panels.OpenStorage!.Contents : null);

        // Force scan mode off the moment its gate stops holding (PDA/cartridge/helmet removed
        // mid-scan) — see _scanModeOn's own doc comment.
        if (_scanModeOn && !CanScan)
        {
            _scanModeOn = false;
        }

        // Same "force off the moment its gate stops holding" shape as scan mode above.
        if (_powerInfoOn && !CanShowPowerInfo)
        {
            _powerInfoOn = false;
        }

        _hud!.RenderPower(
            _powerInfoOn,
            Mathf.RoundToInt(ShipSimRef?.DemandedPower() ?? 0f),
            Mathf.RoundToInt(ShipSim.BatteryCapacity));

        _hud.RenderCarried(_credits, LeftHandItemId, RightHandItemId);
    }

}

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
    private Label? _inventoryLabel;
    private Label? _heldItemLabel;
    private Label? _creditsLabel;
    private readonly SuitResources _suitResources = new();
    private readonly PlayerInventory _inventory = new();
    private float _pitch;

    /// <summary>The game's whole known item catalog, doubling as the hotbar slots (keys 1-3) —
    /// also reused by StationConsoleVerbTarget as the set of things Buy can offer, since there's
    /// no separate item-definition data yet.</summary>
    public static readonly string[] HotbarItems = ["scrap_metal", "spare_parts", "wall_panel", "power_cell"];

    /// <summary>A real "what's in my hand" slot, independent of how much of that item the
    /// PlayerInventory actually holds — a verb needing an item is only affordable while this
    /// matches its Requirement's ItemId AND the inventory has enough of it (see IsAffordable).</summary>

    private string? _heldItemId;

    public string? HeldItemId => _heldItemId;

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
        _roomO2Label = GetNode<Label>("HUD/ResourcesPanel/RoomO2Label");
        _inventoryLabel = GetNode<Label>("HUD/InventoryLabel");
        _heldItemLabel = GetNode<Label>("HUD/HeldItemLabel");
        _creditsLabel = GetNode<Label>("HUD/CreditsLabel");

        // Placeholder/tunable starting stipend for testing the free-form conduit wiring
        // extensively — same "don't wait on it" spirit as the near-instant verb durations.
        // Overwritten by ApplyPlayerState on load, same as every other fresh-start default.
        _inventory.Add("scrap_metal", 50);

        CaptureMouse();
        // Setting MouseMode here alone is unreliable if the window doesn't yet have OS
        // input focus at this exact point in startup — reapply whenever focus is (re)gained.
        GetWindow().FocusEntered += CaptureMouse;

        AddToGroup("player");
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured && !IsBusy)
        {
            RotateY(-mouseMotion.Relative.X * MouseSensitivity);

            _pitch = Mathf.Clamp(_pitch - mouseMotion.Relative.Y * MouseSensitivity, -MaxPitchRadians, MaxPitchRadians);
            if (_head is not null)
            {
                _head.Rotation = new Vector3(_pitch, 0, 0);
            }
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && !IsBusy)
        {
            Interact();
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } && !IsBusy)
        {
            CycleSelectedVerb(1);
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } && !IsBusy)
        {
            CycleSelectedVerb(-1);
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } && IsBusy)
        {
            _busyTarget!.CancelVerb();

            foreach (var requirement in _busyVerb!.Requirements)
            {
                _inventory.Add(requirement.ItemId, requirement.Count);
            }

            _busyTarget = null;
            _busyVerb = null;
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }
                 && Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            CaptureMouse();
        }
        else if (@event is InputEventKey { Keycode: Key.Escape, Pressed: true })
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (@event is InputEventKey { Pressed: true } hotbarKey && !IsBusy)
        {
            var index = hotbarKey.Keycode switch
            {
                Key.Key1 => 0,
                Key.Key2 => 1,
                Key.Key3 => 2,
                Key.Key4 => 3,
                _ => -1,
            };

            if (index >= 0 && index < HotbarItems.Length && _inventory.Has(HotbarItems[index], 1))
            {
                _heldItemId = _heldItemId == HotbarItems[index] ? null : HotbarItems[index];
            }
        }
    }

    // Instance method, not static: the FocusEntered connection below is then tied to this
    // Player's lifetime, so Godot auto-disconnects it when this instance is freed (e.g. on a
    // scene change). A static method's connection has no instance to track, so travelling
    // between scenes would try to reconnect the exact same callable and hit "already connected."
    private void CaptureMouse() => Input.MouseMode = Input.MouseModeEnum.Captured;

    public override void _PhysicsProcess(double delta)
    {
        if (IsBusy && _busyTarget!.CurrentVerbProgress is null)
        {
            // The task we started has finished naturally — no refund, unlike an explicit cancel.
            _busyTarget = null;
            _busyVerb = null;
        }

        var velocity = Velocity;

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
            if (Input.IsPhysicalKeyPressed(Key.W)) inputDirection.Y -= 1;
            if (Input.IsPhysicalKeyPressed(Key.S)) inputDirection.Y += 1;
            if (Input.IsPhysicalKeyPressed(Key.A)) inputDirection.X -= 1;
            if (Input.IsPhysicalKeyPressed(Key.D)) inputDirection.X += 1;
            inputDirection = inputDirection.Normalized();

            var moveDirection = (Transform.Basis * new Vector3(inputDirection.X, 0, inputDirection.Y)).Normalized();
            velocity.X = moveDirection.X * MoveSpeed;
            velocity.Z = moveDirection.Z * MoveSpeed;
        }

        Velocity = velocity;
        MoveAndSlide();

        // Suit resources keep draining while busy performing a verb — a task's duration is a
        // real elapsed-time cost, not a pause (docs/project-plan.md's "time acceleration ...
        // pays the full bill" framing). A breached room's dropping O2 burns the suit's own
        // reserve faster on top of the flat drain (see SuitResources.Tick).
        var roomVolume = ShipSimRef?.VolumeAt(new CellCoord(_ambientTile.X, _ambientTile.Y));
        _suitResources.Tick(delta, roomVolume?.O2Fraction ?? 0.21);
        _o2Bar!.Value = _suitResources.O2Percent;
        _powerBar!.Value = _suitResources.PowerPercent;

        if (roomVolume is not null)
        {
            _roomO2Label!.Visible = true;
            _roomO2Label.Text = Tr("HUD_ROOM_O2") + $": {roomVolume.O2Fraction * 100:F0}%";
        }
        else
        {
            _roomO2Label!.Visible = false;
        }

        UpdateVerbHud();
    }

    private IVerbTarget? GetCurrentVerbTarget()
    {
        if (_interactRay is null || !_interactRay.IsColliding())
        {
            return null;
        }

        var collider = _interactRay.GetCollider();

        if (collider is ShipBuildAimForwarder { BuildTarget: { } forwardedTarget })
        {
            forwardedTarget.SetAimPoint(_interactRay.GetCollisionPoint());
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
        var verbs = target?.AvailableVerbs.Where(IsAffordable).ToList();
        if (target is null || verbs is null || verbs.Count == 0)
        {
            return;
        }

        var verb = verbs[_selectedVerbIndex % verbs.Count];
        if (verb.Disabled)
        {
            return;
        }

        foreach (var requirement in verb.Requirements)
        {
            _inventory.TryRemove(requirement.ItemId, requirement.Count);
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
        var count = GetCurrentVerbTarget()?.AvailableVerbs.Count(IsAffordable) ?? 0;
        if (count == 0)
        {
            return;
        }

        _selectedVerbIndex = ((_selectedVerbIndex + direction) % count + count) % count;
    }

    /// <summary>A verb with no item Requirements is always affordable. One that does have
    /// Requirements needs the player to actually be holding that exact item (see _heldItemId)
    /// with enough of it in the inventory — this is the single place every item-gated verb
    /// (repair hull breach, repair damaged conduit, install conduit, ...) is gated, so a target
    /// never needs its own affordability logic.</summary>
    private bool IsAffordable(Verb verb) =>
        verb.Requirements.Count == 0 ||
        verb.Requirements.All(r => r.ItemId == _heldItemId && _inventory.Has(r.ItemId, r.Count));

    private void UpdateVerbHud()
    {
        // Whatever just consumed the last of the held item (installing the final wall panel,
        // selling it, ...) doesn't know about _heldItemId — this is the one place that catches
        // it dropping to zero and clears the selection, instead of the HUD claiming you're still
        // holding something you no longer have any of.
        if (_heldItemId is { } heldItemId && !_inventory.Has(heldItemId, 1))
        {
            _heldItemId = null;
        }

        var target = GetCurrentVerbTarget();

        if (target != _lastTarget)
        {
            _selectedVerbIndex = 0;
            _lastTarget = target;
        }

        // A verb already in progress on this exact target keeps showing/counting down as-is —
        // its Requirements were already deducted to start it, so re-checking affordability here
        // would hide the HUD partway through an already-succeeding action.
        var verbs = target?.AvailableVerbs.Where(IsAffordable).ToList();
        var verb = IsBusy && _busyTarget == target
            ? _busyVerb
            : verbs is { Count: > 0 } ? verbs[_selectedVerbIndex % verbs.Count] : null;

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
            Inventory = new Dictionary<string, int>(_inventory.Counts),
            Credits = _credits,
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

        _inventory.Clear();
        foreach (var (itemId, count) in data.Inventory)
        {
            _inventory.Add(itemId, count);
        }

        _credits = data.Credits;
    }

    public void RefillSuitResources() => _suitResources.RestoreFrom(100f, 100f);

    private void UpdateInventoryHud()
    {
        _creditsLabel!.Text = Tr("HUD_CREDITS") + $": {_credits}";

        _heldItemLabel!.Text = _heldItemId is { } heldItemId
            ? Tr("HUD_HOLDING") + ": " + Tr("ITEM_" + heldItemId.ToUpperInvariant())
            : Tr("HUD_HOLDING_EMPTY");

        if (_inventory.Counts.Count == 0)
        {
            _inventoryLabel!.Visible = false;
            return;
        }

        var lines = new List<string>();
        foreach (var (itemId, count) in _inventory.Counts)
        {
            lines.Add($"{Tr("ITEM_" + itemId.ToUpperInvariant())}: {count}");
        }

        _inventoryLabel!.Text = string.Join("\n", lines);
        _inventoryLabel.Visible = true;
    }
}

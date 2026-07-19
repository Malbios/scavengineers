using Godot;
using PlayerScript = Scavengineers.Scripts.Player.Player;

namespace Scavengineers.Scripts.Travel;

/// <summary>The docking minigame's own real-time simulation and UI — own two independent axes,
/// same "own reference back to Player, Player owns the reference to whichever world target is
/// currently open" shape TravelMapPanel/ThrusterVerbTarget's own windows already use (see
/// Player._openDockingConsole): lateral alignment (WASD, inertia-based, same accelerate/drag
/// shape as Player's own zero-g thrust) and closing distance (Space). Too fast (combined
/// velocity) or too misaligned aborts the attempt — a free retry via a brief message + reset,
/// not a failed trip. The Dock button only does anything once all three tolerances hold at
/// once.</summary>
public partial class DockingMinigamePanel : PanelContainer
{
    [Export]
    public DockingView? View { get; set; }

    [Export]
    public Label? StatusLabel { get; set; }

    [Export]
    public Button? DockButton { get; set; }

    /// <summary>Set by Player._Ready, same self-addressing shape InventorySlotUI.PlayerRef and
    /// every other panel's own PlayerRef already use.</summary>
    public PlayerScript? PlayerRef { get; set; }

    // Placeholder/tunable — everything below is abstract "offset units," not meters, picked to
    // feel flyable at DockingView's own DisplayScale; needs real in-play tuning.
    private const float StartingDistance = 100f;
    private const float StartingMisalignmentMin = 15f;
    private const float StartingMisalignmentMax = 30f;

    private const float LateralThrustAcceleration = 40f;
    private const float LateralDrag = 15f;
    private const float ClosingThrustAcceleration = 25f;
    private const float ClosingDrag = 8f;

    // Abort thresholds — deliberately looser than the Dock* tolerances below, so there's a real
    // "still flying, not docked yet" zone between "safe" and "aborted."
    private const float MaxSafeSpeed = 35f;
    private const float MaxMisalignment = 60f;

    private const float DockAlignmentTolerance = 8f;
    private const float DockDistanceTolerance = 10f;
    private const float DockMaxSafeSpeed = 5f;

    private const float AbortMessageSeconds = 1.5f;

    private Vector2 _lateralOffset;
    private Vector2 _lateralVelocity;
    private float _distanceRemaining;
    private float _closingVelocity;
    private bool _aborted;
    private Timer? _abortResetTimer;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        DockButton!.Text = Tr("VERB_DOCK");
        DockButton.Pressed += OnDockPressed;

        _abortResetTimer = new Timer { OneShot = true, WaitTime = AbortMessageSeconds };
        AddChild(_abortResetTimer);
        _abortResetTimer.Timeout += () => ResetAttempt();
    }

    /// <summary>Called by Player.OpenDockingMinigame — starts (or restarts) the approach from a
    /// fresh, randomized misalignment so a retry doesn't look identical to the previous one.
    /// Every parameter is an explicit-override test seam (null means "use the normal randomized/
    /// default value," matching this codebase's established SavePath/AutosaveIntervalSeconds-
    /// style testability convention) — production callers never pass any of them.</summary>
    public void ResetAttempt(Vector2? startingOffset = null, Vector2? startingVelocity = null, float? startingDistance = null)
    {
        _aborted = false;
        _distanceRemaining = startingDistance ?? StartingDistance;
        _closingVelocity = 0f;

        if (startingOffset is { } offset)
        {
            _lateralOffset = offset;
        }
        else
        {
            var angle = _rng.RandfRange(0f, Mathf.Tau);
            var magnitude = _rng.RandfRange(StartingMisalignmentMin, StartingMisalignmentMax);
            _lateralOffset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * magnitude;
        }

        _lateralVelocity = startingVelocity ?? Vector2.Zero;

        UpdateDisplay();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Visible)
        {
            return;
        }

        var thrust = Vector2.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W))
        {
            thrust.Y -= 1;
        }

        if (Input.IsPhysicalKeyPressed(Key.S))
        {
            thrust.Y += 1;
        }

        if (Input.IsPhysicalKeyPressed(Key.A))
        {
            thrust.X -= 1;
        }

        if (Input.IsPhysicalKeyPressed(Key.D))
        {
            thrust.X += 1;
        }

        Tick((float)delta, thrust, Input.IsPhysicalKeyPressed(Key.Space));
    }

    /// <summary>The minigame's own pure simulation step, factored out of _PhysicsProcess so it
    /// can be driven with synthetic input directly — Input.IsPhysicalKeyPressed reflects real,
    /// continuously-held OS key state that NodeTests has no established way to fake (unlike the
    /// one-shot InputEventKey Player's own tests already simulate), so this is what actually
    /// makes the abort/tolerance logic testable at all.</summary>
    public void Tick(float dt, Vector2 lateralThrust, bool closingThrust)
    {
        if (_aborted)
        {
            return;
        }

        if (lateralThrust != Vector2.Zero)
        {
            _lateralVelocity += lateralThrust.Normalized() * LateralThrustAcceleration * dt;
        }

        _lateralVelocity = _lateralVelocity.MoveToward(Vector2.Zero, LateralDrag * dt);
        _lateralOffset += _lateralVelocity * dt;

        if (closingThrust)
        {
            _closingVelocity += ClosingThrustAcceleration * dt;
        }

        _closingVelocity = Mathf.MoveToward(_closingVelocity, 0f, ClosingDrag * dt);
        _distanceRemaining = Mathf.Max(0f, _distanceRemaining - _closingVelocity * dt);

        if (CombinedSpeed() > MaxSafeSpeed || _lateralOffset.Length() > MaxMisalignment)
        {
            Abort();
            return;
        }

        UpdateDisplay();
    }

    private float CombinedSpeed() => Mathf.Sqrt(_lateralVelocity.LengthSquared() + _closingVelocity * _closingVelocity);

    private bool WithinDockTolerance =>
        _lateralOffset.Length() <= DockAlignmentTolerance
        && _distanceRemaining <= DockDistanceTolerance
        && CombinedSpeed() <= DockMaxSafeSpeed;

    private void Abort()
    {
        _aborted = true;
        StatusLabel!.Text = Tr("HUD_DOCKING_ABORTED");
        DockButton!.Disabled = true;
        View!.WithinTolerance = false;
        _abortResetTimer!.Start();
    }

    private void UpdateDisplay()
    {
        View!.LateralOffset = _lateralOffset;
        var withinTolerance = WithinDockTolerance;
        View.WithinTolerance = withinTolerance;
        DockButton!.Disabled = !withinTolerance;
        StatusLabel!.Text = $"Distance: {_distanceRemaining:F0}   Speed: {CombinedSpeed():F1}   Misalignment: {_lateralOffset.Length():F1}";
    }

    private void OnDockPressed()
    {
        if (DockButton!.Disabled)
        {
            return;
        }

        PlayerRef?.CompleteDocking();
    }
}

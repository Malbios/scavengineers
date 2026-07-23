using Godot;

namespace Scavengineers.Scripts.Player;

/// <summary>
/// Everything the player HUD *draws*, split out of Player.cs. Player computes state and pushes it
/// here as a snapshot once a frame; this class owns every Label/ProgressBar/ColorRect under the
/// HUD, all the <c>Tr()</c> calls, all the number formatting, and the one piece of purely visual
/// animation (the low-health pulse). It reads nothing back and decides nothing — if a method here
/// ever needs to ask Player a question, that logic belongs on Player's side of the line.
///
/// <para>Constructed in code by <see cref="Player._Ready"/> and handed the HUD root, rather than
/// being a node authored into Player.tscn. That's deliberate: it keeps the whole split invisible to
/// the scene file, so there's no new <c>node_paths</c> wiring to get wrong (this project has been
/// bitten by .tscn export resolution before) and no risk of a scene/script mismatch. Resolving its
/// own children from the root it's given is the same thing Player was doing inline a moment
/// earlier.</para>
/// </summary>
public partial class PlayerHudView : Node
{
    // Placeholder/tunable — Health at/below this fraction shows the pulsing warning. Every other
    // player stat (O2, freezing, burning, smoke) already gets a full-screen cue; Health is the
    // one that currently ends the run (via the death screen) with zero advance warning.
    public const float LowHealthThreshold = 25f;

    // Placeholder/tunable — how the pulse's visible alpha oscillates, multiplied against the
    // overlay's own base alpha (see LowHealthOverlay's color in Player.tscn). Sine-driven off
    // Time.GetTicksMsec() rather than a manually ticked field — one less bit of state to keep in
    // sync with delta time.
    private const float LowHealthPulseBaseAlpha = 0.5f;
    private const float LowHealthPulseAmplitude = 0.5f;
    private const float LowHealthPulseSpeed = 2.5f;

    // Placeholder/tunable — how long the "Saved" confirmation stays visible after a save.
    private const float SavedFlashSeconds = 2f;

    private ProgressBar? _o2Bar;
    private Label? _co2Label;
    private ProgressBar? _co2Bar;
    private ProgressBar? _healthBar;
    private ProgressBar? _hungerBar;
    private ProgressBar? _thirstBar;
    private ProgressBar? _energyBar;
    private Label? _roomO2Label;
    private Label? _drillLabel;
    private ProgressBar? _drillBar;
    private Label? _flashlightLabel;
    private ProgressBar? _flashlightBar;

    private ColorRect? _smokeOverlay;
    private ColorRect? _coldOverlay;
    private ColorRect? _burnOverlay;
    private ColorRect? _lowHealthOverlay;

    private Label? _targetNameLabel;
    private Label? _verbLabel;
    private ProgressBar? _verbProgressBar;
    private Label? _powerInfoLabel;
    private Label? _savedLabel;
    private Label? _leftHandLabel;
    private Label? _rightHandLabel;
    private Label? _creditsLabel;

    private Timer? _savedFlashTimer;

    /// <summary>Test-only observability, matching this codebase's narrow test-accessor convention
    /// (see Player.ScanModeOn): lets a NodeTest assert on what the HUD actually rendered without
    /// having to reach through Player into a private field.</summary>
    public ColorRect? LowHealthOverlay => _lowHealthOverlay;

    public ColorRect? SmokeOverlay => _smokeOverlay;

    public Label? TargetNameLabel => _targetNameLabel;

    public Label? VerbLabel => _verbLabel;

    public Label? PowerInfoLabel => _powerInfoLabel;

    /// <summary>Resolves every HUD node from <paramref name="hudRoot"/>. Separate from
    /// <c>_Ready</c> so Player can call it at a controlled point in its own startup rather than
    /// depending on child-vs-parent _Ready ordering — the same explicit-wiring style the rest of
    /// this project's dynamically-built nodes already use.</summary>
    public void Bind(Node hudRoot)
    {
        _o2Bar = hudRoot.GetNode<ProgressBar>("ResourcesPanel/O2Bar");
        _co2Label = hudRoot.GetNode<Label>("ResourcesPanel/CO2Label");
        _co2Bar = hudRoot.GetNode<ProgressBar>("ResourcesPanel/CO2Bar");
        _healthBar = hudRoot.GetNode<ProgressBar>("ResourcesPanel/HealthBar");
        _hungerBar = hudRoot.GetNode<ProgressBar>("ResourcesPanel/HungerBar");
        _thirstBar = hudRoot.GetNode<ProgressBar>("ResourcesPanel/ThirstBar");
        _energyBar = hudRoot.GetNode<ProgressBar>("ResourcesPanel/EnergyBar");
        _roomO2Label = hudRoot.GetNode<Label>("ResourcesPanel/RoomO2Label");
        _drillLabel = hudRoot.GetNode<Label>("ResourcesPanel/DrillLabel");
        _drillBar = hudRoot.GetNode<ProgressBar>("ResourcesPanel/DrillBar");
        _flashlightLabel = hudRoot.GetNode<Label>("ResourcesPanel/FlashlightLabel");
        _flashlightBar = hudRoot.GetNode<ProgressBar>("ResourcesPanel/FlashlightBar");

        _smokeOverlay = hudRoot.GetNode<ColorRect>("SmokeOverlay");
        _coldOverlay = hudRoot.GetNode<ColorRect>("ColdOverlay");
        _burnOverlay = hudRoot.GetNode<ColorRect>("BurnOverlay");
        _lowHealthOverlay = hudRoot.GetNode<ColorRect>("LowHealthOverlay");

        _targetNameLabel = hudRoot.GetNode<Label>("TargetNameLabel");
        _verbLabel = hudRoot.GetNode<Label>("VerbLabel");
        _verbProgressBar = hudRoot.GetNode<ProgressBar>("VerbProgressBar");
        _powerInfoLabel = hudRoot.GetNode<Label>("PowerInfoLabel");
        _savedLabel = hudRoot.GetNode<Label>("SavedLabel");
        _leftHandLabel = hudRoot.GetNode<Label>("LeftHandLabel");
        _rightHandLabel = hudRoot.GetNode<Label>("RightHandLabel");
        _creditsLabel = hudRoot.GetNode<Label>("CreditsLabel");

        _savedFlashTimer = new Timer { OneShot = true, WaitTime = SavedFlashSeconds };
        AddChild(_savedFlashTimer);
        _savedFlashTimer.Timeout += () => _savedLabel!.Visible = false;
    }

    /// <summary>One frame's worth of everything the resource panel and full-screen overlays show.
    /// A snapshot rather than a live reference back to Player/SuitResources, so this class can't
    /// start reaching for state it wasn't handed. Nullable members mean "hide this readout": the
    /// room O2 line when there's no room reading at all, and the drill/flashlight pair when that
    /// tool isn't in hand.</summary>
    public readonly record struct Vitals(
        double O2Percent,
        double CO2Percent,
        bool SuitSealed,
        double HealthPercent,
        bool IsFreezing,
        bool IsBurning,
        bool InSmoke,
        double HungerPercent,
        double ThirstPercent,
        double EnergyPercent,
        double? RoomO2Fraction,
        float? DrillCharge,
        float? FlashlightCharge);

    public void RenderVitals(in Vitals vitals)
    {
        _o2Bar!.Value = vitals.O2Percent;

        // Only meaningful while actually sealed — hidden the rest of the time rather than
        // showing a permanently-0 bar that means nothing without a suit on.
        _co2Label!.Visible = vitals.SuitSealed;
        _co2Bar!.Visible = vitals.SuitSealed;
        _co2Bar.Value = vitals.CO2Percent;

        _healthBar!.Value = vitals.HealthPercent;
        _coldOverlay!.Visible = vitals.IsFreezing;
        _burnOverlay!.Visible = vitals.IsBurning;
        _smokeOverlay!.Visible = vitals.InSmoke;

        var lowHealth = vitals.HealthPercent <= LowHealthThreshold;
        _lowHealthOverlay!.Visible = lowHealth;
        if (lowHealth)
        {
            var pulse = LowHealthPulseBaseAlpha + LowHealthPulseAmplitude * Mathf.Sin(Time.GetTicksMsec() / 1000f * LowHealthPulseSpeed);
            _lowHealthOverlay.Modulate = new Color(1f, 1f, 1f, pulse);
        }

        _hungerBar!.Value = vitals.HungerPercent;
        _thirstBar!.Value = vitals.ThirstPercent;
        _energyBar!.Value = vitals.EnergyPercent;

        if (vitals.RoomO2Fraction is { } roomO2)
        {
            _roomO2Label!.Visible = true;
            _roomO2Label.Text = Tr("HUD_ROOM_O2") + $": {roomO2 * 100:F0}%";
        }
        else
        {
            _roomO2Label!.Visible = false;
        }

        // Only shown while actually holding the tool — feedback on "loses power with usage"
        // matters mid-task, not just when the inventory panel (with the battery slot) is open.
        RenderToolCharge(_drillLabel!, _drillBar!, vitals.DrillCharge);
        RenderToolCharge(_flashlightLabel!, _flashlightBar!, vitals.FlashlightCharge);
    }

    private static void RenderToolCharge(Label label, ProgressBar bar, float? charge)
    {
        label.Visible = charge is not null;
        bar.Visible = charge is not null;
        if (charge is { } value)
        {
            bar.Value = value * 100;
        }
    }

    /// <summary>What's currently being aimed at. Null hides the label — Player decides what the
    /// text is (it owns the scan-mode gate and the per-target name/condition rules); this only
    /// shows or hides it.</summary>
    public void RenderTargetName(string? text)
    {
        _targetNameLabel!.Visible = text is not null;
        if (text is not null)
        {
            _targetNameLabel.Text = text;
        }
    }

    /// <summary>The selected verb and how far through it we are. Null <paramref name="text"/> hides
    /// both the label and the progress bar. <paramref name="disabled"/> renders red — the "shown
    /// but currently impossible" state (see Verb.Disabled).</summary>
    public void RenderVerb(string? text, bool disabled, float? progress)
    {
        _verbLabel!.Visible = text is not null;
        _verbProgressBar!.Visible = text is not null && progress is not null;

        if (text is null)
        {
            return;
        }

        _verbLabel.Text = text;
        _verbLabel.Modulate = disabled ? Colors.Red : Colors.White;

        if (progress is { } value)
        {
            _verbProgressBar.Value = value * 100;
        }
    }

    public void RenderPower(bool visible, int demand, int capacity)
    {
        _powerInfoLabel!.Visible = visible;
        if (visible)
        {
            _powerInfoLabel.Text = Tr("HUD_POWER") + $": {demand} / {capacity}";
        }
    }

    /// <summary>Both hand labels plus the credit counter — the readouts that follow inventory
    /// rather than vitals. A null item id renders as the "empty" copy.</summary>
    public void RenderCarried(int credits, string? leftHandItemId, string? rightHandItemId)
    {
        _creditsLabel!.Text = Tr("HUD_CREDITS") + $": {credits}";
        _leftHandLabel!.Text = Tr("HUD_LEFT_HAND") + ": " + ItemLabel(leftHandItemId);
        _rightHandLabel!.Text = Tr("HUD_RIGHT_HAND") + ": " + ItemLabel(rightHandItemId);
    }

    private string ItemLabel(string? itemId) =>
        itemId is null ? Tr("HUD_HOLDING_EMPTY") : Tr("ITEM_" + itemId.ToUpperInvariant());

    public void ShowSavedFlash()
    {
        _savedLabel!.Text = Tr("HUD_SAVED");
        _savedLabel.Visible = true;
        _savedFlashTimer!.Start();
    }
}

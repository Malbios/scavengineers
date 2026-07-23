using Godot;

namespace Scavengineers.Scripts.Travel;

/// <summary>Draws the docking minigame's visual metaphor — a fixed ring (the docking port) at
/// the view's center and a marker offset from it by <see cref="LateralOffset"/> (the ship's
/// current misalignment), on top of a StarfieldBackground sibling reused as-is for the
/// background.</summary>
public partial class DockingView : Control
{
    // Placeholder/tunable — how many pixels one unit of DockingMinigamePanel's own abstract
    // offset simulation maps to on screen. Kept here (not in the panel) so the panel's own
    // simulation math stays independent of display size.
    [Export]
    public float DisplayScale { get; set; } = 3f;

    [Export]
    public float RingRadius { get; set; } = 40f;

    private static readonly Color RingColorDefault = new(0.8f, 0.8f, 0.85f);
    private static readonly Color RingColorInTolerance = new(0.3f, 1f, 0.4f);
    private static readonly Color MarkerColor = new(1f, 0.6f, 0.2f);

    private Vector2 _lateralOffset;

    [Export]
    public Vector2 LateralOffset
    {
        get => _lateralOffset;
        set
        {
            _lateralOffset = value;
            QueueRedraw();
        }
    }

    private bool _withinTolerance;

    [Export]
    public bool WithinTolerance
    {
        get => _withinTolerance;
        set
        {
            _withinTolerance = value;
            QueueRedraw();
        }
    }

    public override void _Ready() => Resized += QueueRedraw;

    public override void _Draw()
    {
        var center = Size / 2f;
        DrawArc(center, RingRadius, 0f, Mathf.Tau, 48, _withinTolerance ? RingColorInTolerance : RingColorDefault, 2f);
        DrawCircle(center + _lateralOffset * DisplayScale, 6f, MarkerColor);
    }
}

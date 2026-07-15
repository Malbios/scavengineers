using Godot;

namespace Scavengineers.Scripts.Travel;

/// <summary>Procedural starfield drawn behind the travel map's destination icons — a fixed seed
/// keeps the pattern stable across redraws instead of flickering every frame. Pure code, no
/// external image asset, so nothing to log in docs/asset-provenance.md.</summary>
public partial class StarfieldBackground : Control
{
    [Export]
    public int StarCount { get; set; } = 160;

    [Export]
    public int Seed { get; set; } = 12345;

    private static readonly Color BackgroundColor = new(0.02f, 0.03f, 0.07f);

    public override void _Ready()
    {
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), BackgroundColor);

        var rng = new RandomNumberGenerator { Seed = (ulong)Seed };

        for (var i = 0; i < StarCount; i++)
        {
            var position = new Vector2(rng.RandfRange(0, Size.X), rng.RandfRange(0, Size.Y));
            var brightness = rng.RandfRange(0.4f, 1.0f);
            var radius = rng.RandfRange(0.5f, 1.6f);
            DrawCircle(position, radius, new Color(brightness, brightness, brightness, brightness));
        }
    }
}

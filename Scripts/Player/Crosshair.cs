using Godot;

namespace Scavengineers.Scripts.Player;

/// <summary>
/// A simple "+" drawn at this Control's own center — no texture asset needed, matches the
/// greybox/low-poly aesthetic. Sized/anchored to sit at screen center via its scene transform.
/// </summary>
public partial class Crosshair : Control
{
    private const float ArmLength = 6f;
    private const float Thickness = 2f;

    public override void _Draw()
    {
        var center = Size / 2f;
        var color = Colors.White;

        DrawLine(center + new Vector2(-ArmLength, 0), center + new Vector2(ArmLength, 0), color, Thickness);
        DrawLine(center + new Vector2(0, -ArmLength), center + new Vector2(0, ArmLength), color, Thickness);
    }
}

using Godot;

namespace Scavengineers.Scripts.Verbs;

/// <summary>
/// Attaches to a pre-authored wall panel (WallNorth, MidWallA, ...) so aiming directly at an
/// existing wall's face also resolves to that ship's ShipBuildTarget, not just aiming at the
/// Floor next to it. Player-built wall segments don't need this — their collider is already a
/// child of the same body as ShipBuildTarget itself, so Godot's raycast already resolves those
/// hits correctly.
/// </summary>
public partial class ShipBuildAimForwarder : StaticBody3D
{
    [Export]
    public ShipBuildTarget? BuildTarget { get; set; }

    /// <summary>True for a ceiling's forwarder — routes to
    /// <see cref="ShipBuildTarget.SetCeilingAimPoint"/> instead of the default
    /// <see cref="ShipBuildTarget.SetAimPoint"/>, since aiming straight up has no "edge" concept
    /// to resolve, only a tile.</summary>
    [Export]
    public bool IsCeiling { get; set; }
}

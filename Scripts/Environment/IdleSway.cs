using Godot;

namespace Scavengineers.Scripts.Environment;

/// <summary>Gentle vertical bob for a purely-visual node — never attach this to a node carrying
/// collision (a StaticBody3D's collider is meant to stay put; see AnimatableBody3D/RigidBody3D
/// for anything that should actually move physics-wise). Keep the moving part visual-only.</summary>
public partial class IdleSway : Node3D
{
    [Export] public float Amplitude = 0.03f;
    [Export] public float Speed = 1.2f;

    private Vector3 _basePosition;
    private float _time;

    public override void _Ready() => _basePosition = Position;

    public override void _Process(double delta)
    {
        _time += (float)delta;
        Position = _basePosition + new Vector3(0, Mathf.Sin(_time * Speed) * Amplitude, 0);
    }
}

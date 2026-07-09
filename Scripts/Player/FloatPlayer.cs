using Godot;

namespace Scavengineers.Scripts.Player;

/// <summary>
/// Phase 0 Spike 3 — throwaway free-float push-off feel prototype. Deliberately isolated
/// from <see cref="Player"/>/GreyboxRoom (see docs/architecture/locomotion.md): no gravity,
/// no stabilizer, no sustained thrust — tap a direction for a one-off impulse, then drift on
/// pure inertia until you hit something. This is the raw feel being tested, not a finished
/// control scheme.
/// </summary>
public partial class FloatPlayer : CharacterBody3D
{
    private const float PushImpulse = 2.5f;
    private const float MaxSpeed = 6.0f;
    private const float MouseSensitivity = 0.0025f;
    private const float MaxPitchRadians = Mathf.Pi / 2 - 0.05f;

    private Node3D? _head;
    private float _pitch;

    public override void _Ready()
    {
        _head = GetNode<Node3D>("Head");
        CaptureMouse();
        GetWindow().FocusEntered += CaptureMouse;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-mouseMotion.Relative.X * MouseSensitivity);

            _pitch = Mathf.Clamp(_pitch - mouseMotion.Relative.Y * MouseSensitivity, -MaxPitchRadians, MaxPitchRadians);
            if (_head is not null)
            {
                _head.Rotation = new Vector3(_pitch, 0, 0);
            }
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
        else if (@event is InputEventKey { Pressed: true, Echo: false } keyEvent)
        {
            TryPushOff(keyEvent.Keycode);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // No gravity, no damping: velocity only changes from a push-off or a collision slide.
        MoveAndSlide();
    }

    private void TryPushOff(Key key)
    {
        // Space/Ctrl push along world-up/down regardless of where the camera is looking —
        // W/A/S/D push in the body's horizontal facing (yaw only; pitch lives on Head, not
        // the body), so mixing in a pitch-relative "forward" for vertical would be inconsistent.
        Vector3 worldDirection;
        switch (key)
        {
            case Key.W:
                worldDirection = Transform.Basis * new Vector3(0, 0, -1);
                break;
            case Key.S:
                worldDirection = Transform.Basis * new Vector3(0, 0, 1);
                break;
            case Key.A:
                worldDirection = Transform.Basis * new Vector3(-1, 0, 0);
                break;
            case Key.D:
                worldDirection = Transform.Basis * new Vector3(1, 0, 0);
                break;
            case Key.Space:
                worldDirection = Vector3.Up;
                break;
            case Key.Ctrl:
                worldDirection = Vector3.Down;
                break;
            default:
                return;
        }

        worldDirection = worldDirection.Normalized();
        var newVelocity = Velocity + worldDirection * PushImpulse;

        if (newVelocity.Length() > MaxSpeed)
        {
            newVelocity = newVelocity.Normalized() * MaxSpeed;
        }

        Velocity = newVelocity;
    }

    private static void CaptureMouse() => Input.MouseMode = Input.MouseModeEnum.Captured;
}

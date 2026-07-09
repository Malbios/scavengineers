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
    private ShapeCast3D? _touchCast;
    private float _pitch;
    private bool _isTouchingSurface;

    public override void _Ready()
    {
        _head = GetNode<Node3D>("Head");
        _touchCast = GetNode<ShapeCast3D>("TouchCast");
        _touchCast.AddException(this);
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

        // CharacterBody3D's IsOnFloor()/IsOnWall()/IsOnCeiling() are movement-based
        // classifications from MoveAndSlide()'s motion test — with no gravity and zero
        // velocity there's no motion to classify, so they stay false even while physically
        // resting against a surface. An overlap-based ShapeCast3D detects "touching" correctly
        // regardless of velocity.
        if (_touchCast is not null)
        {
            _touchCast.ForceShapecastUpdate();
            _isTouchingSurface = _touchCast.IsColliding();
        }
    }

    private void TryPushOff(Key key)
    {
        // This is a push-*off* (Ostranauts-style: kick off a surface), not sustained thruster
        // EVA — no reaction mass to push against means no impulse, per docs/architecture/
        // locomotion.md's distinction between the two movement modes.
        if (!_isTouchingSurface)
        {
            return;
        }

        if (_head is null)
        {
            return;
        }

        // Full 6DOF, view-relative: in zero-g there's no "up" to anchor to, so every push
        // direction (including Space/Ctrl) is relative to where the camera is actually
        // looking — pitch included, not just the body's yaw.
        var viewBasis = _head.GlobalTransform.Basis;
        Vector3 localDirection;
        switch (key)
        {
            case Key.W:
                localDirection = new Vector3(0, 0, -1);
                break;
            case Key.S:
                localDirection = new Vector3(0, 0, 1);
                break;
            case Key.A:
                localDirection = new Vector3(-1, 0, 0);
                break;
            case Key.D:
                localDirection = new Vector3(1, 0, 0);
                break;
            case Key.Space:
                localDirection = new Vector3(0, 1, 0);
                break;
            case Key.Ctrl:
                localDirection = new Vector3(0, -1, 0);
                break;
            default:
                return;
        }

        var worldDirection = (viewBasis * localDirection).Normalized();
        var newVelocity = Velocity + worldDirection * PushImpulse;

        if (newVelocity.Length() > MaxSpeed)
        {
            newVelocity = newVelocity.Normalized() * MaxSpeed;
        }

        Velocity = newVelocity;
    }

    private static void CaptureMouse() => Input.MouseMode = Input.MouseModeEnum.Captured;
}

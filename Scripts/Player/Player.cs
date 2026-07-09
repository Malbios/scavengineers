using Godot;
using Scavengineers.Scripts.Interaction;

namespace Scavengineers.Scripts.Player;

public partial class Player : CharacterBody3D
{
    private const float MoveSpeed = 4.0f;
    private const float Gravity = 9.8f;
    private const float MouseSensitivity = 0.0025f;
    private const float MaxPitchRadians = Mathf.Pi / 2 - 0.05f;

    private Node3D? _head;
    private RayCast3D? _interactRay;
    private float _pitch;

    public override void _Ready()
    {
        _head = GetNode<Node3D>("Head");
        _interactRay = GetNode<RayCast3D>("Head/Camera3D/InteractRay");
        CaptureMouse();
        // Setting MouseMode here alone is unreliable if the window doesn't yet have OS
        // input focus at this exact point in startup — reapply whenever focus is (re)gained.
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
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            Interact();
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
    }

    private static void CaptureMouse() => Input.MouseMode = Input.MouseModeEnum.Captured;

    public override void _PhysicsProcess(double delta)
    {
        var velocity = Velocity;

        if (!IsOnFloor())
        {
            velocity.Y -= Gravity * (float)delta;
        }

        var inputDirection = Vector2.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W)) inputDirection.Y -= 1;
        if (Input.IsPhysicalKeyPressed(Key.S)) inputDirection.Y += 1;
        if (Input.IsPhysicalKeyPressed(Key.A)) inputDirection.X -= 1;
        if (Input.IsPhysicalKeyPressed(Key.D)) inputDirection.X += 1;
        inputDirection = inputDirection.Normalized();

        var moveDirection = (Transform.Basis * new Vector3(inputDirection.X, 0, inputDirection.Y)).Normalized();
        velocity.X = moveDirection.X * MoveSpeed;
        velocity.Z = moveDirection.Z * MoveSpeed;

        Velocity = velocity;
        MoveAndSlide();
    }

    private void Interact()
    {
        if (_interactRay is null || !_interactRay.IsColliding())
        {
            return;
        }

        if (_interactRay.GetCollider() is IInteractable interactable)
        {
            interactable.Interact();
        }
    }
}

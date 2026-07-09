using Godot;
using Scavengineers.Scripts.Verbs;

namespace Scavengineers.Scripts.Player;

public partial class Player : CharacterBody3D
{
    private const float MoveSpeed = 4.0f;
    private const float Gravity = 9.8f;
    private const float MouseSensitivity = 0.0025f;
    private const float MaxPitchRadians = Mathf.Pi / 2 - 0.05f;

    private Node3D? _head;
    private RayCast3D? _interactRay;
    private Label? _verbLabel;
    private ProgressBar? _verbProgressBar;
    private float _pitch;

    /// <summary>Set while a timed verb we started is still running — occupies the player,
    /// locking movement/look, until <see cref="IVerbTarget.CurrentVerbProgress"/> goes back
    /// to null (reuses the existing progress signal instead of a separate completion event).</summary>
    private IVerbTarget? _busyTarget;

    private bool IsBusy => _busyTarget is not null;

    public override void _Ready()
    {
        _head = GetNode<Node3D>("Head");
        _interactRay = GetNode<RayCast3D>("Head/Camera3D/InteractRay");
        _verbLabel = GetNode<Label>("HUD/VerbLabel");
        _verbProgressBar = GetNode<ProgressBar>("HUD/VerbProgressBar");
        CaptureMouse();
        // Setting MouseMode here alone is unreliable if the window doesn't yet have OS
        // input focus at this exact point in startup — reapply whenever focus is (re)gained.
        GetWindow().FocusEntered += CaptureMouse;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured && !IsBusy)
        {
            RotateY(-mouseMotion.Relative.X * MouseSensitivity);

            _pitch = Mathf.Clamp(_pitch - mouseMotion.Relative.Y * MouseSensitivity, -MaxPitchRadians, MaxPitchRadians);
            if (_head is not null)
            {
                _head.Rotation = new Vector3(_pitch, 0, 0);
            }
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && !IsBusy)
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
        if (IsBusy && _busyTarget!.CurrentVerbProgress is null)
        {
            _busyTarget = null; // the task we started has finished
        }

        var velocity = Velocity;

        if (!IsOnFloor())
        {
            velocity.Y -= Gravity * (float)delta;
        }

        if (IsBusy)
        {
            velocity.X = 0;
            velocity.Z = 0;
        }
        else
        {
            var inputDirection = Vector2.Zero;
            if (Input.IsPhysicalKeyPressed(Key.W)) inputDirection.Y -= 1;
            if (Input.IsPhysicalKeyPressed(Key.S)) inputDirection.Y += 1;
            if (Input.IsPhysicalKeyPressed(Key.A)) inputDirection.X -= 1;
            if (Input.IsPhysicalKeyPressed(Key.D)) inputDirection.X += 1;
            inputDirection = inputDirection.Normalized();

            var moveDirection = (Transform.Basis * new Vector3(inputDirection.X, 0, inputDirection.Y)).Normalized();
            velocity.X = moveDirection.X * MoveSpeed;
            velocity.Z = moveDirection.Z * MoveSpeed;
        }

        Velocity = velocity;
        MoveAndSlide();

        UpdateVerbHud();
    }

    private IVerbTarget? GetCurrentVerbTarget()
    {
        if (_interactRay is null || !_interactRay.IsColliding())
        {
            return null;
        }

        return _interactRay.GetCollider() as IVerbTarget;
    }

    private void Interact()
    {
        if (IsBusy)
        {
            return;
        }

        var target = GetCurrentVerbTarget();
        if (target is null || target.AvailableVerbs.Count == 0)
        {
            return;
        }

        var verb = target.AvailableVerbs[0];
        target.ExecuteVerb(verb);

        if (verb.DurationSeconds > 0)
        {
            _busyTarget = target;
        }
    }

    private void UpdateVerbHud()
    {
        var target = GetCurrentVerbTarget();

        if (target is not null && target.AvailableVerbs.Count > 0)
        {
            _verbLabel!.Text = Tr(target.AvailableVerbs[0].LocalizationKey);
            _verbLabel.Visible = true;

            var progress = target.CurrentVerbProgress;
            _verbProgressBar!.Visible = progress is not null;
            if (progress is not null)
            {
                _verbProgressBar.Value = progress.Value * 100;
            }
        }
        else
        {
            _verbLabel!.Visible = false;
            _verbProgressBar!.Visible = false;
        }
    }
}

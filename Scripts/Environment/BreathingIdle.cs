using Godot;

namespace Scavengineers.Scripts.Environment;

/// <summary>Idle animation for the station's decorative shop figure — subtle torso breathing
/// (Y-scale pulse), independently-phased arm sway, and an occasional random head turn that eases
/// toward a new target angle. Feet/collider never move; only these purely-visual mesh children
/// are touched (see StaticBody3D's "don't move me every frame" guidance on the parent).</summary>
public partial class BreathingIdle : Node3D
{
    [Export] public float BreathSpeed = 0.8f;
    [Export] public float BreathAmplitude = 0.02f;
    [Export] public float ArmSwayDegrees = 4f;
    [Export] public float HeadTurnRangeDegrees = 18f;
    [Export] public float HeadTurnSpeed = 1.5f;
    [Export] public float HeadHoldMinSeconds = 2f;
    [Export] public float HeadHoldMaxSeconds = 5f;

    private MeshInstance3D _torso = null!;
    private MeshInstance3D _armLeft = null!;
    private MeshInstance3D _armRight = null!;
    private MeshInstance3D _head = null!;

    private readonly RandomNumberGenerator _rng = new();
    private float _breathPhase;
    private float _armLeftPhase;
    private float _armRightPhase;
    private float _time;
    private float _headTargetYaw;
    private float _headCurrentYaw;
    private double _headHoldSeconds;

    public override void _Ready()
    {
        _torso = GetNode<MeshInstance3D>("Torso");
        _armLeft = GetNode<MeshInstance3D>("ArmLeft");
        _armRight = GetNode<MeshInstance3D>("ArmRight");
        _head = GetNode<MeshInstance3D>("Head");

        _rng.Randomize();
        _breathPhase = _rng.RandfRange(0f, Mathf.Tau);
        _armLeftPhase = _rng.RandfRange(0f, Mathf.Tau);
        _armRightPhase = _rng.RandfRange(0f, Mathf.Tau);
        PickNextHeadTarget();
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;

        var breath = 1f + Mathf.Sin(_time * BreathSpeed + _breathPhase) * BreathAmplitude;
        _torso.Scale = new Vector3(1f, breath, 1f);

        _armLeft.RotationDegrees = new Vector3(Mathf.Sin(_time * BreathSpeed + _armLeftPhase) * ArmSwayDegrees, 0f, 0f);
        _armRight.RotationDegrees = new Vector3(Mathf.Sin(_time * BreathSpeed + _armRightPhase) * ArmSwayDegrees, 0f, 0f);

        _headHoldSeconds -= delta;
        if (_headHoldSeconds <= 0d)
        {
            PickNextHeadTarget();
        }

        _headCurrentYaw = Mathf.Lerp(_headCurrentYaw, _headTargetYaw, (float)delta * HeadTurnSpeed);
        _head.RotationDegrees = new Vector3(0f, _headCurrentYaw, 0f);
    }

    private void PickNextHeadTarget()
    {
        _headTargetYaw = _rng.RandfRange(-HeadTurnRangeDegrees, HeadTurnRangeDegrees);
        _headHoldSeconds = _rng.RandfRange(HeadHoldMinSeconds, HeadHoldMaxSeconds);
    }
}

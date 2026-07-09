using Godot;

namespace Scavengineers.Scripts.Travel;

/// <summary>
/// Autoload singleton — survives Godot's ChangeSceneToFile (unlike everything else in the
/// old scene tree, which gets freed). Holds a one-shot payload for the destination scene's
/// Player to consume in _Ready and clear.
/// </summary>
public partial class TravelState : Node
{
    public static TravelState? Instance { get; private set; }

    public TravelPayload? Pending { get; set; }

    public override void _Ready() => Instance = this;
}

using System.Collections.Generic;

namespace Scavengineers.Scripts.Travel;

/// <summary>
/// What carries over across a scene-to-scene travel step — deliberately not position/rotation,
/// since arrival position comes from wherever the new scene's Player node is placed, not the
/// old scene. A different concern from Scripts/SaveLoad (explicit, disk-persisted saves).
/// </summary>
public sealed class TravelPayload
{
    public float O2Percent { get; set; }

    public float PowerPercent { get; set; }

    public Dictionary<string, int> Inventory { get; set; } = new();
}

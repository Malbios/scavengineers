namespace Scavengineers.Scripts.SaveLoad;

/// <summary>
/// A second, narrow save contract alongside <see cref="ISaveable"/> — that one stays bool-only
/// on purpose, so a ShipBuildTarget's freely-placed conduit/wall state (a list of tiles/edges,
/// not a single flag) gets its own interface rather than stretching ISaveable to fit it.
/// </summary>
public interface IBuildTargetSaveable
{
    /// <summary>Stable, hand-assigned identifier — same rule as <see cref="ISaveable.SaveId"/>.</summary>
    string SaveId { get; }

    BuildTargetSaveData CaptureBuildState();

    void ApplyBuildState(BuildTargetSaveData state);
}

namespace Scavengineers.Scripts.SaveLoad;

/// <summary>
/// Deliberately narrow — matches what actually needs saving today (docs/architecture/
/// save-schema.md). Both current stateful world objects reduce to a single boolean; this
/// contract generalizes further only when real content needs richer state.
/// </summary>
public interface ISaveable
{
    /// <summary>Stable, hand-assigned identifier — never derived from scene-tree position
    /// or name, per the save-schema doc's "stable, never-reused content IDs" rule.</summary>
    string SaveId { get; }

    bool GetSaveState();

    void ApplySaveState(bool state);
}

namespace Scavengineers.Scripts.SaveLoad;

/// <summary>A third, narrow save contract — for an object with more than 2 real outcomes that a
/// single flag can't distinguish (e.g. a damaged conduit: still damaged, repaired, or scrapped —
/// repaired keeps it live in the power circuit, scrapped removes it entirely). A plain string
/// rather than an int/enum so the save file stays human-readable and a 4th outcome never needs a
/// new contract.</summary>
public interface IStateSaveable
{
    /// <summary>Stable, hand-assigned identifier — same rule as <see cref="ISaveable.SaveId"/>.</summary>
    string SaveId { get; }

    string GetSaveState();

    void ApplySaveState(string state);
}

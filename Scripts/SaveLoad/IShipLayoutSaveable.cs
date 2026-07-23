namespace Scavengineers.Scripts.SaveLoad;

/// <summary>A fourth, narrower save contract — for a procedurally-generated ship's own resolved
/// seed. Unlike every other contract here, the write side (SaveManager.Save) and the read side
/// are NOT symmetric: by the time SaveManager.Load() could call an ApplySaveState-style method
/// back, the ship's Deck has already been built, since Godot only calls _Ready() once and this
/// project's SaveManager never reloads the scene. The seed is instead read directly off disk by
/// ShipSim itself, synchronously, at the top of its own _Ready() — see
/// SaveManager.TryReadShipLayoutSeeds. This interface exists purely for the write side.</summary>
public interface IShipLayoutSaveable
{
    /// <summary>Stable, hand-assigned identifier — same rule as <see cref="ISaveable.SaveId"/>.</summary>
    string SaveId { get; }

    /// <summary>Null for a ship that isn't procedurally generated at all — nothing to persist.</summary>
    int? LayoutSeed { get; }
}

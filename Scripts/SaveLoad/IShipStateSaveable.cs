namespace Scavengineers.Scripts.SaveLoad;

/// <summary>
/// A fifth save contract alongside <see cref="ISaveable"/>/<see cref="IStateSaveable"/>/
/// <see cref="IBuildTargetSaveable"/>/<see cref="IShipLayoutSaveable"/> — for a ship's *live*
/// simulation state (per-cell atmosphere, burning cells), as opposed to the structural layout
/// <see cref="IBuildTargetSaveable"/> already covers.
///
/// <para>Separate rather than folded into IBuildTargetSaveable because the two are owned by
/// different nodes and mean different things: a ShipBuildTarget knows what has been *built*, a
/// ShipSim knows what the air and fire are *doing*. They're also restored at different times —
/// build state replays through the same install/remove calls a player action uses, whereas this is
/// applied as absolute values straight onto the systems.</para>
///
/// <para>Unlike <see cref="IShipLayoutSaveable"/>, this one is symmetric: the Deck already exists
/// by the time a load runs, and overwriting a cell's volume is safe at any point, so it goes
/// through the ordinary group scan in both directions.</para>
/// </summary>
public interface IShipStateSaveable
{
    /// <summary>Stable, hand-assigned identifier — same rule as <see cref="ISaveable.SaveId"/>.
    /// A ship with an empty id is skipped rather than sharing a key with every other unnamed
    /// ship.</summary>
    string SaveId { get; }

    ShipStateSaveData CaptureShipState();

    void ApplyShipState(ShipStateSaveData state);
}

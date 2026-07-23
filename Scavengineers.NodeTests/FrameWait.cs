using System;
using System.Threading.Tasks;

using Godot;

namespace Scavengineers.NodeTests;

/// <summary>
/// Waits for a condition to become true rather than for a fixed number of frames.
///
/// <para>Most of this project's node startup happens in a <c>CallDeferred</c> from <c>_Ready</c>
/// (ShipBuildTarget's panel/layout/zone/loot generation, ShipSim's vacuum seeding, ...), and a
/// deferred call runs at the end of whichever frame's idle processing it was queued in. Awaiting
/// exactly one <c>ProcessFrame</c> and then asserting is therefore a race: it usually wins, and
/// loses under load — which is precisely how ShipBuildTargetLadderGapTest, ShipBuildTargetLootTest
/// and ShipBuildTargetSaveStateTest came to fail intermittently, in a different combination each
/// run, on code that was completely correct.</para>
///
/// <para>Deliberately does not assert or throw on timeout: the caller's own assertions still run
/// and still fail, so a genuinely broken behaviour reports as the specific assertion that broke
/// rather than as an opaque "wait timed out".</para>
/// </summary>
internal static class FrameWait
{
    /// <summary>Generous enough that a slow/loaded run never times out spuriously, short enough
    /// that a genuinely-never-true condition doesn't stall the suite (~0.5s at 60fps).</summary>
    private const int DefaultMaxFrames = 30;

    public static async Task UntilAsync(SceneTree sceneTree, Func<bool> condition, int maxFrames = DefaultMaxFrames)
    {
        for (var frame = 0; frame < maxFrames && !condition(); frame++)
        {
            await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);
        }
    }

    /// <summary>Waits until <paramref name="player"/> has completed at least one full
    /// _PhysicsProcess since now. The obvious-looking <c>await ToSignal(sceneTree, PhysicsFrame)</c>
    /// does NOT do this: that signal fires at the start of the physics frame, before any node's
    /// _PhysicsProcess runs, so a test that awaits it and then asserts on HUD state (written by
    /// Player.UpdateInventoryHud from inside _PhysicsProcess) is racing — it usually wins, and
    /// loses under load.</summary>
    public static Task UntilPlayerProcessedAsync(SceneTree sceneTree, Scavengineers.Scripts.Player.Player player)
    {
        var target = player.PhysicsFramesProcessed + 1;
        return UntilAsync(sceneTree, () => player.PhysicsFramesProcessed >= target);
    }

    /// <summary>For asserting that something *doesn't* happen: there's no condition to wait for, so
    /// this just gives the deferred flush a few frames of room before the caller checks. Distinct
    /// from <see cref="UntilAsync"/> so the "nothing to key on here" cases read as a deliberate
    /// choice rather than a missing condition.</summary>
    public static async Task FramesAsync(SceneTree sceneTree, int frames = 3)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            await sceneTree.ToSignal(sceneTree, SceneTree.SignalName.ProcessFrame);
        }
    }
}

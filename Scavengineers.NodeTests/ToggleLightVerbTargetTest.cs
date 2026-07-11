using GdUnit4;
using Scavengineers.Scripts.Verbs;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>First real example of testing actual node behavior (not just pure logic) — chosen
/// as the simplest verb target: an immediate toggle, no Timer/cycling state, no ShipSimRef/
/// TargetLight wiring required since both are used only through null-conditional calls that
/// safely no-op when unset. Establishes the pattern; later additions can follow this shape for
/// the Timer-driven verb targets (AirlockDoorVerbTarget, InteriorDoorVerbTarget, ...).</summary>
[TestSuite]
public class ToggleLightVerbTargetTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void ExecuteVerb_TogglesSwitchState_AndFlipsBackOnASecondToggle()
    {
        var target = new ToggleLightVerbTarget();
        var toggleVerb = target.AvailableVerbs[0];

        AssertBool(target.GetSaveState()).IsTrue(); // starts on, per ToggleLightVerbTarget's own default

        target.ExecuteVerb(toggleVerb, inventory: null!);
        AssertBool(target.GetSaveState()).IsFalse();

        target.ExecuteVerb(toggleVerb, inventory: null!);
        AssertBool(target.GetSaveState()).IsTrue();
    }
}

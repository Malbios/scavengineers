using GdUnit4;
using Godot;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Proves the Tier 2 pipeline actually works end-to-end: `dotnet test` spinning up a
/// real headless Godot process for a [RequireGodotRuntime] test, not just compiling C#. A real
/// engine API call (Engine.GetVersionInfo) is the point — a plain "true == true" wouldn't tell
/// us the headless runtime ever launched.</summary>
[TestSuite]
public class GodotRuntimeSanityTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void HeadlessGodotRuntime_ActuallyLaunches_AndReportsVersion4()
    {
        var version = Engine.GetVersionInfo();

        AssertThat(version["major"].AsInt32()).IsEqual(4);
    }
}

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Player;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for DraggableWindow's close affordances — an X button and a
/// right-click landing on the window's own body (not a child control) both close it. The
/// "doesn't also swallow a slot's own right-click action" half (InventorySlotUI.AcceptEvent)
/// relies on Godot's own standard Control._GuiInput propagation/AcceptEvent semantics rather than
/// any custom logic here, so it isn't re-verified with a simulated click routed through the real
/// input pipeline — lower value for the risk of a flaky, artificial test setup.</summary>
[TestSuite]
public class DraggableWindowTest
{
    private static DraggableWindow MakeWindow()
    {
        var titleBar = new Control { Name = "TitleBar" };
        var closeButton = new Button { Name = "CloseButton" };
        var window = new DraggableWindow { TitleBar = titleBar, CloseButton = closeButton };
        window.AddChild(titleBar);
        window.AddChild(closeButton);
        return window;
    }

    [TestCase]
    [RequireGodotRuntime]
    public void CloseButtonPressed_FiresCloseRequested()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var window = AutoFree(MakeWindow());
        sceneTree.Root.AddChild(window);

        var fired = false;
        window.CloseRequested += () => fired = true;

        window.GetNode<Button>("CloseButton").EmitSignal(Button.SignalName.Pressed);

        AssertBool(fired).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void RightClickOnTheWindowsOwnBody_FiresCloseRequested()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var window = AutoFree(MakeWindow());
        sceneTree.Root.AddChild(window);

        var fired = false;
        window.CloseRequested += () => fired = true;

        window._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });

        AssertBool(fired).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void LeftClickOnTheWindowsOwnBody_DoesNotFireCloseRequested()
    {
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var window = AutoFree(MakeWindow());
        sceneTree.Root.AddChild(window);

        var fired = false;
        window.CloseRequested += () => fired = true;

        window._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });

        AssertBool(fired).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void MissingCloseButton_DoesNotThrowOnReady()
    {
        // CloseButton is optional (null for any window that hasn't been wired with one) — this
        // must stay a safe, drop-in addition rather than a hard requirement.
        var sceneTree = (SceneTree)Engine.GetMainLoop();
        var titleBar = new Control { Name = "TitleBar" };
        var window = AutoFree(new DraggableWindow { TitleBar = titleBar });
        window.AddChild(titleBar);

        sceneTree.Root.AddChild(window);

        AssertObject(window.CloseButton).IsNull();
    }
}

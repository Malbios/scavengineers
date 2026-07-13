using Godot;

namespace Scavengineers.Scripts.Player;

/// <summary>Lets any HUD window Control be repositioned by dragging a designated title-bar
/// child — shared by the main inventory panel and every per-item window (drill/flashlight
/// battery, backpack contents). Purely a runtime reposition: doesn't touch anchors, so it works
/// regardless of each window's existing (centered) default layout.</summary>
public partial class DraggableWindow : PanelContainer
{
    [Export]
    public Control? TitleBar { get; set; }

    private bool _dragging;
    private Vector2 _dragOffset;

    public override void _Ready() => TitleBar!.GuiInput += OnTitleBarGuiInput;

    private void OnTitleBarGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            _dragging = true;
            _dragOffset = GetGlobalMousePosition() - GlobalPosition;
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            _dragging = false;
        }
        else if (@event is InputEventMouseMotion && _dragging)
        {
            var viewportSize = GetViewport().GetVisibleRect().Size;
            var clamped = GetGlobalMousePosition() - _dragOffset;

            // Keep at least a corner on-screen — a window dragged fully off-screen would
            // otherwise be unrecoverable (no "reset position" affordance exists).
            clamped.X = Mathf.Clamp(clamped.X, -Size.X + 40, viewportSize.X - 40);
            clamped.Y = Mathf.Clamp(clamped.Y, -Size.Y + 40, viewportSize.Y - 40);
            GlobalPosition = clamped;
        }
    }
}

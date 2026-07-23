using System;

using Godot;

namespace Scavengineers.Scripts.Player;

/// <summary>Lets any HUD window Control be repositioned by dragging a designated title-bar child
/// — shared by the main inventory panel and every per-item window. Purely a runtime reposition:
/// doesn't touch anchors, so it works regardless of each window's existing default layout.</summary>
public partial class DraggableWindow : PanelContainer
{
    [Export]
    public Control? TitleBar { get; set; }

    /// <summary>Top-right X button — optional so this stays a drop-in addition rather than
    /// requiring every consumer to update at once.</summary>
    [Export]
    public Button? CloseButton { get; set; }

    /// <summary>Fired by CloseButton and by a right-click landing on this window's own body (not
    /// a child control — see _GuiInput).</summary>
    public event Action? CloseRequested;

    private bool _dragging;
    private Vector2 _dragOffset;

    public override void _Ready()
    {
        TitleBar!.GuiInput += OnTitleBarGuiInput;

        if (CloseButton is not null)
        {
            CloseButton.Pressed += () => CloseRequested?.Invoke();
        }
    }

    /// <summary>Right-click anywhere on this window's own body (not consumed by a child, e.g. a
    /// slot's own right-click action) closes it. Godot's Control _GuiInput bubbles from the
    /// topmost hit control up through its ancestors when an event isn't explicitly accepted, so
    /// this only fires for a click that landed on genuinely empty window space.</summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            CloseRequested?.Invoke();
            AcceptEvent();
        }
    }

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

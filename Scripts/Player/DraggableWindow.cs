using System;

using Godot;

namespace Scavengineers.Scripts.Player;

/// <summary>Lets any HUD window Control be repositioned by dragging a designated title-bar
/// child — shared by the main inventory panel and every per-item window (drill/flashlight
/// battery, backpack contents). Purely a runtime reposition: doesn't touch anchors, so it works
/// regardless of each window's existing (centered) default layout. Also owns this window's own
/// close affordances (X button + right-click-on-background) — see CloseRequested.</summary>
public partial class DraggableWindow : PanelContainer
{
    [Export]
    public Control? TitleBar { get; set; }

    /// <summary>Top-right X button — optional (null for any window that hasn't been wired with
    /// one) so this stays a drop-in addition rather than requiring every consumer to update at
    /// once.</summary>
    [Export]
    public Button? CloseButton { get; set; }

    /// <summary>Fired by CloseButton and by a right-click landing on this window's own body (not
    /// a child control — see _GuiInput) — Player.cs subscribes once per window to whatever
    /// closing that specific window actually entails (some also clear an "open item id" alongside
    /// just hiding, see ToggleItemWindow's own closing branches).</summary>
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
    /// slot's own right-click action — see InventorySlotUI's matching AcceptEvent) closes it, a
    /// quicker alternative to precisely hitting the small X button. Godot's Control _GuiInput
    /// bubbles from the topmost hit control up through its ancestors when an event isn't
    /// explicitly accepted, so this only ever fires for a click that landed on genuinely empty
    /// window space — a slot's own right-click handler accepting the event first stops it from
    /// ever reaching here.</summary>
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

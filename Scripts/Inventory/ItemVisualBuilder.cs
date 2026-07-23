using System.Collections.Generic;

using Godot;

namespace Scavengineers.Scripts.Inventory;

/// <summary>Builds a small multi-primitive visual (and a roughly-matching collision shape) for a
/// pickup's own ItemId, tinted by <see cref="ItemCatalog.Color"/>. Every mesh is one of Godot's
/// own primitives (BoxMesh/CylinderMesh/SphereMesh) — no new asset files.</summary>
public static class ItemVisualBuilder
{
    private static readonly Vector3 FallbackBoxSize = new(0.3f, 0.3f, 0.3f);

    /// <summary>Takes `shapeKind` explicitly rather than looking it up internally, so this stays a
    /// pure function of its inputs — testable without a reachable Data/items.json.</summary>
    public static Node3D BuildVisual(string itemId, string? shapeKind)
    {
        var root = new Node3D();
        var material = ItemCatalog.TintedMaterial(itemId, null);

        foreach (var part in PartsFor(shapeKind))
        {
            part.SetSurfaceOverrideMaterial(0, material);
            root.AddChild(part);
        }

        return root;
    }

    /// <summary>Half this item's vertical collision extent — used to nudge a raycast-resolved
    /// world-drop position above a resting surface so the item doesn't spawn already embedded.
    /// Reuses <see cref="BuildCollisionShape"/>'s own dimensions rather than a second table.</summary>
    public static float RestingHalfHeight(string? shapeKind) => BuildCollisionShape(shapeKind) switch
    {
        BoxShape3D box => box.Size.Y / 2f,
        CylinderShape3D cylinder => cylinder.Height / 2f,
        SphereShape3D sphere => sphere.Radius,
        _ => FallbackBoxSize.Y / 2f,
    };

    /// <summary>A single Shape3D roughly matching this item's silhouette — one shape, not a
    /// physically exact compound collider.</summary>
    public static Shape3D BuildCollisionShape(string? shapeKind) => shapeKind switch
    {
        "tank" => new CylinderShape3D { Radius = 0.08f, Height = 0.43f },
        "cell" => new CylinderShape3D { Radius = 0.12f, Height = 0.15f },
        "canister" => new CylinderShape3D { Radius = 0.15f, Height = 0.12f },
        "bottle" => new CylinderShape3D { Radius = 0.08f, Height = 0.33f },
        "bar" => new BoxShape3D { Size = new Vector3(0.25f, 0.04f, 0.08f) },
        "panel" => new BoxShape3D { Size = new Vector3(0.35f, 0.03f, 0.35f) },
        "debris" => new BoxShape3D { Size = new Vector3(0.24f, 0.13f, 0.2f) },
        "fastener" => new BoxShape3D { Size = new Vector3(0.2f, 0.06f, 0.1f) },
        "bag" => new BoxShape3D { Size = new Vector3(0.28f, 0.42f, 0.16f) },
        "tool_crowbar" => new BoxShape3D { Size = new Vector3(0.08f, 0.1f, 0.44f) },
        "tool_drill" => new BoxShape3D { Size = new Vector3(0.14f, 0.16f, 0.4f) },
        "tool_wrench" => new BoxShape3D { Size = new Vector3(0.12f, 0.05f, 0.34f) },
        "flashlight" => new BoxShape3D { Size = new Vector3(0.09f, 0.09f, 0.3f) },
        "switch" => new BoxShape3D { Size = new Vector3(0.12f, 0.09f, 0.1f) },
        "battery_unit" => new BoxShape3D { Size = new Vector3(0.18f, 0.17f, 0.1f) },
        "recharge_station" => new BoxShape3D { Size = new Vector3(0.16f, 0.45f, 0.14f) },
        "thruster" => new BoxShape3D { Size = new Vector3(0.2f, 0.18f, 0.32f) },
        "tablet" => new BoxShape3D { Size = new Vector3(0.18f, 0.02f, 0.28f) },
        "cartridge" => new BoxShape3D { Size = new Vector3(0.08f, 0.015f, 0.12f) },
        "suit_torso" => new BoxShape3D { Size = new Vector3(0.4f, 0.3f, 0.14f) },
        "helmet" => new SphereShape3D { Radius = 0.15f },
        "crate" => new BoxShape3D { Size = new Vector3(0.3f, 0.25f, 0.3f) },
        _ => new BoxShape3D { Size = FallbackBoxSize },
    };

    private static readonly Rect2 FallbackIconRect = new(0f, 0f, 1f, 1f);

    /// <summary>Normalized (0-1) rects for this item's flat 2D inventory icon — a flattened echo
    /// of <see cref="PartsFor"/>'s 3D composition. Y grows downward in UI space (unlike the 3D
    /// parts' Y-up convention) — "on top of" another part means a SMALLER y here.</summary>
    public static IReadOnlyList<Rect2> IconPartsFor(string? shapeKind) => shapeKind switch
    {
        "tank" =>
        [
            new Rect2(0.3f, 0.3f, 0.4f, 0.65f),
            new Rect2(0.4f, 0.1f, 0.2f, 0.2f),
        ],
        "cell" => [new Rect2(0.15f, 0.35f, 0.7f, 0.3f)],
        "canister" => [new Rect2(0.1f, 0.4f, 0.8f, 0.25f)],
        "bottle" =>
        [
            new Rect2(0.35f, 0.3f, 0.3f, 0.6f),
            new Rect2(0.42f, 0.1f, 0.16f, 0.2f),
        ],
        "bar" => [new Rect2(0.1f, 0.42f, 0.8f, 0.16f)],
        "panel" => [new Rect2(0.05f, 0.35f, 0.9f, 0.3f)],
        "debris" =>
        [
            new Rect2(0.15f, 0.45f, 0.35f, 0.3f),
            new Rect2(0.5f, 0.3f, 0.3f, 0.25f),
            new Rect2(0.35f, 0.55f, 0.28f, 0.25f),
        ],
        "fastener" =>
        [
            new Rect2(0.25f, 0.35f, 0.35f, 0.3f),
            new Rect2(0.6f, 0.42f, 0.18f, 0.16f),
        ],
        "bag" =>
        [
            new Rect2(0.2f, 0.35f, 0.6f, 0.55f),
            new Rect2(0.32f, 0.15f, 0.36f, 0.2f),
        ],
        "tool_crowbar" =>
        [
            new Rect2(0.42f, 0.1f, 0.16f, 0.7f),
            new Rect2(0.3f, 0.75f, 0.4f, 0.15f),
        ],
        "tool_drill" =>
        [
            new Rect2(0.25f, 0.35f, 0.35f, 0.4f),
            new Rect2(0.6f, 0.45f, 0.3f, 0.18f),
        ],
        "tool_wrench" =>
        [
            new Rect2(0.42f, 0.1f, 0.16f, 0.65f),
            new Rect2(0.28f, 0.68f, 0.44f, 0.2f),
        ],
        "flashlight" =>
        [
            new Rect2(0.4f, 0.35f, 0.2f, 0.55f),
            new Rect2(0.3f, 0.1f, 0.4f, 0.25f),
        ],
        "switch" =>
        [
            new Rect2(0.25f, 0.45f, 0.5f, 0.35f),
            new Rect2(0.42f, 0.25f, 0.16f, 0.2f),
        ],
        "battery_unit" =>
        [
            new Rect2(0.2f, 0.35f, 0.6f, 0.45f),
            new Rect2(0.28f, 0.2f, 0.14f, 0.15f),
            new Rect2(0.58f, 0.2f, 0.14f, 0.15f),
        ],
        "recharge_station" =>
        [
            new Rect2(0.3f, 0.35f, 0.4f, 0.55f),
            new Rect2(0.46f, 0.08f, 0.08f, 0.27f),
        ],
        "thruster" =>
        [
            new Rect2(0.25f, 0.5f, 0.5f, 0.3f),
            new Rect2(0.35f, 0.2f, 0.3f, 0.35f),
        ],
        "tablet" => [new Rect2(0.15f, 0.38f, 0.7f, 0.22f)],
        "cartridge" => [new Rect2(0.3f, 0.42f, 0.4f, 0.16f)],
        "suit_torso" =>
        [
            new Rect2(0.32f, 0.3f, 0.36f, 0.55f),
            new Rect2(0.14f, 0.32f, 0.18f, 0.2f),
            new Rect2(0.68f, 0.32f, 0.18f, 0.2f),
        ],
        "helmet" =>
        [
            new Rect2(0.25f, 0.15f, 0.5f, 0.5f),
            new Rect2(0.32f, 0.55f, 0.36f, 0.18f),
        ],
        "crate" =>
        [
            new Rect2(0.15f, 0.35f, 0.7f, 0.5f),
            new Rect2(0.1f, 0.42f, 0.8f, 0.1f),
        ],
        _ => [FallbackIconRect],
    };

    private static List<MeshInstance3D> PartsFor(string? shapeKind) => shapeKind switch
    {
        "tank" =>
        [
            Cylinder(0.08f, 0.08f, 0.35f, new Vector3(0, 0, 0)),
            Cylinder(0.05f, 0.05f, 0.08f, new Vector3(0, 0.215f, 0)),
        ],
        "cell" => [Cylinder(0.12f, 0.12f, 0.15f, Vector3.Zero)],
        "canister" => [Cylinder(0.15f, 0.15f, 0.12f, Vector3.Zero)],
        "bottle" =>
        [
            Cylinder(0.08f, 0.08f, 0.25f, new Vector3(0, 0, 0)),
            Cylinder(0.03f, 0.03f, 0.08f, new Vector3(0, 0.165f, 0)),
        ],
        "bar" => [Box(new Vector3(0.25f, 0.04f, 0.08f), Vector3.Zero)],
        "panel" => [Box(new Vector3(0.35f, 0.03f, 0.35f), Vector3.Zero)],
        "debris" =>
        [
            Box(new Vector3(0.15f, 0.08f, 0.12f), Vector3.Zero, new Vector3(0, 15, 0)),
            Box(new Vector3(0.1f, 0.06f, 0.1f), new Vector3(0.08f, 0.03f, 0.05f), new Vector3(0, -25, 10)),
            Box(new Vector3(0.08f, 0.05f, 0.09f), new Vector3(-0.06f, -0.02f, -0.04f), new Vector3(5, 40, 0)),
        ],
        "fastener" =>
        [
            Box(new Vector3(0.12f, 0.05f, 0.1f), Vector3.Zero),
            Cylinder(0.02f, 0.02f, 0.06f, new Vector3(0.08f, 0.03f, 0), new Vector3(0, 0, 90)),
        ],
        "bag" =>
        [
            Box(new Vector3(0.28f, 0.32f, 0.16f), Vector3.Zero),
            Box(new Vector3(0.22f, 0.1f, 0.05f), new Vector3(0, 0.14f, 0.1f)),
        ],
        "tool_crowbar" =>
        [
            Box(new Vector3(0.05f, 0.05f, 0.4f), Vector3.Zero),
            Box(new Vector3(0.05f, 0.05f, 0.12f), new Vector3(0, 0.04f, 0.2f), new Vector3(30, 0, 0)),
        ],
        "tool_drill" =>
        [
            Box(new Vector3(0.14f, 0.16f, 0.18f), Vector3.Zero),
            Cylinder(0.04f, 0.05f, 0.22f, new Vector3(0, 0.02f, 0.18f), new Vector3(90, 0, 0)),
        ],
        "tool_wrench" =>
        [
            Box(new Vector3(0.04f, 0.04f, 0.3f), Vector3.Zero),
            Box(new Vector3(0.12f, 0.05f, 0.08f), new Vector3(0, 0, 0.17f)),
        ],
        "flashlight" =>
        [
            Cylinder(0.035f, 0.035f, 0.22f, Vector3.Zero, new Vector3(90, 0, 0)),
            Cylinder(0f, 0.045f, 0.08f, new Vector3(0, 0, 0.15f), new Vector3(90, 0, 0)),
        ],
        "switch" =>
        [
            Box(new Vector3(0.12f, 0.04f, 0.1f), Vector3.Zero),
            Cylinder(0.02f, 0.02f, 0.05f, new Vector3(0, 0.045f, 0)),
        ],
        "battery_unit" =>
        [
            Box(new Vector3(0.18f, 0.14f, 0.1f), Vector3.Zero),
            Cylinder(0.015f, 0.015f, 0.03f, new Vector3(0.05f, 0.085f, 0)),
            Cylinder(0.015f, 0.015f, 0.03f, new Vector3(-0.05f, 0.085f, 0)),
        ],
        "recharge_station" =>
        [
            Box(new Vector3(0.16f, 0.3f, 0.14f), Vector3.Zero),
            Cylinder(0.01f, 0.01f, 0.15f, new Vector3(0, 0.225f, 0)),
        ],
        "thruster" =>
        [
            Box(new Vector3(0.16f, 0.16f, 0.1f), Vector3.Zero),
            Cylinder(0.1f, 0.06f, 0.18f, new Vector3(0, 0, 0.14f), new Vector3(90, 0, 0)),
        ],
        "tablet" => [Box(new Vector3(0.18f, 0.02f, 0.28f), Vector3.Zero)],
        "cartridge" => [Box(new Vector3(0.08f, 0.015f, 0.12f), Vector3.Zero)],
        "suit_torso" =>
        [
            Box(new Vector3(0.24f, 0.3f, 0.14f), Vector3.Zero),
            Box(new Vector3(0.08f, 0.08f, 0.14f), new Vector3(0.14f, 0.13f, 0)),
            Box(new Vector3(0.08f, 0.08f, 0.14f), new Vector3(-0.14f, 0.13f, 0)),
        ],
        "helmet" =>
        [
            Sphere(0.14f, Vector3.Zero),
            Box(new Vector3(0.16f, 0.06f, 0.05f), new Vector3(0, -0.02f, 0.12f)),
        ],
        "crate" =>
        [
            Box(new Vector3(0.3f, 0.25f, 0.3f), Vector3.Zero),
            Box(new Vector3(0.32f, 0.04f, 0.32f), new Vector3(0, 0.08f, 0)),
        ],
        _ => [Box(FallbackBoxSize, Vector3.Zero)],
    };

    private static MeshInstance3D Box(Vector3 size, Vector3 position, Vector3? rotationDegrees = null) => new()
    {
        Mesh = new BoxMesh { Size = size },
        Position = position,
        RotationDegrees = rotationDegrees ?? Vector3.Zero,
    };

    private static MeshInstance3D Cylinder(float topRadius, float bottomRadius, float height, Vector3 position, Vector3? rotationDegrees = null) => new()
    {
        Mesh = new CylinderMesh { TopRadius = topRadius, BottomRadius = bottomRadius, Height = height },
        Position = position,
        RotationDegrees = rotationDegrees ?? Vector3.Zero,
    };

    private static MeshInstance3D Sphere(float radius, Vector3 position) => new()
    {
        Mesh = new SphereMesh { Radius = radius, Height = radius * 2f },
        Position = position,
    };
}

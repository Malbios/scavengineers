using System.Linq;

using GdUnit4;
using Godot;
using Scavengineers.Scripts.Inventory;

using static GdUnit4.Assertions;

namespace Scavengineers.NodeTests;

/// <summary>Regression coverage for ItemVisualBuilder — replaces the old "every item is the same
/// shared box" pickup visual with a per-item multi-primitive composition (see Data/items.json's
/// shapeKind field, resolved by the caller — PickupItem/ContainerPickupItem — via
/// ItemCatalog.ShapeKind before reaching here; ItemVisualBuilder itself takes shapeKind directly
/// so it stays testable without a reachable Data/items.json, which this scene-less test project
/// can't load). Not exhaustive over all 23 items/shapeKinds (items.json is the source of truth
/// for that mapping) — just confirms the mechanism: the same shapeKind really produces the same
/// composition, different kinds really differ, and an unknown/null kind falls back safely.</summary>
[TestSuite]
public class ItemVisualBuilderTest
{
    [TestCase]
    [RequireGodotRuntime]
    public void BuildVisual_SameShapeKind_ProducesTheSamePrimitiveComposition()
    {
        var o2 = AutoFree(ItemVisualBuilder.BuildVisual("o2_tank", "tank"));
        var n2 = AutoFree(ItemVisualBuilder.BuildVisual("n2_tank", "tank"));

        var o2Meshes = o2.GetChildren().OfType<MeshInstance3D>().Select(m => m.Mesh.GetType()).ToList();
        var n2Meshes = n2.GetChildren().OfType<MeshInstance3D>().Select(m => m.Mesh.GetType()).ToList();

        AssertBool(o2Meshes.SequenceEqual(n2Meshes)).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BuildVisual_DistinctToolShapeKinds_EachProduceADifferentPrimitiveComposition()
    {
        var crowbar = AutoFree(ItemVisualBuilder.BuildVisual("crowbar", "tool_crowbar"));
        var drill = AutoFree(ItemVisualBuilder.BuildVisual("power_drill", "tool_drill"));
        var wrench = AutoFree(ItemVisualBuilder.BuildVisual("wrench", "tool_wrench"));

        var crowbarParts = crowbar.GetChildren().OfType<MeshInstance3D>().Select(DescribeMesh).ToList();
        var drillParts = drill.GetChildren().OfType<MeshInstance3D>().Select(DescribeMesh).ToList();
        var wrenchParts = wrench.GetChildren().OfType<MeshInstance3D>().Select(DescribeMesh).ToList();

        // Comparing actual mesh dimensions (not just primitive type) — a crowbar and a wrench are
        // both "two boxes" by type alone, so type comparison can't tell them apart; their actual
        // box sizes/positions must differ for these to be genuinely distinct compositions.
        AssertBool(crowbarParts.SequenceEqual(drillParts)).IsFalse();
        AssertBool(crowbarParts.SequenceEqual(wrenchParts)).IsFalse();
        AssertBool(drillParts.SequenceEqual(wrenchParts)).IsFalse();
    }

    private static string DescribeMesh(MeshInstance3D instance) => instance.Mesh switch
    {
        BoxMesh box => $"Box{box.Size}@{instance.Position}",
        CylinderMesh cylinder => $"Cylinder{cylinder.TopRadius}/{cylinder.BottomRadius}/{cylinder.Height}@{instance.Position}",
        SphereMesh sphere => $"Sphere{sphere.Radius}@{instance.Position}",
        _ => instance.Mesh.GetType().Name,
    };

    [TestCase]
    [RequireGodotRuntime]
    public void BuildVisual_UnknownShapeKind_FallsBackToASinglePlainBox()
    {
        var visual = AutoFree(ItemVisualBuilder.BuildVisual("mystery_item", "not_a_real_shape_kind"));

        var meshes = visual.GetChildren().OfType<MeshInstance3D>().ToList();

        AssertInt(meshes.Count).IsEqual(1);
        AssertBool(meshes[0].Mesh is BoxMesh).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BuildVisual_NullShapeKind_FallsBackToASinglePlainBox()
    {
        var visual = AutoFree(ItemVisualBuilder.BuildVisual("mystery_item", null));

        var meshes = visual.GetChildren().OfType<MeshInstance3D>().ToList();

        AssertInt(meshes.Count).IsEqual(1);
        AssertBool(meshes[0].Mesh is BoxMesh).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BuildCollisionShape_UnknownShapeKind_FallsBackToABoxShape()
    {
        var shape = ItemVisualBuilder.BuildCollisionShape("not_a_real_shape_kind");

        AssertBool(shape is BoxShape3D).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BuildCollisionShape_Helmet_UsesASphereShape()
    {
        var shape = ItemVisualBuilder.BuildCollisionShape("helmet");

        AssertBool(shape is SphereShape3D).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void BuildCollisionShape_Tank_UsesACylinderShape()
    {
        var shape = ItemVisualBuilder.BuildCollisionShape("tank");

        AssertBool(shape is CylinderShape3D).IsTrue();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void RestingHalfHeight_MatchesHalfOfTheBoxShapeSActualHeight()
    {
        // suit_torso's own BoxShape3D is Size.Y = 0.3 — this is the exact regression case (a
        // dropped EVA suit torso spawning embedded past the floor's own thin collision and
        // falling straight through) this helper exists to prevent.
        var halfHeight = ItemVisualBuilder.RestingHalfHeight("suit_torso");

        AssertFloat(halfHeight).IsEqual(0.15f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void RestingHalfHeight_ForACylinderShape_IsHalfItsOwnHeight()
    {
        var halfHeight = ItemVisualBuilder.RestingHalfHeight("tank");

        AssertFloat(halfHeight).IsEqual(0.215f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void RestingHalfHeight_ForASphereShape_IsItsOwnRadius()
    {
        var halfHeight = ItemVisualBuilder.RestingHalfHeight("helmet");

        AssertFloat(halfHeight).IsEqual(0.15f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void RestingHalfHeight_UnknownShapeKind_FallsBackToHalfTheFallbackBoxHeight()
    {
        var halfHeight = ItemVisualBuilder.RestingHalfHeight("not_a_real_shape_kind");

        AssertFloat(halfHeight).IsEqual(0.15f);
    }
}

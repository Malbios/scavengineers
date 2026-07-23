using Godot;
using Scavengineers.Sim.Grid;

namespace Scavengineers.Scripts.Ship;

/// <summary>
/// The shared mapping between a ship's tile grid (<see cref="CellCoord"/> / Vector2I, the
/// coordinate system Scavengineers.Sim works in) and ship-local world space, plus the handful of
/// heights that mapping depends on.
///
/// <para>This exists because the conversion was duplicated: <c>ShipBuildTarget</c> wrote
/// <c>tile.X - 3 + 0.5f</c> in two places and <c>ShipAtmosphereZone.TileAt</c> wrote the inverse
/// <c>local.X + 3</c>, floored — the same constant, unnamed, in three places, with nothing
/// connecting them. Getting one of them wrong would put the player's O2 reading on a different tile
/// than the one they're standing on, which is exactly the sort of bug that presents as "the
/// atmosphere sim is broken".</para>
///
/// <para>Pure static functions over plain values: no node references, no scene assumptions. Callers
/// that need *global* space convert the ship-local result through their own ShipRoot, since only
/// they know which ship they mean.</para>
/// </summary>
public static class ShipGeometry
{
    /// <summary>Tile (0,0) sits this many tiles negative of the ship root on both axes, so a
    /// default 6-deep grid straddles the origin rather than extending only positively. Baked into
    /// every ship scene's authored geometry, so it isn't freely tunable — it's recorded here
    /// because the value was previously an unnamed literal 3 in three separate expressions.</summary>
    public const int GridOriginOffset = 3;

    /// <summary>Half a tile — every conversion below addresses a tile's *center*, not its
    /// corner.</summary>
    private const float TileCenter = 0.5f;

    public const float TileSize = 1f;

    /// <summary>Matches WallSegmentShape/WallSegmentMesh's authored Y size (see World.tscn) — the
    /// same hand-kept-in-sync convention <see cref="WallCenterHeight"/> already uses for that
    /// mesh.</summary>
    public const float WallHeight = 2f;

    public const float WallCenterHeight = 1.0f;

    /// <summary>Half the conduit mesh's own 0.05 thickness above the floor's actual top surface
    /// (Y=0, matching <see cref="FloorPanelHeight"/>'s surface alignment) — resting flush on the
    /// floor rather than visibly floating above it.</summary>
    public const float FloorConduitHeight = 0.025f;

    /// <summary>Match the (collision-only) floor/ceiling colliders' actual top/bottom surfaces
    /// exactly, so a panel mesh sits flush with where the player's feet and the ceiling's underside
    /// really are, instead of at the conduit's mount height — sharing that height with conduits is
    /// what used to make panels z-fight with them.</summary>
    public const float FloorPanelHeight = -0.025f;

    public const float CeilingPanelHeight = 2.025f;

    /// <summary>Vertical spacing between a site's stacked decks — a second deck's root sits this
    /// far above the first's, making deck 2's floor plane exactly meet deck 1's ceiling plane
    /// (docs/project-plan.md Appendix A3: "deck N's ceiling plane is the same boundary as deck
    /// N+1's floor"). Derived rather than a magic number in a scene transform, so the two heights
    /// it depends on can never drift out of sync with it.</summary>
    public const float DeckYOffset = CeilingPanelHeight - FloorPanelHeight;

    /// <summary>A wall face gets one conduit mount slot per tile-height's worth of its own height,
    /// stacked vertically — a taller or shorter wall gets more or fewer slots automatically rather
    /// than a hand-picked fixed count.</summary>
    public static readonly int WallSlotCount = Mathf.RoundToInt(WallHeight / TileSize);

    public static readonly float WallSlotHeight = WallHeight / WallSlotCount;

    /// <summary>Center height of wall mount slot <paramref name="slot"/>, counting from the floor
    /// up. Both the count and each slot's height stay correct automatically if
    /// <see cref="WallHeight"/> ever changes.</summary>
    public static float SlotHeight(int slot) => (slot + 0.5f) * WallSlotHeight;

    /// <summary>Ship-local position of a tile's center at the given height.</summary>
    public static Vector3 TileShipLocal(Vector2I tile, float height) =>
        new(tile.X - GridOriginOffset + TileCenter, height, tile.Y - GridOriginOffset + TileCenter);

    /// <summary>Ship-local position of the midpoint of the edge between two cells. Works uniformly
    /// for interior edges and boundary edges (where the far cell is off-grid) — the midpoint math
    /// is the same either way.</summary>
    public static Vector3 EdgeShipLocal(CellCoord a, CellCoord b, float height)
    {
        var midX = (a.X + b.X) / 2f;
        var midY = (a.Y + b.Y) / 2f;
        return new Vector3(midX - GridOriginOffset + TileCenter, height, midY - GridOriginOffset + TileCenter);
    }

    /// <summary>Inverse of <see cref="TileShipLocal"/>: which tile a ship-local position falls in.
    /// Floors rather than rounds, so a position anywhere inside a tile maps to that tile.</summary>
    public static Vector2I TileAtShipLocal(Vector3 shipLocal) =>
        new(
            Mathf.FloorToInt(shipLocal.X + GridOriginOffset),
            Mathf.FloorToInt(shipLocal.Z + GridOriginOffset));

    /// <summary>Rotation for a mesh placed on the edge between two cells. Wall meshes are authored
    /// running along X (separating cells that differ in Y), so an edge separating cells that differ
    /// in X needs a quarter turn.</summary>
    public static Vector3 EdgeRotationDegrees(CellCoord a, CellCoord b) =>
        a.X != b.X ? new Vector3(0, 90, 0) : Vector3.Zero;
}

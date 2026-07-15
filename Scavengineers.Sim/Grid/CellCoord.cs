namespace Scavengineers.Sim.Grid;

public readonly record struct CellCoord(int X, int Y)
{
    /// <summary>The four orthogonally-adjacent cells (no diagonals) — shared by every subsystem
    /// that walks the deck grid (atmosphere diffusion, fire spread), so a future adjacency-rule
    /// change (e.g. diagonals) only has one place to update.</summary>
    public IEnumerable<CellCoord> OrthogonalNeighbors()
    {
        yield return this with { X = X + 1 };
        yield return this with { X = X - 1 };
        yield return this with { Y = Y + 1 };
        yield return this with { Y = Y - 1 };
    }
}

namespace Scavengineers.Sim.Connectivity;

/// <summary>
/// The one shared flood-fill/graph solver behind atmosphere, power, and (later) rooms/data —
/// see docs/architecture/atmosphere-power-sim.md. Subsystems express their own blocking rules
/// (sealed edges, open switches, missing conduit) through <see cref="IConnectivityGraph{TNode}.Neighbors"/>;
/// this solver never encodes subsystem-specific logic.
/// </summary>
public static class ConnectivitySolver
{
    public static IReadOnlyList<IReadOnlySet<TNode>> FindComponents<TNode>(IConnectivityGraph<TNode> graph)
        where TNode : notnull
    {
        var visited = new HashSet<TNode>();
        var components = new List<IReadOnlySet<TNode>>();

        foreach (var start in graph.Nodes)
        {
            if (!visited.Add(start))
            {
                continue;
            }

            var component = new HashSet<TNode> { start };
            var queue = new Queue<TNode>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbor in graph.Neighbors(current))
                {
                    if (visited.Add(neighbor))
                    {
                        component.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            components.Add(component);
        }

        return components;
    }
}

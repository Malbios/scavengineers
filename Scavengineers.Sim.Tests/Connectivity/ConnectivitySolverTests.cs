using Scavengineers.Sim.Connectivity;

namespace Scavengineers.Sim.Tests.Connectivity;

public class ConnectivitySolverTests
{
    private sealed class Graph : IConnectivityGraph<int>
    {
        private readonly Dictionary<int, HashSet<int>> _adjacency = [];

        public IEnumerable<int> Nodes => _adjacency.Keys;

        public IEnumerable<int> Neighbors(int node) => _adjacency.TryGetValue(node, out var n) ? n : [];

        public void AddNode(int node) => _adjacency.TryAdd(node, []);

        public void Connect(int a, int b)
        {
            AddNode(a);
            AddNode(b);
            _adjacency[a].Add(b);
            _adjacency[b].Add(a);
        }
    }

    [Fact]
    public void FullyConnectedGraph_IsOneComponent()
    {
        var graph = new Graph();
        graph.Connect(1, 2);
        graph.Connect(2, 3);

        var components = ConnectivitySolver.FindComponents(graph);

        Assert.Single(components);
        Assert.Equal(new HashSet<int> { 1, 2, 3 }, components[0]);
    }

    [Fact]
    public void SplitGraph_IsTwoComponents()
    {
        var graph = new Graph();
        graph.Connect(1, 2);
        graph.Connect(3, 4);

        var components = ConnectivitySolver.FindComponents(graph);

        Assert.Equal(2, components.Count);
        Assert.Contains(components, c => c.SetEquals(new HashSet<int> { 1, 2 }));
        Assert.Contains(components, c => c.SetEquals(new HashSet<int> { 3, 4 }));
    }

    [Fact]
    public void IsolatedNode_IsItsOwnComponent()
    {
        var graph = new Graph();
        graph.Connect(1, 2);
        graph.AddNode(99);

        var components = ConnectivitySolver.FindComponents(graph);

        Assert.Contains(components, c => c.SetEquals(new HashSet<int> { 99 }));
    }
}

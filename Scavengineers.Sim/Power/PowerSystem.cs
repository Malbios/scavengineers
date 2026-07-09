using Scavengineers.Sim.Connectivity;

namespace Scavengineers.Sim.Power;

/// <summary>
/// Minimal power sim for Phase 0 Spike 2: a graph of conduit/machine nodes connected by
/// conduit links, where a switch is a toggleable link rather than a node. Reuses
/// <see cref="ConnectivitySolver"/> — the same solver <see cref="Scavengineers.Sim.Atmosphere.AtmosphereSystem"/>
/// reuses for its own network.
/// </summary>
public sealed class PowerSystem : IConnectivityGraph<PowerNodeId>
{
    private readonly HashSet<PowerNodeId> _nodes = [];
    private readonly HashSet<(PowerNodeId, PowerNodeId)> _links = [];
    private readonly HashSet<(PowerNodeId, PowerNodeId)> _openSwitches = [];
    private readonly HashSet<PowerNodeId> _sources = [];

    public IEnumerable<PowerNodeId> Nodes => _nodes;

    public IEnumerable<PowerNodeId> Neighbors(PowerNodeId node)
    {
        foreach (var link in _links)
        {
            if (_openSwitches.Contains(link))
            {
                continue;
            }

            if (link.Item1.Equals(node))
            {
                yield return link.Item2;
            }
            else if (link.Item2.Equals(node))
            {
                yield return link.Item1;
            }
        }
    }

    public void Connect(PowerNodeId a, PowerNodeId b)
    {
        _nodes.Add(a);
        _nodes.Add(b);
        _links.Add(Normalize(a, b));
    }

    public void MarkSource(PowerNodeId source)
    {
        _nodes.Add(source);
        _sources.Add(source);
    }

    public void OpenSwitch(PowerNodeId a, PowerNodeId b) => _openSwitches.Add(Normalize(a, b));

    public void CloseSwitch(PowerNodeId a, PowerNodeId b) => _openSwitches.Remove(Normalize(a, b));

    public IReadOnlySet<PowerNodeId> PoweredNodes()
    {
        var powered = new HashSet<PowerNodeId>();

        foreach (var component in ConnectivitySolver.FindComponents(this))
        {
            if (component.Overlaps(_sources))
            {
                powered.UnionWith(component);
            }
        }

        return powered;
    }

    public bool IsPowered(PowerNodeId node) => PoweredNodes().Contains(node);

    private static (PowerNodeId, PowerNodeId) Normalize(PowerNodeId a, PowerNodeId b) =>
        string.CompareOrdinal(a.Value, b.Value) <= 0 ? (a, b) : (b, a);
}

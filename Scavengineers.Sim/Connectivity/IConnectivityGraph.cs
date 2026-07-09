namespace Scavengineers.Sim.Connectivity;

public interface IConnectivityGraph<TNode>
    where TNode : notnull
{
    IEnumerable<TNode> Nodes { get; }

    IEnumerable<TNode> Neighbors(TNode node);
}

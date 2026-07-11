using Scavengineers.Sim.Connectivity;
using Scavengineers.Sim.Grid;
using Scavengineers.Sim.ShipModel;

namespace Scavengineers.Sim.Power;

/// <summary>
/// A pure reader of a <see cref="Deck"/> (the shared structural tier — see
/// docs/architecture/ship-model.md): graph nodes are the deck's conductive fixtures
/// (<see cref="ConduitFixture"/>/<see cref="SwitchFixture"/>/<see cref="MachineFixture"/>),
/// connected by adjacent-cell connectivity (the option the ship-model doc recommends for
/// MVP over explicit port-to-port routing) — a switch on the connecting tile-pair that
/// is open conducts nowhere. Reuses <see cref="ConnectivitySolver"/> — the same solver
/// <see cref="Scavengineers.Sim.Atmosphere.AtmosphereSystem"/> reuses for its own network.
/// </summary>
public sealed class PowerSystem : IConnectivityGraph<PowerNodeId>
{
    private readonly Deck _deck;
    private readonly HashSet<PowerNodeId> _sources = [];

    public PowerSystem(Deck deck) => _deck = deck;

    public IEnumerable<PowerNodeId> Nodes =>
        _deck.Fixtures.Where(IsConductive).Select(f => new PowerNodeId(f.Id));

    public IEnumerable<PowerNodeId> Neighbors(PowerNodeId node)
    {
        var fixture = FindFixture(node);
        if (fixture is null || IsOpenSwitch(fixture))
        {
            yield break;
        }

        foreach (var other in _deck.Fixtures)
        {
            if (other.Id == fixture.Id || !IsConductive(other) || IsOpenSwitch(other))
            {
                continue;
            }

            if (AreConnected(fixture.Tile, other.Tile))
            {
                yield return new PowerNodeId(other.Id);
            }
        }
    }

    public void MarkSource(PowerNodeId source) => _sources.Add(source);

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

    private Fixture? FindFixture(PowerNodeId node) =>
        _deck.Fixtures.FirstOrDefault(f => f.Id == node.Value);

    private static bool IsConductive(Fixture fixture) =>
        fixture is ConduitFixture or SwitchFixture or MachineFixture or BatteryFixture;

    private static bool IsOpenSwitch(Fixture fixture) =>
        fixture is SwitchFixture { IsOpen: true };

    private static bool AreConnected(CellCoord a, CellCoord b) =>
        a == b || (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1);
}

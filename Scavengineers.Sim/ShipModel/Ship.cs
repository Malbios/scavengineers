namespace Scavengineers.Sim.ShipModel;

/// <summary>A ship is a vertical stack of decks. A list from v1 even with one deck populated,
/// matching the "list, never a singleton" rule already applied to fleets.</summary>
public sealed class Ship
{
    private readonly List<Deck> _decks = [];

    public IReadOnlyList<Deck> Decks => _decks;

    public void AddDeck(Deck deck) => _decks.Add(deck);
}

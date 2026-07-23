using System.Text.Json;

using Scavengineers.Scripts.Travel;

namespace Scavengineers.Scripts.Tests.Travel;

/// <summary>Guards the real Data/destinations.json rather than a seeded stand-in. Destination
/// *ordering* in that file is load-bearing — a destination is addressed by its index across the
/// whole list, stations first — and getting it wrong misroutes arrivals and in-flight
/// CargoDelivery contracts rather than failing visibly. Reads the file directly, since
/// DestinationCatalog.Load needs a running Godot engine.</summary>
public class DestinationCatalogTests
{
    private static List<DestinationCatalog.DestinationDefinition> RealDestinations()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Data", "destinations.json");
        var loaded = JsonSerializer.Deserialize<List<DestinationCatalog.DestinationDefinition>>(File.ReadAllText(path));

        Assert.NotNull(loaded);
        Assert.NotEmpty(loaded);
        return loaded;
    }

    [Fact]
    public void EveryStationPrecedesEveryDerelict()
    {
        var destinations = RealDestinations();
        var firstDerelict = destinations.FindIndex(d => !d.IsStation);

        Assert.True(firstDerelict >= 0, "destinations.json has no derelicts at all");
        Assert.DoesNotContain(destinations.Skip(firstDerelict), d => d.IsStation);
    }

    [Fact]
    public void EveryDestinationHasAStableIdAndAName_AndNoIdIsReused()
    {
        var destinations = RealDestinations();

        foreach (var destination in destinations)
        {
            Assert.False(string.IsNullOrWhiteSpace(destination.Id), "a destination is missing its stable id");
            Assert.False(string.IsNullOrWhiteSpace(destination.NameKey), $"'{destination.Id}' has no nameKey to label it on the travel map");
        }

        Assert.Equal(destinations.Count, destinations.Select(d => d.Id).Distinct().Count());
    }

    [Fact]
    public void EveryKindIsOneThisCodeUnderstands()
    {
        // IsStation treats anything that isn't "station" as a derelict, so a typo would silently
        // turn a station into a wreck rather than erroring.
        Assert.All(RealDestinations(), d => Assert.Contains(d.Kind, new[] { "station", "derelict" }));
    }
}

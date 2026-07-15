namespace Scavengineers.Scripts.Tests.Ship;

/// <summary>Forces every test class that seeds the shared static <c>ShipLayoutCatalog</c> onto
/// one non-parallel xUnit collection — same rationale as ItemCatalogCollection.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class ShipLayoutCatalogCollection
{
    public const string Name = "ShipLayoutCatalog";
}

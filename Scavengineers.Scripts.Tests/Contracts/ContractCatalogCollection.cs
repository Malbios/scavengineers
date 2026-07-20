namespace Scavengineers.Scripts.Tests.Contracts;

/// <summary>Forces every test class that seeds the shared static <c>ContractCatalog</c> onto one
/// non-parallel xUnit collection — same reasoning as ItemCatalogCollection: xUnit runs different
/// collections in parallel by default, and the seeded state is process-wide static.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class ContractCatalogCollection
{
    public const string Name = "ContractCatalog";
}

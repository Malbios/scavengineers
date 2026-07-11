namespace Scavengineers.Scripts.Tests.Inventory;

/// <summary>Forces every test class that seeds the shared static <c>ItemCatalog</c> onto one
/// non-parallel xUnit collection — xUnit runs different collections in parallel by default,
/// and ItemCatalog's seeded state is process-wide static, so two classes mutating it at once
/// would otherwise race.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class ItemCatalogCollection
{
    public const string Name = "ItemCatalog";
}

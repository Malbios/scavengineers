using Scavengineers.Scripts.Contracts;

namespace Scavengineers.Scripts.Tests.Contracts;

[Collection(ContractCatalogCollection.Name)]
public class ContractCatalogTests : IDisposable
{
    public ContractCatalogTests()
    {
        ContractCatalog.SeedForTests(new List<ContractCatalog.ContractTemplate>
        {
            new()
            {
                Id = "retrieve_common",
                Type = ContractType.RetrieveItem,
                ItemPool = ["wrench", "crowbar"],
                RewardMin = 30,
                RewardMax = 30,
                DeadlineSeconds = 180f,
                FailureFee = 20,
            },
            new()
            {
                Id = "cargo_delivery",
                Type = ContractType.CargoDelivery,
                RewardMin = 40,
                RewardMax = 80,
                DeadlineSeconds = 240f,
                FailureFee = 30,
            },
            new()
            {
                Id = "salvage_quota",
                Type = ContractType.SalvageQuota,
                ItemPool = ["scrap_metal"],
                CountMin = 10,
                CountMax = 10,
                RewardMin = 20,
                RewardMax = 50,
                DeadlineSeconds = 200f,
                FailureFee = 15,
            },
        });
    }

    public void Dispose() => ContractCatalog.ResetForTests();

    [Fact]
    public void Roll_ReturnsAFreshInstanceId_EvenForTheSameTemplateRolledTwice()
    {
        var first = ContractCatalog.Roll("cargo_delivery", new Random(1));
        var second = ContractCatalog.Roll("cargo_delivery", new Random(1));

        Assert.NotEqual(first.InstanceId, second.InstanceId);
    }

    [Fact]
    public void Roll_CopiesTheTemplatesTypeAndId()
    {
        var contract = ContractCatalog.Roll("retrieve_common", new Random(1));

        Assert.Equal("retrieve_common", contract.TemplateId);
        Assert.Equal(ContractType.RetrieveItem, contract.Type);
    }

    [Fact]
    public void Roll_PicksAnItemFromThePool_WhenTheTemplateHasOne()
    {
        var contract = ContractCatalog.Roll("retrieve_common", new Random(1));

        Assert.Contains(contract.ItemId, new[] { "wrench", "crowbar" });
    }

    [Fact]
    public void Roll_LeavesItemIdNull_WhenTheTemplateHasNoPool()
    {
        var contract = ContractCatalog.Roll("cargo_delivery", new Random(1));

        Assert.Null(contract.ItemId);
    }

    [Fact]
    public void Roll_LeavesEveryDestinationFieldNull_SinceOnlyTheCallerHasLiveConsoleAccess()
    {
        var contract = ContractCatalog.Roll("retrieve_common", new Random(1));

        Assert.Null(contract.TargetDestinationId);
        Assert.Null(contract.OriginStationId);
        Assert.Null(contract.DestinationStationId);
    }

    [Fact]
    public void Roll_RespectsTheCountRange()
    {
        for (var i = 0; i < 20; i++)
        {
            var contract = ContractCatalog.Roll("salvage_quota", new Random(i));
            Assert.Equal(10, contract.Count); // countMin == countMax == 10 in this seed
        }
    }

    [Fact]
    public void Roll_RespectsTheRewardRange()
    {
        for (var i = 0; i < 20; i++)
        {
            var contract = ContractCatalog.Roll("cargo_delivery", new Random(i));
            Assert.InRange(contract.Reward, 40, 80);
        }
    }

    [Fact]
    public void Roll_CopiesTheFailureFeeAndInitialDeadline()
    {
        var contract = ContractCatalog.Roll("salvage_quota", new Random(1));

        Assert.Equal(15, contract.FailureFee);
        Assert.Equal(200f, contract.RemainingSeconds);
    }
}

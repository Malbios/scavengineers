using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Godot;

namespace Scavengineers.Scripts.Contracts;

/// <summary>The game's contract-template data, loaded from Data/contracts.json (CLAUDE.md's
/// data-driven non-negotiable). A template describes a *kind* of job with ranges;
/// <see cref="Roll"/> resolves those ranges into one concrete <see cref="Contract"/> instance.
/// Deliberately uses plain System.Random, not Godot's RandomNumberGenerator (a RefCounted-derived
/// Godot class), to keep this class and its tests fully engine-free.</summary>
public static class ContractCatalog
{
    private const string ResourcePath = "res://Data/contracts.json";

    private static List<ContractTemplate>? _templates;

    private static List<ContractTemplate> Templates => _templates ??= Load();

    internal static IReadOnlyList<ContractTemplate> AllTemplates => Templates;

    /// <summary>Test-only seam bypassing <see cref="Load"/>'s Godot.FileAccess call.</summary>
    internal static void SeedForTests(List<ContractTemplate> templates) => _templates = templates;

    internal static void ResetForTests() => _templates = null;

    /// <summary>Resolves one template's ranges into a concrete, acceptable Contract — a fresh
    /// InstanceId every call, the item pool picked uniformly at random (null if the template has
    /// none, e.g. CargoDelivery/Survey), count/reward each independently rolled within their own
    /// range. Leaves every destination-id field null — the caller (ContractGiverVerbTarget) fills
    /// those in from its own live console access.</summary>
    public static Contract Roll(string templateId, Random rng)
    {
        var template = Templates.First(t => t.Id == templateId);
        var itemId = template.ItemPool.Count > 0 ? template.ItemPool[rng.Next(template.ItemPool.Count)] : null;
        var count = template.CountMin >= template.CountMax ? template.CountMin : rng.Next(template.CountMin, template.CountMax + 1);
        var reward = template.RewardMin >= template.RewardMax ? template.RewardMin : rng.Next(template.RewardMin, template.RewardMax + 1);

        return new Contract
        {
            InstanceId = Guid.NewGuid().ToString(),
            TemplateId = template.Id,
            Type = template.Type,
            ItemId = itemId,
            Count = count,
            Reward = reward,
            FailureFee = template.FailureFee,
            RemainingSeconds = template.DeadlineSeconds,
        };
    }

    private static List<ContractTemplate> Load()
    {
        if (!Godot.FileAccess.FileExists(ResourcePath))
        {
            GD.PushWarning($"[ContractCatalog] {ResourcePath} not found — no contracts will ever be offered.");
            return new List<ContractTemplate>();
        }

        var json = Godot.FileAccess.GetFileAsString(ResourcePath);
        return JsonSerializer.Deserialize<List<ContractTemplate>>(json) ?? [];
    }

    internal sealed class ContractTemplate
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("type")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ContractType Type { get; set; }

        /// <summary>Eligible item ids for RetrieveItem/SalvageQuota — empty for CargoDelivery/
        /// Survey, which don't target one specific catalog item.</summary>
        [JsonPropertyName("itemPool")]
        public List<string> ItemPool { get; set; } = new();

        [JsonPropertyName("countMin")]
        public int CountMin { get; set; } = 1;

        [JsonPropertyName("countMax")]
        public int CountMax { get; set; } = 1;

        [JsonPropertyName("rewardMin")]
        public int RewardMin { get; set; }

        [JsonPropertyName("rewardMax")]
        public int RewardMax { get; set; }

        [JsonPropertyName("deadlineSeconds")]
        public float DeadlineSeconds { get; set; } = 180f;

        [JsonPropertyName("failureFee")]
        public int FailureFee { get; set; }
    }
}

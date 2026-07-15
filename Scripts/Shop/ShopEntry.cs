namespace Scavengineers.Scripts.Shop;

/// <summary>One row in the shop panel — Buy and Sell rows share this shape, built from different
/// price/affordability rules (see StationConsoleVerbTarget.BuildBuyEntries/BuildSellEntries).</summary>
public readonly record struct ShopEntry(string ItemId, string DisplayNameKey, int Price, bool Disabled);

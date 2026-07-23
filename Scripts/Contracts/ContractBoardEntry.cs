namespace Scavengineers.Scripts.Contracts;

/// <summary>Display-ready row for ContractBoardPanel — same role ShopEntry plays for ShopPanel:
/// the panel only ever sees ready-to-show text, not raw Contract/destination-id data it would
/// need console/localization access to interpret itself (see ContractGiverVerbTarget.Describe).
/// ActionAvailable doubles as "has a button at all" and "enabled" — CargoDelivery/Survey rows in
/// the Active list are shown but never actionable, since they complete automatically on arrival.</summary>
public readonly record struct ContractBoardEntry(string InstanceId, string Text, bool ActionAvailable);

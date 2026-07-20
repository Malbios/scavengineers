namespace Scavengineers.Scripts.Contracts;

/// <summary>Display-ready row for ContractBoardPanel — same role ShopEntry plays for ShopPanel:
/// the panel only ever sees ready-to-show text, not raw Contract/destination-id data it would
/// need console/localization access to interpret itself (see
/// ContractGiverVerbTarget.Describe, which builds this). ActionAvailable doubles as "has a
/// button at all" and "enabled" — CargoDelivery/Survey rows in the Active list are shown but
/// never actionable (they complete automatically on arrival), same reuse of Button.Disabled
/// ShopPanel already leans on for "visible but not clickable right now."</summary>
public readonly record struct ContractBoardEntry(string InstanceId, string Text, bool ActionAvailable);

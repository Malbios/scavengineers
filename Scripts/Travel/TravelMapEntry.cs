using Godot;

namespace Scavengineers.Scripts.Travel;

/// <summary>Uniform destination summary for the travel map — built fresh from
/// TravelConsoleVerbTarget's own exported wiring each time the map opens; never itself exported
/// or serialized, so Station and every Derelict reach the UI through one uniform shape.</summary>
public readonly record struct TravelMapEntry(int DestinationId, string DisplayNameKey, Vector2 MapPosition, bool IsCurrent);

namespace Scavengineers.Scripts.Inventory;

/// <summary>Implemented by loose RigidBody3D pickups (PickupItem/ContainerPickupItem) so
/// TravelConsoleVerbTarget.SetShipPresence can freeze/unfreeze them in lockstep with its own
/// CollisionShape3D.Disabled toggle — a live RigidBody3D has nothing to rest on or collide with
/// once its ship's collision is disabled, and would otherwise fall through the now-decollided
/// floor forever (see TravelConsoleVerbTarget's own doc comment on SetShipPresence).</summary>
public interface IPhysicsPresenceAware
{
    void SetPhysicsPresence(bool present);
}

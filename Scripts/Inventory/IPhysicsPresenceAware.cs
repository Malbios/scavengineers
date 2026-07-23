namespace Scavengineers.Scripts.Inventory;

/// <summary>Lets TravelConsoleVerbTarget.SetShipPresence freeze/unfreeze loose RigidBody3D
/// pickups in lockstep with a ship's collision toggle — otherwise a live body falls through the
/// now-decollided floor forever.</summary>
public interface IPhysicsPresenceAware
{
    void SetPhysicsPresence(bool present);
}

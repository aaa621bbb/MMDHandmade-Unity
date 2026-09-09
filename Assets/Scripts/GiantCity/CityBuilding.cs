using UnityEngine;

namespace GiantCity
{
    /// <summary>
    /// Empty marker component placed on city obstacles (buildings, lamps, walls) so the tiny player can
    /// detect collisions/lookups by component instead of relying on custom tags or layers (which require
    /// project-wide definitions that are fragile to add at runtime).
    /// </summary>
    public sealed class CityBuilding : MonoBehaviour
    {
    }
}

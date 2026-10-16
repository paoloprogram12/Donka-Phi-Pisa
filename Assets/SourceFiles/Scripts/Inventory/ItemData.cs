using UnityEngine;

namespace DonkaPhiPisa.Inventory
{
    /// <summary>
    /// Defines one kind of item (Flashlight, Battery, Flare...).
    /// Create via Assets > Create > Donka Phi Pisa > Item.
    /// IMPORTANT: keep these assets inside a "Resources/Items" folder
    /// (e.g. Assets/SourceFiles/Resources/Items) so every client can look them up by id.
    /// </summary>
    [CreateAssetMenu(menuName = "Donka Phi Pisa/Item", fileName = "NewItem")]
    public class ItemData : ScriptableObject
    {
        [Tooltip("Unique id sent over the network. Lowercase, no spaces (e.g. 'battery'). Don't change it after items are placed.")]
        public string itemId;

        [Tooltip("Name shown in the HUD and the pickup prompt.")]
        public string displayName;

        [Tooltip("Optional icon for the hotbar. If empty, the name is shown instead.")]
        public Sprite icon;

        [Tooltip("How many fit in one inventory slot. 1 = not stackable.")]
        [Min(1)] public int maxStack = 1;

        [TextArea] public string description;

        [Tooltip("What gets spawned when a player drops this item (needs WorldItem + NetworkIdentity, " +
                 "and must be in the NetworkManager's 'Registered Spawnable Prefabs' list). Empty = can't be dropped.")]
        public GameObject worldPrefab;

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(itemId)) itemId = name.ToLowerInvariant().Replace(" ", "_");
            if (string.IsNullOrWhiteSpace(displayName)) displayName = name;
        }
    }
}

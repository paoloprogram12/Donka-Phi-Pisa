using System.Collections.Generic;
using UnityEngine;

namespace DonkaPhiPisa.Inventory
{
    /// <summary>
    /// Looks up ItemData by id. The network only sends ids (strings), so every client
    /// turns an id back into its ItemData (name, icon, stack size) here.
    /// Loads every ItemData found in any "Resources/Items" folder.
    /// </summary>
    public static class ItemDatabase
    {
        private static Dictionary<string, ItemData> _items;

        public static ItemData Get(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            if (_items == null) Load();
            _items.TryGetValue(itemId, out ItemData item);
            if (item == null) Debug.LogWarning($"[ItemDatabase] No ItemData with id '{itemId}'. Is it inside a Resources/Items folder?");
            return item;
        }

        private static void Load()
        {
            _items = new Dictionary<string, ItemData>();
            foreach (ItemData item in Resources.LoadAll<ItemData>("Items"))
            {
                if (string.IsNullOrEmpty(item.itemId)) continue;
                if (_items.ContainsKey(item.itemId))
                {
                    Debug.LogWarning($"[ItemDatabase] Duplicate item id '{item.itemId}' ({item.name}). Ids must be unique.");
                    continue;
                }
                _items.Add(item.itemId, item);
            }
        }

#if UNITY_EDITOR
        // Re-scan when entering Play Mode so newly created items are picked up
        // even with "Enter Play Mode Options" (domain reload off).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => _items = null;
#endif
    }
}

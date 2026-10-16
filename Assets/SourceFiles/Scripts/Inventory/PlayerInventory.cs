using System;
using Mirror;
using UnityEngine;

namespace DonkaPhiPisa.Inventory
{
    /// <summary>One hotbar slot. Empty slot = itemId "" and count 0.</summary>
    [Serializable]
    public struct InventorySlot
    {
        public string itemId;
        public int count;

        public bool IsEmpty => string.IsNullOrEmpty(itemId) || count <= 0;
        public ItemData Item => IsEmpty ? null : ItemDatabase.Get(itemId);
    }

    /// <summary>
    /// The player's hotbar inventory. Lives on the "Robot" object next to FirstPersonController.
    /// The SERVER owns the contents (no cheating / no duplicate pickups); clients just read it.
    /// Other scripts (flashlight, flares...) should call the [Server] helpers below.
    /// </summary>
    public class PlayerInventory : NetworkBehaviour
    {
        [Tooltip("Number of hotbar slots.")]
        [Min(1)] public int slotCount = 4;

        [Header("Dropping (G)")]
        [Tooltip("How far in front of the player a dropped item lands, in meters.")]
        public float dropDistance = 0f;

        [Tooltip("Height the drop is aimed from (about chest height).")]
        public float dropHeight = 1.2f;

        /// <summary>Synced from server to all clients. Always slotCount long.</summary>
        public readonly SyncList<InventorySlot> slots = new SyncList<InventorySlot>();

        /// <summary>Which slot is held (0-based). Synced so other players can later see what you hold.</summary>
        [SyncVar] public int selectedSlot;

        // last "Inventory full"-style message, shown by the HUD for a moment (local player only)
        public string Message { get; private set; }
        public float MessageTime { get; private set; } = -100f;

        public InventorySlot SelectedSlotData =>
            selectedSlot >= 0 && selectedSlot < slots.Count ? slots[selectedSlot] : default;

        public ItemData SelectedItem => SelectedSlotData.Item;

        public override void OnStartServer()
        {
            slots.Clear();
            for (int i = 0; i < slotCount; i++)
                slots.Add(new InventorySlot { itemId = "", count = 0 });
            selectedSlot = 0;
        }

        // ---------------- server-side API ----------------

        /// <summary>Adds items, filling existing stacks first. Returns how many did NOT fit (0 = all added).</summary>
        [Server]
        public int TryAdd(ItemData item, int amount)
        {
            if (item == null || amount <= 0) return amount;
            int maxStack = Mathf.Max(1, item.maxStack);

            // 1) top up stacks of the same item
            for (int i = 0; i < slots.Count && amount > 0; i++)
            {
                InventorySlot slot = slots[i];
                if (slot.IsEmpty || slot.itemId != item.itemId || slot.count >= maxStack) continue;

                int add = Mathf.Min(maxStack - slot.count, amount);
                slots[i] = new InventorySlot { itemId = item.itemId, count = slot.count + add };
                amount -= add;
            }

            // 2) use empty slots
            for (int i = 0; i < slots.Count && amount > 0; i++)
            {
                if (!slots[i].IsEmpty) continue;

                int add = Mathf.Min(maxStack, amount);
                slots[i] = new InventorySlot { itemId = item.itemId, count = add };
                amount -= add;
            }

            return amount;
        }

        /// <summary>Total of an item across all slots.</summary>
        public int CountOf(string itemId)
        {
            int total = 0;
            foreach (InventorySlot slot in slots)
                if (!slot.IsEmpty && slot.itemId == itemId) total += slot.count;
            return total;
        }

        public bool Has(string itemId, int amount = 1) => CountOf(itemId) >= amount;

        /// <summary>Removes items (e.g. using a battery). Returns false and removes nothing if there aren't enough.</summary>
        [Server]
        public bool TryRemove(string itemId, int amount = 1)
        {
            if (amount <= 0) return true;
            if (!Has(itemId, amount)) return false;

            for (int i = slots.Count - 1; i >= 0 && amount > 0; i--)
            {
                InventorySlot slot = slots[i];
                if (slot.IsEmpty || slot.itemId != itemId) continue;

                int take = Mathf.Min(slot.count, amount);
                int left = slot.count - take;
                slots[i] = left > 0
                    ? new InventorySlot { itemId = itemId, count = left }
                    : new InventorySlot { itemId = "", count = 0 };
                amount -= take;
            }
            return true;
        }

        /// <summary>Removes items from one specific slot. Returns false if that slot doesn't have enough.</summary>
        [Server]
        public bool TryRemoveFromSlot(int index, int amount = 1)
        {
            if (index < 0 || index >= slots.Count) return false;
            InventorySlot slot = slots[index];
            if (slot.IsEmpty || slot.count < amount) return false;

            int left = slot.count - amount;
            slots[index] = left > 0
                ? new InventorySlot { itemId = slot.itemId, count = left }
                : new InventorySlot { itemId = "", count = 0 };
            return true;
        }

        // ---------------- client -> server ----------------

        [Command]
        public void CmdSelectSlot(int index)
        {
            if (index < 0 || index >= slots.Count) return;
            selectedSlot = index;
        }

        /// <summary>Drops ONE of the held item in front of the player.</summary>
        [Command]
        public void CmdDropSelected()
        {
            // 1) what are we holding? (the server checks its own copy, never trusts the client)
            InventorySlot slot = SelectedSlotData;
            if (slot.IsEmpty) return;

            ItemData item = ItemDatabase.Get(slot.itemId);
            if (item == null) return;
            if (item.worldPrefab == null)
            {
                TargetShowMessage($"Can't drop {item.displayName}");
                return;
            }

            // 2) where should it land?
            Vector3 position = FindDropPosition();
            Quaternion rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

            // 3) take it out of the inventory, then put it in the world
            if (!TryRemoveFromSlot(selectedSlot, 1)) return;

            GameObject dropped = Instantiate(item.worldPrefab, position, rotation);
            if (dropped.TryGetComponent(out WorldItem worldItem))
            {
                worldItem.item = item;
                worldItem.amount = 1;
            }
            SitOnGround(dropped, position.y);

            // 4) tell every client to create their own copy
            NetworkServer.Spawn(dropped);
        }

        // ---------------- server helpers for dropping ----------------

        /// <summary>A spot about 1 m in front of the player, pulled back if a wall is in the way, then down onto the floor.</summary>
        [Server]
        private Vector3 FindDropPosition()
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 chest = transform.position + Vector3.up * dropHeight;
            float distance = dropDistance;

            // don't drop through walls: stop just short of whatever is in front of us
            if (Physics.Raycast(chest, forward, out RaycastHit wallHit, dropDistance, ~0, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(0.2f, wallHit.distance - 0.25f);

            Vector3 inFront = chest + forward * distance;

            // find the floor below that point
            if (Physics.Raycast(inFront, Vector3.down, out RaycastHit groundHit, dropHeight + 5f, ~0, QueryTriggerInteraction.Ignore))
                return groundHit.point;

            return transform.position + forward * distance; // no floor found: drop at foot level
        }

        /// <summary>Moves the object up so its lowest point rests at groundY instead of sinking into the floor.</summary>
        private static void SitOnGround(GameObject go, float groundY)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            go.transform.position += Vector3.up * (groundY - bounds.min.y + 0.01f);
        }

        // ---------------- server -> owning client ----------------

        /// <summary>Shows a short message on this player's screen only.</summary>
        [TargetRpc]
        public void TargetShowMessage(string message)
        {
            Message = message;
            MessageTime = Time.time;
        }
    }
}

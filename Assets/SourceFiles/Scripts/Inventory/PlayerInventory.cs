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

        // ---------------- client -> server ----------------

        [Command]
        public void CmdSelectSlot(int index)
        {
            if (index < 0 || index >= slots.Count) return;
            selectedSlot = index;
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

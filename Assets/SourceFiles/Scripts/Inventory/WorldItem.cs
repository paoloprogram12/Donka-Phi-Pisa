using Mirror;
using UnityEngine;

namespace DonkaPhiPisa.Inventory
{
    /// <summary>
    /// An item lying in the world that a player can pick up with E.
    /// Put it on any object with a Collider. A NetworkIdentity is added automatically.
    /// Items placed in the scene need no extra setup: Mirror spawns scene objects on its own.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class WorldItem : NetworkBehaviour, IInteractable
    {
        [Tooltip("Which item this is (Flashlight, Battery, Flare...).")]
        public ItemData item;

        [Tooltip("How many you get when picking this up (e.g. a pack of 2 batteries).")]
        [SyncVar, Min(1)] public int amount = 1;

        private bool _taken; // server only: stops two players grabbing the same item in the same frame

        public string GetPrompt(PlayerInteractor player)
        {
            if (item == null) return "Pick up ???";
            return amount > 1 ? $"Pick up {item.displayName} x{amount}" : $"Pick up {item.displayName}";
        }

        public bool CanInteract(PlayerInteractor player) => item != null;

        [Server]
        public void ServerInteract(PlayerInteractor player)
        {
            if (_taken || item == null) return;

            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            if (inventory == null) return;

            int leftover = inventory.TryAdd(item, amount);

            if (leftover == amount)
            {
                inventory.TargetShowMessage("Inventory full");
                return;
            }

            if (leftover > 0)
            {
                // picked up part of the stack; the rest stays on the ground
                amount = leftover;
                inventory.TargetShowMessage("Inventory full");
                return;
            }

            _taken = true;
            NetworkServer.Destroy(gameObject);
        }
    }
}

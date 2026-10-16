using Mirror;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DonkaPhiPisa.Inventory
{
    /// <summary>
    /// Local player only: looks where the camera points, shows "[E] ..." for interactables,
    /// sends E presses to the server, and handles hotbar selection (1-4 keys / scroll wheel).
    /// Lives on the "Robot" object next to FirstPersonController and PlayerInventory.
    /// </summary>
    [RequireComponent(typeof(PlayerInventory))]
    public class PlayerInteractor : NetworkBehaviour
    {
        [Tooltip("How far you can reach, in meters, measured from the camera.")]
        public float interactRange = 2.5f;

        [Tooltip("Layers the look ray can hit. Walls on these layers block interaction.")]
        public LayerMask interactMask = ~0;

        [Tooltip("Extra meters of slack the server allows (for lag) before rejecting a pickup.")]
        public float serverRangeTolerance = 1.5f;

        /// <summary>What you're currently looking at (null if nothing). Read by the HUD.</summary>
        public IInteractable CurrentTarget { get; private set; }
        public string CurrentPrompt { get; private set; }

        private NetworkIdentity _currentTargetIdentity;
        private PlayerInventory _inventory;
        private Transform _playerRoot;
        private Camera _camera;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();
            NetworkIdentity rootIdentity = GetComponentInParent<NetworkIdentity>();
            _playerRoot = rootIdentity != null ? rootIdentity.transform : transform.root;
        }

        private void Update()
        {
            if (!isLocalPlayer) return;

            FindTarget();
            HandleInteractKey();
            HandleHotbarKeys();
        }

        // ---------------- looking ----------------

        private void FindTarget()
        {
            CurrentTarget = null;
            CurrentPrompt = null;
            _currentTargetIdentity = null;

            Camera cam = GetCamera();
            if (cam == null) return;

            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            int count = Physics.RaycastNonAlloc(ray, _hits, interactRange, interactMask, QueryTriggerInteraction.Collide);

            // closest hit that isn't part of our own body
            RaycastHit? closest = null;
            for (int i = 0; i < count; i++)
            {
                if (_hits[i].transform.IsChildOf(_playerRoot)) continue;
                if (closest == null || _hits[i].distance < closest.Value.distance) closest = _hits[i];
            }
            if (closest == null) return;

            // whatever we hit first blocks the view; it's only a target if it's interactable
            IInteractable interactable = closest.Value.collider.GetComponentInParent<IInteractable>();
            if (interactable == null || !interactable.CanInteract(this)) return;

            NetworkIdentity identity = ((Component)interactable).GetComponentInParent<NetworkIdentity>();
            if (identity == null) return;

            CurrentTarget = interactable;
            _currentTargetIdentity = identity;
            CurrentPrompt = interactable.GetPrompt(this);
        }

        private Camera GetCamera()
        {
            // our own camera (remote players' cameras are switched off by FirstPersonController)
            if (_camera == null || !_camera.isActiveAndEnabled)
                _camera = _playerRoot.GetComponentInChildren<Camera>();
            if (_camera == null || !_camera.isActiveAndEnabled)
                _camera = Camera.main;
            return _camera;
        }

        // ---------------- input ----------------

        private void HandleInteractKey()
        {
            if (_currentTargetIdentity == null) return;
            if (InteractPressed()) CmdInteract(_currentTargetIdentity);
        }

        private void HandleHotbarKeys()
        {
            int slotCount = _inventory.slots.Count;
            if (slotCount == 0) return;

            for (int i = 0; i < Mathf.Min(slotCount, 9); i++)
            {
                if (NumberKeyPressed(i))
                {
                    _inventory.CmdSelectSlot(i);
                    return;
                }
            }

            float scroll = ScrollDelta();
            if (Mathf.Abs(scroll) > 0.01f)
            {
                int dir = scroll > 0 ? -1 : 1; // scroll up = previous slot
                int next = (_inventory.selectedSlot + dir + slotCount) % slotCount;
                _inventory.CmdSelectSlot(next);
            }
        }

        private static bool InteractPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.E);
#endif
        }

        private static bool NumberKeyPressed(int index)
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current[Key.Digit1 + index].wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Alpha1 + index);
#endif
        }

        private static float ScrollDelta()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
#else
            return Input.mouseScrollDelta.y;
#endif
        }

        // ---------------- server ----------------

        [Command]
        private void CmdInteract(NetworkIdentity target)
        {
            if (target == null) return; // already picked up by someone else

            // don't trust the client: check it's actually within reach
            float maxDistance = interactRange + serverRangeTolerance + 1f; // +1: camera is above the feet
            if (Vector3.Distance(transform.position, target.transform.position) > maxDistance) return;

            IInteractable interactable = target.GetComponentInChildren<IInteractable>();
            interactable?.ServerInteract(this);
        }
    }
}

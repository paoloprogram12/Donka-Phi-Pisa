using UnityEngine;

namespace DonkaPhiPisa.Inventory
{
    /// <summary>
    /// Simple placeholder HUD for the local player: crosshair dot, "[E] Pick up ..." prompt,
    /// hotbar at the bottom, and short messages like "Inventory full".
    /// Uses OnGUI so it needs no Canvas setup; swap for a proper UI Canvas later.
    /// </summary>
    [RequireComponent(typeof(PlayerInventory), typeof(PlayerInteractor))]
    public class InventoryHUD : MonoBehaviour
    {
        public float slotSize = 64f;
        public float slotSpacing = 8f;
        public float messageDuration = 2f;

        private PlayerInventory _inventory;
        private PlayerInteractor _interactor;
        private GUIStyle _center, _small, _count;
        private Texture2D _white;

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();
            _interactor = GetComponent<PlayerInteractor>();
        }

        private void OnGUI()
        {
            if (_interactor == null || !_interactor.isLocalPlayer) return;
            EnsureStyles();

            DrawCrosshair();
            DrawPrompt();
            DrawHotbar();
            DrawMessage();
        }

        private void DrawCrosshair()
        {
            Color c = _interactor.CurrentTarget != null ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            float s = _interactor.CurrentTarget != null ? 6f : 4f;
            DrawRect(new Rect(Screen.width / 2f - s / 2f, Screen.height / 2f - s / 2f, s, s), c);
        }

        private void DrawPrompt()
        {
            if (string.IsNullOrEmpty(_interactor.CurrentPrompt)) return;
            Rect r = new Rect(0, Screen.height / 2f + 24f, Screen.width, 30f);
            GUI.Label(r, $"[E]  {_interactor.CurrentPrompt}", _center);
        }

        private void DrawHotbar()
        {
            int n = _inventory.slots.Count;
            if (n == 0) return;

            float total = n * slotSize + (n - 1) * slotSpacing;
            float x0 = (Screen.width - total) / 2f;
            float y = Screen.height - slotSize - 20f;

            for (int i = 0; i < n; i++)
            {
                Rect r = new Rect(x0 + i * (slotSize + slotSpacing), y, slotSize, slotSize);
                bool selected = i == _inventory.selectedSlot;

                DrawRect(r, new Color(0f, 0f, 0f, 0.55f));
                DrawBorder(r, selected ? Color.white : new Color(1f, 1f, 1f, 0.25f), selected ? 3f : 1f);

                // slot number
                GUI.Label(new Rect(r.x + 4, r.y + 2, 20, 16), (i + 1).ToString(), _small);

                InventorySlot slot = _inventory.slots[i];
                if (slot.IsEmpty) continue;

                ItemData item = slot.Item;
                if (item != null && item.icon != null)
                    GUI.DrawTexture(new Rect(r.x + 8, r.y + 8, r.width - 16, r.height - 16), item.icon.texture, ScaleMode.ScaleToFit);
                else
                    GUI.Label(r, item != null ? item.displayName : slot.itemId, _center);

                if (slot.count > 1)
                    GUI.Label(new Rect(r.x, r.yMax - 20, r.width - 6, 18), $"x{slot.count}", _count);
            }

            // name of the held item above the hotbar
            ItemData held = _inventory.SelectedItem;
            if (held != null)
                GUI.Label(new Rect(0, y - 28f, Screen.width, 24f), held.displayName, _center);
        }

        private void DrawMessage()
        {
            if (string.IsNullOrEmpty(_inventory.Message)) return;
            if (Time.time - _inventory.MessageTime > messageDuration) return;
            GUI.Label(new Rect(0, Screen.height / 2f + 56f, Screen.width, 30f), _inventory.Message, _center);
        }

        // ---------------- drawing helpers ----------------

        private void EnsureStyles()
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            if (_center != null) return;

            _center = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, wordWrap = true };
            _center.normal.textColor = Color.white;
            _small = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            _small.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            _count = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontSize = 13, fontStyle = FontStyle.Bold };
            _count.normal.textColor = Color.white;
        }

        private void DrawRect(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }

        private void DrawBorder(Rect r, Color c, float t)
        {
            DrawRect(new Rect(r.x, r.y, r.width, t), c);
            DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            DrawRect(new Rect(r.x, r.y, t, r.height), c);
            DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        private void OnDestroy()
        {
            if (_white != null) Destroy(_white);
        }
    }
}

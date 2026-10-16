using DonkaPhiPisa.Inventory;
using StarterAssets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DonkaPhiPisa.Inventory.EditorTools
{
    /// <summary>
    /// One-click setup under  Tools > Donka Phi Pisa > Inventory.
    /// Safe to run more than once: it skips anything that already exists.
    /// </summary>
    public static class InventorySetupMenu
    {
        private const string ItemsFolder = "Assets/SourceFiles/Resources/Items";
        private const string MaterialsFolder = "Assets/SourceFiles/Materials/Items";
        private const string PlayerPrefabPath = "Assets/Prefabs/PlayerRobot.prefab";
        private const string Menu = "Tools/Donka Phi Pisa/Inventory/";

        [MenuItem(Menu + "Set Up Everything", priority = 0)]
        public static void SetUpEverything()
        {
            CreateItemAssets();
            AddInventoryToPlayerPrefab();
            AddTestItemsToScene();
        }

        // ------------------------------------------------------------------

        [MenuItem(Menu + "1. Create Item Assets (Flashlight, Battery, Flare)", priority = 20)]
        public static void CreateItemAssets()
        {
            EnsureFolder(ItemsFolder);
            CreateItem("flashlight", "Flashlight", 1, "Lights the way. Needs batteries.");
            CreateItem("battery", "Battery", 5, "Recharges the flashlight.");
            CreateItem("flare", "Flare", 3, "Burns bright red for a while. Throwable.");
            AssetDatabase.SaveAssets();
            Debug.Log($"[Inventory] Item assets ready in {ItemsFolder}");
        }

        [MenuItem(Menu + "2. Add Inventory To PlayerRobot Prefab", priority = 21)]
        public static void AddInventoryToPlayerPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (root == null)
            {
                Debug.LogError($"[Inventory] Couldn't open {PlayerPrefabPath}");
                return;
            }

            try
            {
                FirstPersonController controller = root.GetComponentInChildren<FirstPersonController>(true);
                if (controller == null)
                {
                    Debug.LogError("[Inventory] No FirstPersonController found in PlayerRobot prefab.");
                    return;
                }

                GameObject robot = controller.gameObject;
                bool changed = false;
                if (robot.GetComponent<PlayerInventory>() == null) { robot.AddComponent<PlayerInventory>(); changed = true; }
                if (robot.GetComponent<PlayerInteractor>() == null) { robot.AddComponent<PlayerInteractor>(); changed = true; }
                if (robot.GetComponent<InventoryHUD>() == null) { robot.AddComponent<InventoryHUD>(); changed = true; }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                    Debug.Log($"[Inventory] Added PlayerInventory, PlayerInteractor and InventoryHUD to '{robot.name}' in PlayerRobot.prefab");
                }
                else
                {
                    Debug.Log("[Inventory] PlayerRobot prefab already has the inventory components.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem(Menu + "3. Add Test Items To Open Scene", priority = 22)]
        public static void AddTestItemsToScene()
        {
            CreateItemAssets();

            ItemData flashlight = AssetDatabase.LoadAssetAtPath<ItemData>($"{ItemsFolder}/Flashlight.asset");
            ItemData battery = AssetDatabase.LoadAssetAtPath<ItemData>($"{ItemsFolder}/Battery.asset");
            ItemData flare = AssetDatabase.LoadAssetAtPath<ItemData>($"{ItemsFolder}/Flare.asset");

            // place them in a row a few meters in front of SpawnPoint_1 (or the Scene view camera)
            Vector3 origin, forward, right;
            GameObject spawn = GameObject.Find("SpawnPoint_1");
            if (spawn != null)
            {
                origin = spawn.transform.position;
                forward = Flatten(spawn.transform.forward);
                right = Flatten(spawn.transform.right);
            }
            else if (SceneView.lastActiveSceneView != null)
            {
                origin = SceneView.lastActiveSceneView.pivot;
                forward = Vector3.forward;
                right = Vector3.right;
            }
            else
            {
                origin = Vector3.zero;
                forward = Vector3.forward;
                right = Vector3.right;
            }

            GameObject parent = GameObject.Find("Items");
            if (parent == null)
            {
                parent = new GameObject("Items");
                Undo.RegisterCreatedObjectUndo(parent, "Create Items");
            }

            Vector3 rowCenter = origin + forward * 3f;
            CreateWorldItem(parent.transform, flashlight, 1, PrimitiveType.Cylinder, new Vector3(0.12f, 0.15f, 0.12f),
                Quaternion.Euler(90f, 0f, 0f), new Color(1f, 0.85f, 0.2f), rowCenter - right * 1.5f);
            CreateWorldItem(parent.transform, battery, 2, PrimitiveType.Cube, new Vector3(0.12f, 0.22f, 0.12f),
                Quaternion.identity, new Color(0.3f, 0.9f, 0.4f), rowCenter - right * 0.5f);
            CreateWorldItem(parent.transform, battery, 1, PrimitiveType.Cube, new Vector3(0.12f, 0.22f, 0.12f),
                Quaternion.identity, new Color(0.3f, 0.9f, 0.4f), rowCenter + right * 0.5f);
            CreateWorldItem(parent.transform, flare, 1, PrimitiveType.Capsule, new Vector3(0.1f, 0.15f, 0.1f),
                Quaternion.Euler(90f, 0f, 0f), new Color(1f, 0.25f, 0.2f), rowCenter + right * 1.5f);

            EditorSceneManager.MarkSceneDirty(parent.scene);
            Selection.activeGameObject = parent;
            Debug.Log("[Inventory] Added test items under 'Items'. Save the scene (Ctrl+S) so Mirror assigns their scene IDs.");
        }

        // ------------------------------------------------------------------

        private static void CreateWorldItem(Transform parent, ItemData data, int amount, PrimitiveType shape,
            Vector3 scale, Quaternion rotation, Color color, Vector3 position)
        {
            if (data == null) return;

            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = amount > 1 ? $"{data.displayName} x{amount}" : data.displayName;
            go.transform.SetParent(parent, true);
            go.transform.localScale = scale;
            go.transform.rotation = rotation;
            go.transform.position = SnapToGround(position, go);

            Material mat = GetOrCreateMaterial(data.displayName, color);
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;

            WorldItem worldItem = go.AddComponent<WorldItem>(); // also adds NetworkIdentity
            worldItem.item = data;
            worldItem.amount = amount;

            Undo.RegisterCreatedObjectUndo(go, "Create World Item");
        }

        private static Vector3 SnapToGround(Vector3 position, GameObject go)
        {
            Collider ownCollider = go.GetComponent<Collider>();
            if (ownCollider != null) ownCollider.enabled = false;

            Vector3 result = position + Vector3.up * 0.5f;
            if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
            {
                float halfHeight = go.GetComponent<Renderer>().bounds.extents.y;
                result = hit.point + Vector3.up * (halfHeight + 0.02f);
            }

            if (ownCollider != null) ownCollider.enabled = true;
            return result;
        }

        private static Material GetOrCreateMaterial(string itemName, Color color)
        {
            EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/{itemName}_Mat.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;

            mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            // slight glow so items are findable in the dark
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 0.4f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void CreateItem(string id, string displayName, int maxStack, string description)
        {
            string path = $"{ItemsFolder}/{displayName}.asset";
            if (AssetDatabase.LoadAssetAtPath<ItemData>(path) != null) return;

            ItemData item = ScriptableObject.CreateInstance<ItemData>();
            item.itemId = id;
            item.displayName = displayName;
            item.maxStack = maxStack;
            item.description = description;
            AssetDatabase.CreateAsset(item, path);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 0.001f ? Vector3.forward : v.normalized;
        }
    }
}

using UnityEditor;
using UnityEngine;

/// <summary>
/// Crea los prefabs reutilizables de llave y puerta con sus componentes ya cableados.
/// Se ejecuta desde el menu Xibalba/Progreso y se puede repetir para regenerarlos.
/// </summary>
public static class ProgressPrefabBuilder
{
    private const string KeyPrefabPath = "Assets/Art/Prefabs/KeyPickup.prefab";
    private const string DoorPrefabPath = "Assets/Art/Prefabs/LockedDoor.prefab";
    private const string JadeMaterialPath = "Assets/Materials/Jade.mat";
    private const string DoorMaterialPath = "Assets/Materials/DoorStone.mat";
    private const string BaseMaterialPath = "Assets/Resources/DaggerGold.mat";

    [MenuItem("Xibalba/Progreso/Crear prefabs de llave y puerta")]
    public static void CreatePrefabs()
    {
        Material jade = EnsureMaterial(JadeMaterialPath,
            new Color(0.08f, 0.72f, 0.42f), new Color(0.02f, 0.24f, 0.13f), new Color(0.35f, 1f, 0.7f));

        Material stone = EnsureMaterial(DoorMaterialPath,
            new Color(0.34f, 0.28f, 0.22f), new Color(0.09f, 0.07f, 0.05f), new Color(0.6f, 0.5f, 0.35f));

        CreateKeyPrefab(jade);
        CreateDoorPrefab(stone);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Progreso] Prefabs de llave y puerta listos en Assets/Art/Prefabs.");
    }

    private static void CreateKeyPrefab(Material material)
    {
        GameObject root = new GameObject("KeyPickup");

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        ApplyMaterial(visual, material);

        CircleCollider2D trigger = root.AddComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius = 0.35f;

        root.AddComponent<KeyPickup>();
        root.AddComponent<RotatingPickup>();

        PrefabUtility.SaveAsPrefabAsset(root, KeyPrefabPath);
        Object.DestroyImmediate(root);
    }

    private static void CreateDoorPrefab(Material material)
    {
        GameObject root = new GameObject("LockedDoor");

        BoxCollider2D blocker = root.AddComponent<BoxCollider2D>();
        blocker.size = new Vector2(1.4f, 2.8f);

        GameObject leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leaf.name = "Leaf";
        leaf.transform.SetParent(root.transform, false);
        leaf.transform.localScale = new Vector3(1.4f, 2.8f, 0.3f);
        Object.DestroyImmediate(leaf.GetComponent<Collider>());
        ApplyMaterial(leaf, material);

        LockedDoor door = root.AddComponent<LockedDoor>();

        SerializedObject serializedDoor = new SerializedObject(door);
        SerializedProperty closedVisual = serializedDoor.FindProperty("closedVisual");
        if (closedVisual != null)
            closedVisual.objectReferenceValue = leaf;
        serializedDoor.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, DoorPrefabPath);
        Object.DestroyImmediate(root);
    }

    private static Material EnsureMaterial(string path, Color baseColor, Color shadeColor, Color rimColor)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Material source = AssetDatabase.LoadAssetAtPath<Material>(BaseMaterialPath);
        if (source == null)
        {
            Debug.LogError("[Progreso] No se encontro " + BaseMaterialPath + " para crear materiales.");
            return null;
        }

        Material material = new Material(source);
        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_ShadeColor", shadeColor);
        material.SetColor("_RimColor", rimColor);

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void ApplyMaterial(GameObject target, Material material)
    {
        if (material == null) return;

        MeshRenderer renderer = target.GetComponent<MeshRenderer>();
        if (renderer != null)
            renderer.sharedMaterial = material;
    }
}

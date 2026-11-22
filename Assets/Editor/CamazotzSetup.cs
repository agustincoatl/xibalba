#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Genera el prefab del jefe 1 a partir del modelo FBX ya importado y del
/// AnimatorCamazotz.controller. No edita escenas: luego arrastra el prefab
/// a Battle-01 y ajusta los limites de arena en el inspector.
/// </summary>
public static class CamazotzSetup
{
    private const string ModelPath = "Assets/Art/Boss1Camazotz/Animations/base.fbx";
    private const string ControllerPath = "Assets/Art/Boss1Camazotz/Animations/AnimatorCamazotz.controller";
    private const string PrefabPath = "Assets/Art/Prefabs/Boss1Camazotz.prefab";

    [MenuItem("Tools/Boss 1/Crear prefab Camazotz")]
    public static void CreatePrefab()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);

        if (model == null)
        {
            Debug.LogError("[Boss1] No se encontro el modelo en " + ModelPath);
            return;
        }

        if (controller == null)
        {
            Debug.LogError("[Boss1] No se encontro el animator en " + ControllerPath);
            return;
        }

        GameObject root = new GameObject("Boss1Camazotz");

        Rigidbody2D rb = root.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        CapsuleCollider2D col = root.AddComponent<CapsuleCollider2D>();
        col.direction = CapsuleDirection2D.Vertical;
        col.size = new Vector2(1.2f, 1.9f);
        col.isTrigger = false; // los golpes del jugador usan OverlapCircle

        GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        modelInstance.name = "Model";
        modelInstance.transform.SetParent(root.transform, false);
        modelInstance.transform.localPosition = Vector3.zero;
        modelInstance.transform.localRotation = Quaternion.identity;
        modelInstance.transform.localScale = Vector3.one;

        Animator animator = modelInstance.GetComponent<Animator>();
        if (animator == null) animator = modelInstance.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        CamazotzBoss boss = root.AddComponent<CamazotzBoss>();
        SerializedObject serializedBoss = new SerializedObject(boss);
        serializedBoss.FindProperty("animator").objectReferenceValue = animator;
        serializedBoss.FindProperty("modelRoot").objectReferenceValue = modelInstance.transform;
        serializedBoss.FindProperty("bodyCollider").objectReferenceValue = col;
        // El jefe nace dormido: solo despierta cuando el jugador entra a la zona.
        serializedBoss.FindProperty("battleActive").boolValue = false;
        serializedBoss.FindProperty("initialState").enumValueIndex = (int)BossState.Dormant;
        // Facing por rotacion Y (nunca por localScale.x). No hay ventana de
        // castigo: Idle/Walk siempre son dañables y los i-frames solo existen
        // mientras se ejecuta un ataque.
        serializedBoss.FindProperty("modelFacesRightByDefault").boolValue = false;
        serializedBoss.FindProperty("attackCooldown").floatValue = 0.65f;
        serializedBoss.FindProperty("phase2AttackCooldownMultiplier").floatValue = 0.65f;
        serializedBoss.FindProperty("phase2DecisionTimeMultiplier").floatValue = 0.65f;
        serializedBoss.FindProperty("specialAttackCooldown").floatValue = 0f;
        serializedBoss.FindProperty("phase2SuperSonicDuration").floatValue = 4f;
        serializedBoss.FindProperty("phase2SuperSonicInterval").floatValue = 0.32f;
        serializedBoss.FindProperty("phase2SuperSonicWaves").intValue = 8;

        // Areas de spawn desactivadas = comportamiento original de la arena.
        SetAreaDefaults(serializedBoss, "screamSpawnArea", new Rect(0f, 0f, 10f, 10f));
        SetAreaDefaults(serializedBoss, "spellSpawnArea", new Rect(0f, 0f, 10f, 10f));
        serializedBoss.FindProperty("sonicOriginForwardOffset").floatValue = 0.6f;
        serializedBoss.FindProperty("fangEdgeInset").floatValue = 0.6f;

        // Dive alineado con la trayectoria y telegraph/area activa del Sweep.
        serializedBoss.FindProperty("maxDiveTilt").floatValue = 60f;
        serializedBoss.FindProperty("diveTiltSpeed").floatValue = 420f;
        serializedBoss.FindProperty("tiltRecoverySpeed").floatValue = 240f;
        serializedBoss.FindProperty("sweepTelegraphColor").colorValue = new Color(1f, 0.55f, 0.15f, 0.9f);
        serializedBoss.FindProperty("sweepTelegraphWidth").floatValue = 0.22f;
        serializedBoss.FindProperty("sweepActiveColor").colorValue = new Color(1f, 0.15f, 0.25f, 0.55f);
        serializedBoss.FindProperty("sweepActiveSortingOrder").intValue = 21;
        serializedBoss.ApplyModifiedPropertiesWithoutUndo();

        // La barra/nombre se crean solos en runtime; aqui solo cableamos el jefe.
        BossHealthUI healthUI = root.AddComponent<BossHealthUI>();
        SerializedObject serializedUI = new SerializedObject(healthUI);
        serializedUI.FindProperty("boss").objectReferenceValue = boss;
        serializedUI.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();

        Debug.Log("[Boss1] Prefab creado en " + PrefabPath +
            ". Arrastralo a Battle-01 y ajusta la arena (la barra de vida se crea sola).");
    }

    private static void SetAreaDefaults(SerializedObject serializedBoss, string path, Rect rect)
    {
        serializedBoss.FindProperty(path + ".enabled").boolValue = false;
        serializedBoss.FindProperty(path + ".space").enumValueIndex = (int)BossAreaSpace.Arena;
        serializedBoss.FindProperty(path + ".rect").rectValue = rect;
        serializedBoss.FindProperty(path + ".offset").vector2Value = Vector2.zero;
        serializedBoss.FindProperty(path + ".clampToArena").boolValue = true;
    }
}
#endif

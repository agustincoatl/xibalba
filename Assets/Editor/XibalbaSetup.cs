#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Genera el prefab del jefe 2 a partir del modelo FBX y del Animator de
/// Xibalba ya importados. No modifica escenas; despues arrastra el prefab a
/// Room-boss y ajusta la arena en el Inspector.
/// </summary>
public static class XibalbaSetup
{
    private const string ModelPath = "Assets/Art/Boss2Xibalba/Animations/base.fbx";
    private const string ControllerPath = "Assets/Art/Boss2Xibalba/Animations/XibalbaAnimator.controller";
    private const string PrefabPath = "Assets/Art/Prefabs/Boss2Xibalba.prefab";

    [MenuItem("Xibalba/Boss 2/Crear prefab Xibalba")]
    public static void CreatePrefab()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);

        if (model == null)
        {
            Debug.LogError("[Boss2] No se encontro el modelo en " + ModelPath);
            return;
        }

        if (controller == null)
        {
            Debug.LogError("[Boss2] No se encontro el animator en " + ControllerPath);
            return;
        }

        GameObject root = new GameObject("Boss2Xibalba");

        Rigidbody2D rb = root.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        CapsuleCollider2D col = root.AddComponent<CapsuleCollider2D>();
        col.direction = CapsuleDirection2D.Vertical;
        col.size = new Vector2(1.2f, 1.9f);
        col.isTrigger = false;

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

        XibalbaBoss boss = root.AddComponent<XibalbaBoss>();
        SerializedObject serializedBoss = new SerializedObject(boss);
        SetReference(serializedBoss, "animator", animator);
        SetReference(serializedBoss, "modelRoot", modelInstance.transform);
        SetReference(serializedBoss, "bodyCollider", col);

        // Xibalba espera dormido hasta que el trigger de la arena active el
        // encuentro. La vida, fase y ataques quedan configurables en Inspector.
        SetBool(serializedBoss, "battleActive", false);
        SetEnum(serializedBoss, "initialState", BossState.Dormant);
        SetBool(serializedBoss, "modelFacesRightByDefault", false);

        // Valores iniciales conservadores para que el prefab sea jugable al
        // generarlo, manteniendo las mismas reglas del prefab actual.
        SetFloat(serializedBoss, "attackCooldown", 0.5f);
        SetFloat(serializedBoss, "phase2AttackCooldownMultiplier", 0.65f);
        SetFloat(serializedBoss, "phase2AnimatorSpeed", 1.15f);
        SetFloat(serializedBoss, "phase2ScreamDuration", 1.8f);

        SetAreaDefaults(serializedBoss, "jumpTargetArea",
            new Rect(0f, 0f, 10f, 6f));
        SetAreaDefaults(serializedBoss, "obsidianSpawnArea",
            new Rect(0f, 0f, 10f, 6f));
        serializedBoss.ApplyModifiedPropertiesWithoutUndo();

        BossHealthUI healthUI = root.AddComponent<BossHealthUI>();
        SerializedObject serializedUI = new SerializedObject(healthUI);
        SetReference(serializedUI, "boss", boss);
        SetString(serializedUI, "bossName", "XIBALBA");
        serializedUI.ApplyModifiedPropertiesWithoutUndo();

        // El jefe final dispara la pantalla de victoria al emitir Defeated.
        VictoryOnDeath victory = root.AddComponent<VictoryOnDeath>();
        SerializedObject serializedVictory = new SerializedObject(victory);
        SetInt(serializedVictory, "pointsOnVictory", 0);
        serializedVictory.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[Boss2] Prefab creado en " + PrefabPath +
                  ". Arrastralo a Room-boss y ajusta la arena en el Inspector.");
    }

    private static void SetAreaDefaults(SerializedObject serializedObject,
        string path, Rect rect)
    {
        SerializedProperty area = serializedObject.FindProperty(path);
        if (area == null) return;

        SerializedProperty enabled = area.FindPropertyRelative("enabled");
        SerializedProperty space = area.FindPropertyRelative("space");
        SerializedProperty areaRect = area.FindPropertyRelative("rect");
        SerializedProperty offset = area.FindPropertyRelative("offset");
        SerializedProperty clampToArena = area.FindPropertyRelative("clampToArena");

        if (enabled != null) enabled.boolValue = false;
        if (space != null) space.enumValueIndex = (int)BossAreaSpace.Arena;
        if (areaRect != null) areaRect.rectValue = rect;
        if (offset != null) offset.vector2Value = Vector2.zero;
        if (clampToArena != null) clampToArena.boolValue = true;
    }

    private static void SetReference(SerializedObject serializedObject,
        string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null) property.objectReferenceValue = value;
    }

    private static void SetBool(SerializedObject serializedObject,
        string propertyName, bool value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null) property.boolValue = value;
    }

    private static void SetFloat(SerializedObject serializedObject,
        string propertyName, float value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null) property.floatValue = value;
    }

    private static void SetEnum(SerializedObject serializedObject,
        string propertyName, BossState value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null) property.enumValueIndex = (int)value;
    }

    private static void SetString(SerializedObject serializedObject,
        string propertyName, string value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null) property.stringValue = value;
    }

    private static void SetInt(SerializedObject serializedObject,
        string propertyName, int value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null) property.intValue = value;
    }
}
#endif

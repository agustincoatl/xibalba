#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Genera y valida el Animator Controller y el prefab del enemigo basico 3D.
/// Usa la API de Unity (PrefabUtility / AnimatorController) para que no haya
/// que escribir fileIDs de los FBX a mano. No modifica escenas.
/// </summary>
public static class BasicEnemySetup
{
    private const string ModelPath = "Assets/Art/BasicEnemy/base.fbx";
    private const string ControllerPath = "Assets/Art/BasicEnemy/Animations/BasicEnemyAnimator.controller";
    private const string PrefabPath = "Assets/Art/Prefabs/BasicEnemy.prefab";
    private const string CoinPrefabPath = "Assets/Art/Prefabs/Coin_Up.prefab";

    private const string IdleClip = "Assets/Art/BasicEnemy/Idle.fbx";
    private const string WalkClip = "Assets/Art/BasicEnemy/Walk.fbx";
    private const string RunClip = "Assets/Art/BasicEnemy/Run.fbx";
    private const string AttackClip = "Assets/Art/BasicEnemy/Attack.fbx";

    private static readonly string[] StateNames = { "Idle", "Walk", "Run", "Attack" };
    private static readonly string[] BoolParameters = { "Walking", "Running" };

    [MenuItem("Xibalba/Basic Enemy/Crear animator y prefab")]
    public static void Create()
    {
        if (!CreateController()) return;
        CreatePrefab();
    }

    [MenuItem("Xibalba/Basic Enemy/Validar animator y prefab")]
    public static void Validate()
    {
        var report = new List<string>();

        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError("[BasicEnemy] No se encontro el controller en " + ControllerPath);
            return;
        }

        ValidateParameters(controller, report);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (string stateName in StateNames)
            if (FindState(machine, stateName) == null)
                report.Add("FALTA estado: " + stateName);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            report.Add("FALTA prefab: " + PrefabPath);
        }
        else
        {
            BasicEnemy enemy = prefab.GetComponent<BasicEnemy>();
            if (enemy == null) report.Add("FALTA componente BasicEnemy en el prefab.");
            if (prefab.GetComponent<Rigidbody2D>() == null) report.Add("FALTA Rigidbody2D.");
            if (prefab.GetComponent<Collider2D>() == null) report.Add("FALTA Collider2D.");

            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            if (animator == null) report.Add("FALTA Animator en el modelo del prefab.");
            else if (animator.runtimeAnimatorController == null)
                report.Add("El Animator del prefab no tiene controller asignado.");

            SerializedObject serialized = new SerializedObject(enemy);
            CheckReference(serialized, "modelRoot", report);
            CheckReference(serialized, "animator", report);
            CheckArrayReference(serialized, "lootPrefabs", report);
        }

        if (report.Count == 0)
            Debug.Log("[BasicEnemy] Validacion correcta: animator (Idle/Walk/Run/Attack) y prefab.", controller);
        else
            Debug.LogWarning("[BasicEnemy] " + string.Join(" | ", report), controller);
    }

    private static bool CreateController()
    {
        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            if (controller == null)
            {
                Debug.LogError("[BasicEnemy] No se pudo crear el controller en " + ControllerPath);
                return false;
            }
        }

        Undo.RegisterCompleteObjectUndo(controller, "Configure BasicEnemy Animator");

        // Los parametros se crean antes que las transiciones que los usan.
        foreach (string parameter in BoolParameters)
            EnsureParameter(controller, parameter, AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "Attack", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        ConfigureState(machine, "Idle", IdleClip, 1f, new Vector3(240f, 120f, 0f));
        ConfigureState(machine, "Walk", WalkClip, 1f, new Vector3(240f, -20f, 0f));
        ConfigureState(machine, "Run", RunClip, 1f, new Vector3(520f, -20f, 0f));
        ConfigureState(machine, "Attack", AttackClip, 1.5f, new Vector3(520f, -160f, 0f));

        // Idle <-> Walk: el script pone Walking en true solo durante la patrulla.
        ConfigureStateTransition(machine, "Idle", "Walk", 0.08f, false,
            Condition(AnimatorConditionMode.If, "Walking"));
        ConfigureStateTransition(machine, "Walk", "Idle", 0.08f, false,
            Condition(AnimatorConditionMode.IfNot, "Walking"));

        // Any State -> Run: la carrera tiene prioridad sobre Walk/Attack.
        ConfigureAnyTransition(machine, "Run", 0.08f,
            Condition(AnimatorConditionMode.If, "Running"));

        // Run -> Walk / Idle: al dejar de correr vuelve al estado de patrulla.
        ConfigureStateTransition(machine, "Run", "Walk", 0.08f, false,
            Condition(AnimatorConditionMode.IfNot, "Running"),
            Condition(AnimatorConditionMode.If, "Walking"));
        ConfigureStateTransition(machine, "Run", "Idle", 0.08f, false,
            Condition(AnimatorConditionMode.IfNot, "Running"),
            Condition(AnimatorConditionMode.IfNot, "Walking"));

        // Any State -> Attack: trigger del FSM al entrar en rango de ataque.
        ConfigureAnyTransition(machine, "Attack", 0.05f,
            Condition(AnimatorConditionMode.If, "Attack"));

        // Salida del ataque por fin de clip; vuelve a Idle y la FSM decide.
        ConfigureStateTransition(machine, "Attack", "Idle", 0.08f, true, 0.92f);

        if (machine.defaultState == null || FindState(machine, "Idle") != null)
            machine.defaultState = FindState(machine, "Idle");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return true;
    }

    private static void CreatePrefab()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        GameObject coin = AssetDatabase.LoadAssetAtPath<GameObject>(CoinPrefabPath);
        RuntimeAnimatorController controller =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (model == null)
        {
            Debug.LogError("[BasicEnemy] No se encontro el modelo en " + ModelPath);
            return;
        }
        if (controller == null)
        {
            Debug.LogError("[BasicEnemy] No se encontro el controller en " + ControllerPath);
            return;
        }
        if (coin == null)
            Debug.LogWarning("[BasicEnemy] No se encontro el prefab de moneda en " + CoinPrefabPath);

        GameObject root = new GameObject("BasicEnemy");

        Rigidbody2D rb = root.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 1f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        CapsuleCollider2D col = root.AddComponent<CapsuleCollider2D>();
        col.direction = CapsuleDirection2D.Vertical;
        col.size = new Vector2(0.9f, 1.8f);
        col.offset = new Vector2(0f, 0.9f);
        col.isTrigger = false;

        GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        modelInstance.name = "Model";
        modelInstance.transform.SetParent(root.transform, false);
        modelInstance.transform.localPosition = Vector3.zero;
        modelInstance.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        modelInstance.transform.localScale = Vector3.one;

        Animator animator = modelInstance.GetComponent<Animator>();
        if (animator == null) animator = modelInstance.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        BasicEnemy enemy = root.AddComponent<BasicEnemy>();
        SerializedObject serialized = new SerializedObject(enemy);
        SetReference(serialized, "modelRoot", modelInstance.transform);
        SetReference(serialized, "animator", animator);
        if (coin != null) SetArrayReference(serialized, "lootPrefabs", coin);
        // Los tilemaps de suelo estan en Default; asi los sensores de borde y
        // pared funcionan sin tocar el Inspector al arrastrar el prefab.
        SetLayerMask(serialized, "groundLayer", 1 << 0);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[BasicEnemy] Prefab creado en " + PrefabPath +
                  ". Arrastralo a la sala y ajusta rango/velocidad en el Inspector.");
    }

    private static void ValidateParameters(AnimatorController controller, List<string> report)
    {
        var parameters = new Dictionary<string, AnimatorControllerParameterType>();
        foreach (AnimatorControllerParameter parameter in controller.parameters)
            parameters[parameter.name] = parameter.type;

        foreach (string parameter in BoolParameters)
        {
            if (!parameters.ContainsKey(parameter))
                report.Add("FALTA parametro: " + parameter);
            else if (parameters[parameter] != AnimatorControllerParameterType.Bool)
                report.Add("TIPO INCORRECTO: " + parameter + " debe ser Bool.");
        }

        if (!parameters.ContainsKey("Attack"))
            report.Add("FALTA parametro: Attack");
        else if (parameters["Attack"] != AnimatorControllerParameterType.Trigger)
            report.Add("TIPO INCORRECTO: Attack debe ser Trigger.");
    }

    private static void EnsureParameter(AnimatorController controller, string name,
        AnimatorControllerParameterType type)
    {
        if (controller.parameters.Any(parameter => parameter.name == name)) return;
        controller.AddParameter(name, type);
    }

    private static void ConfigureState(AnimatorStateMachine machine, string name,
        string clipPath, float speed, Vector3 position)
    {
        AnimatorState state = FindState(machine, name);
        if (state == null) state = machine.AddState(name, position);
        else SetStatePosition(machine, state, position);

        if (!string.IsNullOrEmpty(clipPath))
        {
            AnimationClip clip = LoadFirstClip(clipPath);
            if (clip != null)
            {
                state.motion = clip;
                // Los clip FBX de Mixamo se importan en modo Once; Walk y Run
                // deben repetirse mientras dure la patrulla / la carrera.
                bool shouldLoop = name == "Walk" || name == "Run";
                if (shouldLoop && clip.wrapMode != WrapMode.Loop)
                {
                    Undo.RegisterCompleteObjectUndo(clip, "Loop BasicEnemy clip");
                    clip.wrapMode = WrapMode.Loop;
                    EditorUtility.SetDirty(clip);
                }
            }
            else Debug.LogWarning("[BasicEnemy] No se encontro un clip en " + clipPath);
        }
        state.speed = speed;
    }

    // AnimatorState no expone su posicion: se ajusta en el ChildAnimatorState
    // del state machine y se vuelve a asignar el array completo.
    private static void SetStatePosition(AnimatorStateMachine machine,
        AnimatorState state, Vector3 position)
    {
        ChildAnimatorState[] children = machine.states;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].state != state) continue;
            children[i].position = position;
            machine.states = children;
            return;
        }
    }

    private static AnimationClip LoadFirstClip(string path)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        return assets.OfType<AnimationClip>().FirstOrDefault();
    }

    private static AnimatorState FindState(AnimatorStateMachine machine, string name)
    {
        return machine.states.Select(child => child.state).FirstOrDefault(state => state.name == name);
    }

    private static AnimatorCondition Condition(AnimatorConditionMode mode, string parameter)
    {
        return new AnimatorCondition { mode = mode, parameter = parameter };
    }

    private static void ConfigureAnyTransition(AnimatorStateMachine machine, string destinationName,
        float duration, params AnimatorCondition[] conditions)
    {
        AnimatorState destination = FindState(machine, destinationName);
        if (destination == null) return;

        AnimatorStateTransition transition = machine.anyStateTransitions
            .FirstOrDefault(candidate => candidate.destinationState == destination && SameConditions(candidate, conditions));
        if (transition == null) transition = machine.AddAnyStateTransition(destination);
        Configure(transition, duration, false, conditions);
    }

    private static void ConfigureStateTransition(AnimatorStateMachine machine, string sourceName,
        string destinationName, float duration, bool hasExitTime, params AnimatorCondition[] conditions)
    {
        ConfigureStateTransition(machine, sourceName, destinationName, duration, hasExitTime, 0f, conditions);
    }

    private static void ConfigureStateTransition(AnimatorStateMachine machine, string sourceName,
        string destinationName, float duration, bool hasExitTime, float exitTime,
        params AnimatorCondition[] conditions)
    {
        AnimatorState source = FindState(machine, sourceName);
        AnimatorState destination = FindState(machine, destinationName);
        if (source == null || destination == null) return;

        AnimatorStateTransition transition = source.transitions
            .FirstOrDefault(candidate => candidate.destinationState == destination && SameConditions(candidate, conditions));
        if (transition == null) transition = source.AddTransition(destination);
        Configure(transition, duration, hasExitTime, conditions);
        if (hasExitTime) transition.exitTime = exitTime;
    }

    private static void Configure(AnimatorStateTransition transition, float duration,
        bool hasExitTime, params AnimatorCondition[] conditions)
    {
        transition.hasExitTime = hasExitTime;
        transition.exitTime = hasExitTime ? Mathf.Max(0.01f, transition.exitTime) : 0f;
        transition.hasFixedDuration = true;
        transition.duration = duration;
        transition.canTransitionToSelf = false;
        transition.conditions = conditions ?? Array.Empty<AnimatorCondition>();
    }

    private static bool SameConditions(AnimatorStateTransition transition, AnimatorCondition[] expected)
    {
        AnimatorCondition[] actual = transition.conditions;
        if (actual.Length != expected.Length) return false;
        for (int i = 0; i < actual.Length; i++)
            if (actual[i].mode != expected[i].mode || actual[i].parameter != expected[i].parameter)
                return false;
        return true;
    }

    private static void CheckReference(SerializedObject serializedObject, string propertyName,
        List<string> report)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || property.objectReferenceValue == null)
            report.Add("Referencia sin asignar: " + propertyName);
    }

    private static void SetReference(SerializedObject serializedObject, string propertyName,
        UnityEngine.Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null) property.objectReferenceValue = value;
    }

    private static void SetLayerMask(SerializedObject serializedObject, string propertyName, int bits)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null) property.intValue = bits;
    }

    private static void CheckArrayReference(SerializedObject serializedObject, string propertyName,
        List<string> report)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || !property.isArray || property.arraySize == 0)
        {
            report.Add("Drop sin asignar: " + propertyName);
            return;
        }
        for (int i = 0; i < property.arraySize; i++)
        {
            if (property.GetArrayElementAtIndex(i).objectReferenceValue == null)
            {
                report.Add("Drop vacio en " + propertyName + "[" + i + "]");
                return;
            }
        }
    }

    private static void SetArrayReference(SerializedObject serializedObject, string propertyName,
        UnityEngine.Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || !property.isArray) return;
        property.arraySize = 1;
        property.GetArrayElementAtIndex(0).objectReferenceValue = value;
    }
}
#endif

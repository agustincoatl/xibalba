using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Herramienta de un solo uso: crea el clip/controlador de animacion,
// prepara al Player de Room-01 y lo guarda como prefab reutilizable.
public static class PlayerAnimationSetup
{
    private const string FbxPath = "Assets/Art/Player/Body Block.fbx";
    private const string ClipPath = "Assets/Art/Player/PlayerIdle.anim";
    private const string ControllerPath = "Assets/Art/Player/PlayerAnimator.controller";
    private const string PrefabDir = "Assets/Art/Prefabs";
    private const string PrefabPath = PrefabDir + "/Player.prefab";
    private const string ScenePath = "Assets/Scenes/Level-01/Room-01.unity";

    [MenuItem("Xibalba/Player/Setup Player Animation")]
    public static void Setup()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        AnimationClip sourceClip = null;
        Avatar avatar = null;

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
        {
            if (sourceClip == null && asset is AnimationClip foundClip && !foundClip.name.StartsWith("__preview__"))
                sourceClip = foundClip;

            if (avatar == null && asset is Avatar foundAvatar)
                avatar = foundAvatar;
        }

        if (sourceClip == null)
        {
            Debug.LogError($"[PlayerAnimationSetup] No se encontro ningun AnimationClip en {FbxPath}");
            return;
        }

        Debug.Log($"[PlayerAnimationSetup] Clip: '{sourceClip.name}' duracion={sourceClip.length:F2}s humanMotion={sourceClip.isHumanMotion}");

        if (avatar != null)
            Debug.Log($"[PlayerAnimationSetup] Avatar: '{avatar.name}' isHuman={avatar.isHuman} isValid={avatar.isValid}");
        else
            Debug.LogWarning("[PlayerAnimationSetup] El FBX no tiene Avatar; el Animator usara el que ya tenga el modelo.");

        EnsureFolder("Assets/Art/Player");
        EnsureFolder(PrefabDir);

        AnimationClip clip = CreateLoopingClip(sourceClip);
        AnimatorController controller = CreateController(clip);

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        PlayerController player = Object.FindAnyObjectByType<PlayerController>();

        if (player == null)
        {
            Debug.LogError("[PlayerAnimationSetup] No se encontro PlayerController en Room-01.");
            return;
        }

        Animator animator = SetupAnimator(player, controller, avatar);
        SetupAnimationDriver(player, animator);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        GameObject prefab = SavePlayerPrefab(player, scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (prefab != null)
        {
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            Debug.Log($"[PlayerAnimationSetup] Listo. Prefab: {PrefabPath}");
        }
    }

    private static AnimationClip CreateLoopingClip(AnimationClip sourceClip)
    {
        AssetDatabase.DeleteAsset(ClipPath);

        AnimationClip clip = Object.Instantiate(sourceClip);
        clip.name = "PlayerIdle";

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        settings.loopBlend = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        AssetDatabase.CreateAsset(clip, ClipPath);
        AssetDatabase.SaveAssets();

        return clip;
    }

    private static AnimatorController CreateController(AnimationClip clip)
    {
        AssetDatabase.DeleteAsset(ControllerPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        AddParameter(controller, "Speed", AnimatorControllerParameterType.Float);
        AddParameter(controller, "Grounded", AnimatorControllerParameterType.Bool);
        AddParameter(controller, "VerticalSpeed", AnimatorControllerParameterType.Float);
        AddParameter(controller, "Dash", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "Attack", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "Hurt", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "Dead", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

        foreach (ChildAnimatorState childState in stateMachine.states.ToArray())
            stateMachine.RemoveState(childState.state);

        AnimatorState state = stateMachine.AddState("Idle", new Vector3(300f, 80f, 0f));
        state.motion = clip;
        state.speed = 1f;
        state.speedParameterActive = true;
        state.speedParameter = "Speed";
        state.writeDefaultValues = true;
        stateMachine.defaultState = state;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        return controller;
    }

    private static Animator SetupAnimator(PlayerController player, AnimatorController controller, Avatar avatar)
    {
        Animator animator = player.GetComponentInChildren<Animator>(true);

        if (animator == null)
        {
            SkinnedMeshRenderer skinnedMesh = player.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Transform target = skinnedMesh != null ? skinnedMesh.transform : player.transform;
            GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(target.gameObject);

            if (prefabRoot != null)
                target = prefabRoot.transform;

            animator = target.gameObject.AddComponent<Animator>();
            Debug.Log($"[PlayerAnimationSetup] Animator anadido a '{target.name}'.");
        }

        animator.runtimeAnimatorController = controller;

        if (avatar != null && (animator.avatar == null || !animator.avatar.isValid))
            animator.avatar = avatar;

        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.enabled = true;

        EditorUtility.SetDirty(animator);

        return animator;
    }

    private static void SetupAnimationDriver(PlayerController player, Animator animator)
    {
        PlayerAnimation driver = player.GetComponent<PlayerAnimation>();

        if (driver == null)
            driver = player.gameObject.AddComponent<PlayerAnimation>();

        SerializedObject serializedDriver = new SerializedObject(driver);
        SerializedProperty animatorProperty = serializedDriver.FindProperty("animator");

        if (animatorProperty != null)
            animatorProperty.objectReferenceValue = animator;

        serializedDriver.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject SavePlayerPrefab(PlayerController player, Scene scene)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(player.gameObject);

            if (root == player.gameObject)
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);

            AssetDatabase.DeleteAsset(PrefabPath);
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
            player.gameObject,
            PrefabPath,
            InteractionMode.AutomatedAction);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        return prefab;
    }

    private static void AddParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        if (controller.parameters.Any(parameter => parameter.name == name))
            return;

        controller.AddParameter(name, type);
    }

    private static void EnsureFolder(string path)
    {
        path = path.Replace('\\', '/');

        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string folderName = Path.GetFileName(path);

        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folderName))
            return;

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }
}

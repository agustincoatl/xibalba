#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Valida y configura el Animator de Camazotz desde Unity, con Undo. No edita
/// YAML a mano y no agrega Any State -> Idle/Walk: esos estados se gobiernan
/// por Walking/IsFlying desde la FSM.
/// </summary>
public static class CamazotzAnimatorSetup
{
    private const string ControllerPath = "Assets/Art/Boss1Camazotz/Animations/AnimatorCamazotz.controller";

    private static readonly string[] RequiredParameters =
    {
        "BattleActive", "Walking", "IsFlying", "Enraged",
        "BasicAttack", "ComboAttack", "Sonic", "Fangs", "Dive", "Sweep",
        "Die", "IdleReset", "SuperSonic"
    };

    private static readonly string[] BoolParameters =
    {
        "BattleActive", "Walking", "IsFlying", "Enraged"
    };

    private static readonly string[] LegacyStates = { "Stunned", "Recover", "Vulnerable", "Lurk", "Chase" };

    [MenuItem("Tools/Boss 1/Validate Camazotz Animator")]
    public static void Validate()
    {
        AnimatorController controller = LoadController();
        if (controller == null) return;

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        var report = new List<string>();
        ValidateParameters(controller, report);

        string[] states = { "Idle", "Walk", "Fly", "BasicAttack", "ComboAttack", "Scream", "Spell", "SuperSonic", "Dead" };
        foreach (string stateName in states)
        {
            if (FindState(machine, stateName) == null)
                report.Add($"FALTA estado: {stateName}");
        }

        foreach (string legacy in LegacyStates)
        {
            if (FindState(machine, legacy) != null)
                report.Add($"LEGACY: el estado {legacy} ya no pertenece al flujo Idle/Walk/Ataque.");
        }

        foreach (AnimatorStateTransition transition in machine.anyStateTransitions)
        {
            if (transition.destinationState == null) continue;
            if (transition.destinationState.name == "Walk" ||
                (transition.destinationState.name == "Idle" && !HasCondition(transition, "IdleReset")))
                report.Add($"REVISAR: Any State -> {transition.destinationState.name} sin IdleReset; compite con Walking.");
            if (transition.hasExitTime)
                report.Add($"REVISAR: Any State -> {transition.destinationState.name} tiene Has Exit Time activo.");
        }

        if (report.Count == 0)
            Debug.Log("[CamazotzAnimator] Validacion correcta: parametros, Any State y estados principales.", controller);
        else
            Debug.LogWarning("[CamazotzAnimator] " + string.Join(" | ", report), controller);
    }

    [MenuItem("Tools/Boss 1/Apply Camazotz Animator Rules")]
    public static void ApplyRules()
    {
        AnimatorController controller = LoadController();
        if (controller == null) return;
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        Undo.RegisterCompleteObjectUndo(controller, "Configure Camazotz Animator");
        EnsureParameter(controller, "SuperSonic", AnimatorControllerParameterType.Trigger);
        EnsureSuperSonicState(machine);
        RemoveLegacyParameters(controller);

        foreach (string legacy in LegacyStates)
        {
            AnimatorState state = FindState(machine, legacy);
            if (state != null) machine.RemoveState(state);
        }

        // Idle y Walk se gobiernan desde Idle <-> Walk. Solo se conserva
        // Any State -> Idle cuando usa IdleReset: es la salida limpia de
        // SuperSonic y evita estados colgados tras un retry.
        foreach (AnimatorStateTransition transition in machine.anyStateTransitions.ToArray())
        {
            string destination = transition.destinationState != null ? transition.destinationState.name : string.Empty;
            if (destination == "Walk" ||
                (destination == "Idle" && !HasCondition(transition, "IdleReset")))
                machine.RemoveAnyStateTransition(transition);
        }

        ConfigureStateTransition(machine, "Idle", "Walk", 0.08f, false, Condition(AnimatorConditionMode.If, "Walking"));
        ConfigureStateTransition(machine, "Walk", "Idle", 0.08f, false, Condition(AnimatorConditionMode.IfNot, "Walking"));
        ConfigureStateTransition(machine, "Idle", "Fly", 0.08f, false, Condition(AnimatorConditionMode.If, "IsFlying"));
        ConfigureStateTransition(machine, "Walk", "Fly", 0.08f, false, Condition(AnimatorConditionMode.If, "IsFlying"));
        ConfigureStateTransition(machine, "Fly", "Idle", 0.10f, false, Condition(AnimatorConditionMode.IfNot, "IsFlying"));

        ConfigureAnyTransition(machine, "BasicAttack", 0.05f, Condition(AnimatorConditionMode.If, "BasicAttack"), Condition(AnimatorConditionMode.If, "BattleActive"));
        ConfigureAnyTransition(machine, "ComboAttack", 0.05f, Condition(AnimatorConditionMode.If, "ComboAttack"), Condition(AnimatorConditionMode.If, "BattleActive"));
        ConfigureAnyTransition(machine, "Scream", 0.08f, Condition(AnimatorConditionMode.If, "Sonic"), Condition(AnimatorConditionMode.If, "BattleActive"));
        ConfigureAnyTransition(machine, "Spell", 0.08f, Condition(AnimatorConditionMode.If, "Fangs"), Condition(AnimatorConditionMode.If, "BattleActive"));
        ConfigureAnyTransition(machine, "Fly", 0.08f, Condition(AnimatorConditionMode.If, "Dive"), Condition(AnimatorConditionMode.If, "BattleActive"));
        ConfigureAnyTransition(machine, "Fly", 0.08f, Condition(AnimatorConditionMode.If, "Sweep"), Condition(AnimatorConditionMode.If, "BattleActive"));
        ConfigureAnyTransition(machine, "Dead", 0.10f, Condition(AnimatorConditionMode.If, "Die"));
        ConfigureAnyTransition(machine, "Idle", 0.08f, Condition(AnimatorConditionMode.If, "IdleReset"));
        ConfigureAnyTransition(machine, "SuperSonic", 0.08f, Condition(AnimatorConditionMode.If, "SuperSonic"), Condition(AnimatorConditionMode.If, "BattleActive"));

        ConfigureStateTransition(machine, "BasicAttack", "Idle", 0.08f, true, 0.92f);
        ConfigureStateTransition(machine, "ComboAttack", "Idle", 0.08f, true, 0.95f);
        ConfigureStateTransition(machine, "Scream", "Idle", 0.08f, true, 0.90f);
        ConfigureStateTransition(machine, "Spell", "Idle", 0.08f, true, 0.90f);
        ConfigureStateTransition(machine, "SuperSonic", "Idle", 0.08f, false, Condition(AnimatorConditionMode.If, "IdleReset"));

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[CamazotzAnimator] Reglas aplicadas. Ejecuta Validate para revisar el resultado.", controller);
    }

    private static AnimatorController LoadController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            Debug.LogError($"No se encontro el Animator Controller en {ControllerPath}.");
        return controller;
    }

    private static void ValidateParameters(AnimatorController controller, List<string> report)
    {
        var parameters = new Dictionary<string, AnimatorControllerParameterType>();
        foreach (AnimatorControllerParameter parameter in controller.parameters)
            parameters[parameter.name] = parameter.type;

        foreach (string required in RequiredParameters)
            if (!parameters.ContainsKey(required)) report.Add($"FALTA parametro: {required}");

        foreach (string boolParameter in BoolParameters)
        {
            if (parameters.TryGetValue(boolParameter, out AnimatorControllerParameterType type) &&
                type != AnimatorControllerParameterType.Bool)
                report.Add($"TIPO INCORRECTO: {boolParameter} debe ser Bool.");
        }

        foreach (string triggerParameter in RequiredParameters.Except(BoolParameters))
        {
            if (parameters.TryGetValue(triggerParameter, out AnimatorControllerParameterType type) &&
                type != AnimatorControllerParameterType.Trigger)
                report.Add($"TIPO INCORRECTO: {triggerParameter} debe ser Trigger.");
        }
    }

    private static void EnsureParameter(AnimatorController controller, string name,
        AnimatorControllerParameterType type)
    {
        if (controller.parameters.Any(parameter => parameter.name == name)) return;
        controller.AddParameter(name, type);
    }

    private static void RemoveLegacyParameters(AnimatorController controller)
    {
        string[] legacy = { "Hurt", "Vulnerable" };
        for (int i = controller.parameters.Length - 1; i >= 0; i--)
        {
            if (Array.IndexOf(legacy, controller.parameters[i].name) >= 0)
                controller.RemoveParameter(i);
        }
    }

    private static bool HasCondition(AnimatorStateTransition transition, string parameter)
    {
        foreach (AnimatorCondition condition in transition.conditions)
            if (condition.parameter == parameter) return true;
        return false;
    }

    private static AnimatorState EnsureSuperSonicState(AnimatorStateMachine machine)
    {
        AnimatorState state = FindState(machine, "SuperSonic");
        if (state != null) return state;

        AnimatorState scream = FindState(machine, "Scream");
        if (scream == null) return null;

        state = machine.AddState("SuperSonic", new Vector3(760f, -300f));
        state.motion = scream.motion;
        state.speed = 0.9f;
        return state;
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
        string destinationName, float duration, bool hasExitTime, AnimatorCondition condition)
    {
        ConfigureStateTransition(machine, sourceName, destinationName, duration, hasExitTime,
            0.0f, new[] { condition });
    }

    private static void ConfigureStateTransition(AnimatorStateMachine machine, string sourceName,
        string destinationName, float duration, bool hasExitTime, float exitTime)
    {
        ConfigureStateTransition(machine, sourceName, destinationName, duration, hasExitTime,
            exitTime, Array.Empty<AnimatorCondition>());
    }

    private static void ConfigureStateTransition(AnimatorStateMachine machine, string sourceName,
        string destinationName, float duration, bool hasExitTime, float exitTime,
        AnimatorCondition[] conditions)
    {
        AnimatorState source = FindState(machine, sourceName);
        AnimatorState destination = FindState(machine, destinationName);
        if (source == null || destination == null) return;

        AnimatorStateTransition transition = source.transitions
            .FirstOrDefault(candidate => candidate.destinationState == destination);
        if (transition == null) transition = source.AddTransition(destination);
        Configure(transition, duration, hasExitTime, conditions);
        if (hasExitTime) transition.exitTime = exitTime;
    }

    private static void Configure(AnimatorStateTransition transition, float duration,
        bool hasExitTime, params AnimatorCondition[] conditions)
    {
        transition.hasExitTime = hasExitTime;
        transition.exitTime = hasExitTime ? Mathf.Max(0.01f, transition.exitTime) : 0.0f;
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
        {
            if (actual[i].mode != expected[i].mode || actual[i].parameter != expected[i].parameter)
                return false;
        }
        return true;
    }
}
#endif

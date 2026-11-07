using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Genera Resources/DaggerShatter.prefab a partir de DaggerFragments.fbx.
// La correspondencia de materiales por submesh se obtuvo comparando los
// triángulos de cada submesh de Unity con los de Blender.
public static class BuildDaggerShatterPrefab
{
    private const string FbxPath = "Assets/Resources/DaggerFragments.fbx";
    private const string PrefabPath = "Assets/Resources/DaggerShatter.prefab";
    private const string SortingLayer = "Default";
    private const int SortingOrder = 11;

    private static readonly Dictionary<string, string[]> FragmentMaterials =
        new Dictionary<string, string[]>
        {
            { "Fragmento_01.001", new[] { "DaggerGold", "DaggerObsidian", "DaggerTerracotta" } },
            { "Fragmento_02.001", new[] { "DaggerObsidian", "DaggerGold" } },
            { "Fragmento_03.001", new[] { "DaggerObsidian", "DaggerGold" } },
            { "Fragmento_04.001", new[] { "DaggerObsidian", "DaggerGold" } },
            { "Fragmento_05.001", new[] { "DaggerObsidian", "DaggerGold" } },
            { "Fragmento_06.001", new[] { "DaggerObsidian", "DaggerGold" } },
            { "Fragmento_07.001", new[] { "DaggerObsidian", "DaggerGold" } },
            { "Fragmento_08.001", new[] { "DaggerGold", "DaggerTerracotta" } },
            { "Fragmento_09.001", new[] { "DaggerGold", "DaggerTerracotta" } },
            { "Fragmento_10.001", new[] { "DaggerGold", "DaggerObsidian", "DaggerTerracotta" } },
            { "Fragmento_11.001", new[] { "DaggerTerracotta", "DaggerGold" } },
            { "Fragmento_12.001", new[] { "DaggerGold" } },
            { "Fragmento_13.001", new[] { "DaggerObsidian", "DaggerGold" } },
            { "Fragmento_14", new[] { "DaggerTerracotta", "DaggerGold" } }
        };

    [MenuItem("Tools/Xibalba/Rebuild Dagger Shatter Prefab")]
    public static void Run()
    {
        AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);

        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null)
            throw new Exception("No se encontró " + FbxPath);

        GameObject instance = UnityEngine.Object.Instantiate(fbx);
        instance.name = "DaggerShatter";

        // La raíz del FBX de fragmentos está sin rotación y cada hijo trae
        // 270° en X. Giramos la raíz 90° en X para dejar el ensamblado en el
        // mismo espacio que la malla intacta (largo X, ancho Y, grosor Z).
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        instance.transform.localScale = Vector3.one;

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length != FragmentMaterials.Count)
        {
            Debug.LogWarning(
                $"[DaggerShatter] Se esperaban {FragmentMaterials.Count} renderers y llegaron {renderers.Length}.");
        }

        var usedNames = new HashSet<string>();
        foreach (Renderer renderer in renderers)
        {
            string objectName = renderer.gameObject.name;
            if (!FragmentMaterials.TryGetValue(objectName, out string[] materialNames))
                throw new Exception("Falta la correspondencia de materiales para " + objectName);

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                throw new Exception("El fragmento " + objectName + " no tiene MeshFilter/mesh.");

            int subMeshes = filter.sharedMesh.subMeshCount;
            if (subMeshes != materialNames.Length)
            {
                throw new Exception(
                    $"El fragmento {objectName} tiene {subMeshes} submeshes pero la tabla define " +
                    $"{materialNames.Length} materiales.");
            }

            Material[] materials = new Material[materialNames.Length];
            for (int i = 0; i < materialNames.Length; i++)
            {
                string path = "Assets/Resources/" + materialNames[i] + ".mat";
                materials[i] = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (materials[i] == null)
                    throw new Exception("No se encontró el material " + path);
            }

            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.sortingLayerName = SortingLayer;
            renderer.sortingOrder = SortingOrder;

            usedNames.Add(objectName);
        }

        foreach (string name in FragmentMaterials.Keys)
        {
            if (!usedNames.Contains(name))
                throw new Exception("El fragmento esperado no existe en el FBX: " + name);
        }

        instance.AddComponent<DaggerShatter>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
        UnityEngine.Object.DestroyImmediate(instance);

        if (prefab == null)
            throw new Exception("No se pudo guardar " + PrefabPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Validación del ensamblado: debe medir lo mismo que la malla intacta
        // en el espacio local (largo 1, ancho ~0.22, grosor ~0.15).
        GameObject check = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Renderer[] checkRenderers = check.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = new Bounds();
        bool hasBounds = false;
        foreach (Renderer renderer in checkRenderers)
        {
            if (hasBounds) bounds.Encapsulate(renderer.bounds);
            else { bounds = renderer.bounds; hasBounds = true; }
        }

        Debug.Log(
            $"[DaggerShatter] Prefab creado en {PrefabPath}. " +
            $"Renderers={checkRenderers.Length}, bounds center={bounds.center}, size={bounds.size}.");

        UnityEngine.Object.DestroyImmediate(check);
    }
}

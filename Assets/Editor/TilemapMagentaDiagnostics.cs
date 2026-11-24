#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

/// <summary>
/// Diagnostica por que los tilemaps se ven magenta y permite asignar un
/// material URP 2D propio. El diagnostico es solo lectura; el arreglo Solo
/// afecta a las escenas abiertas y se puede deshacer con Ctrl+Z.
/// </summary>
public static class TilemapMagentaDiagnostics
{
    private const string MaterialPath = "Assets/Art/Materials/Tilemap_URP_Unlit.mat";
    private const string UnlitShaderName = "Universal Render Pipeline/2D/Sprite-Unlit-Default";

    [MenuItem("Xibalba/Diagnostics/Diagnosticar tilemaps magenta")]
    public static void Diagnose()
    {
        var report = new StringBuilder();
        report.AppendLine("=== DIAGNOSTICO TILEMAPS MAGENTA ===");

        var pipeline = GraphicsSettings.currentRenderPipeline;
        report.AppendLine("Render pipeline (Graphics): " + (pipeline != null ? pipeline.GetType().Name + " / " + pipeline.name : "NINGUNO (Built-in)"));
        var qualityPipeline = QualitySettings.renderPipeline;
        report.AppendLine("Render pipeline (Quality):  " + (qualityPipeline != null ? qualityPipeline.GetType().Name + " / " + qualityPipeline.name : "NINGUNO (Built-in)"));

        report.AppendLine("Shader Sprites/Default (Built-in): " + Found("Sprites/Default"));
        report.AppendLine("Shader URP 2D Sprite-Unlit-Default: " + Found(UnlitShaderName));
        report.AppendLine("Shader URP 2D Sprite-Lit-Default:   " + Found("Universal Render Pipeline/2D/Sprite-Lit-Default"));
        report.AppendLine("Shader error interno: " + Found("Hidden/InternalErrorShader"));

        var renderers = UnityEngine.Object.FindObjectsByType<TilemapRenderer>(
            FindObjectsInactive.Include);
        report.AppendLine("TilemapRenderers en escenas abiertas: " + renderers.Length);

        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            report.AppendLine();
            report.AppendLine("--- TilemapRenderer " + (i + 1) + ": " + renderer.gameObject.name + " ---");

            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                report.AppendLine("  Material: NULO");
            }
            else
            {
                for (int m = 0; m < materials.Length; m++)
                {
                    var mat = materials[m];
                    if (mat == null)
                    {
                        report.AppendLine("  Material[" + m + "]: NULO");
                        continue;
                    }
                    var shader = mat.shader;
                    report.AppendLine("  Material[" + m + "]: " + mat.name +
                                      "  shader=" + (shader != null ? shader.name : "NULO") +
                                      "  isSupported=" + (shader != null && shader.isSupported) +
                                      "  errorShader=" + (shader != null && shader.name == "Hidden/InternalErrorShader") +
                                      "  assetPath=" + AssetDatabase.GetAssetPath(mat));
                    if (shader != null && ShaderUtil.ShaderHasError(shader))
                        report.AppendLine("    !! ShaderUtil.ShaderHasError = TRUE");
                }
            }

            report.AppendLine("  Capa: " + LayerMask.LayerToName(renderer.gameObject.layer) +
                              "  sortingLayer=" + renderer.sortingLayerName +
                              "  modo=" + renderer.mode);

            var tilemap = renderer.GetComponent<Tilemap>();
            if (tilemap == null)
            {
                report.AppendLine("  Tilemap: NULO");
                continue;
            }

            int inspected = 0;
            var bounds = tilemap.cellBounds;
            foreach (var cell in bounds.allPositionsWithin)
            {
                var tile = tilemap.GetTile<Tile>(cell);
                if (tile == null) continue;

                var sprite = tile.sprite;
                report.AppendLine("  Tile de muestra en " + cell + ": " + tile.name +
                                  "  sprite=" + (sprite != null ? sprite.name : "NULO"));
                if (sprite != null)
                {
                    var texture = sprite.texture;
                    report.AppendLine("    textura=" + (texture != null ? texture.name : "NULO") +
                                      "  rect=" + sprite.rect +
                                      "  path=" + AssetDatabase.GetAssetPath(texture) +
                                      "  isReadable=" + (texture != null && texture.isReadable));
                }
                inspected++;
                if (inspected >= 2) break;
            }
            if (inspected == 0) report.AppendLine("  Sin tiles pintados en este Tilemap.");
        }

        Debug.Log(report.ToString());
    }

    [MenuItem("Xibalba/Diagnostics/Arreglar tilemaps (escenas abiertas)")]
    public static void FixOpenScenes()
    {
        var shader = Shader.Find(UnlitShaderName);
        if (shader == null)
        {
            Debug.LogError("No se encontro el shader '" + UnlitShaderName + "'. Revisa que URP este instalado.");
            return;
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            var folder = "Assets/Art/Materials";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/Art", "Materials");

            material = new Material(shader) { name = "Tilemap_URP_Unlit" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Creado " + MaterialPath + " con shader " + shader.name);
        }
        else
        {
            material.shader = shader;
            EditorUtility.SetDirty(material);
        }

        var renderers = UnityEngine.Object.FindObjectsByType<TilemapRenderer>(
            FindObjectsInactive.Include);

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        int changed = 0;

        foreach (var renderer in renderers)
        {
            var materials = renderer.sharedMaterials;
            bool needsFix = materials == null || materials.Length == 0;
            if (!needsFix)
            {
                foreach (var mat in materials)
                {
                    if (mat == null || mat.shader == null ||
                        mat.shader.name == "Hidden/InternalErrorShader" ||
                        !mat.shader.name.StartsWith("Universal Render Pipeline", StringComparison.Ordinal))
                    {
                        needsFix = true;
                        break;
                    }
                }
            }
            if (!needsFix) continue;

            Undo.RecordObject(renderer, "Asignar material URP 2D al tilemap");
            var replacement = new Material[Math.Max(1, materials == null ? 0 : materials.Length)];
            for (int i = 0; i < replacement.Length; i++) replacement[i] = material;
            renderer.sharedMaterials = replacement;
            EditorUtility.SetDirty(renderer);
            changed++;
        }

        Undo.CollapseUndoOperations(group);
        Debug.Log("Tilemaps actualizados: " + changed + " (de " + renderers.Length + "). " +
                  "Guarda las escenas para conservar el cambio.");
    }

    private static string Found(string shaderName)
    {
        return Shader.Find(shaderName) != null ? "SI" : "NO";
    }
}
#endif

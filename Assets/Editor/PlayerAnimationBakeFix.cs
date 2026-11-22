using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Configura los clips del jugador para que el root motion no pueda desplazar
/// al Player: Bake Into Pose en rotacion, Y y XZ, y Loop Time solo en Idle/Run.
/// Usa la API del importador (no reescribe .meta a mano) para no romper los
/// internalID de los clips autogenerados de Dead.fbx y Trhow.fbx.
/// </summary>
public static class PlayerAnimationBakeFix
{
    private const string AnimationsFolder = "Assets/Art/Player/Animations";

    private static readonly string[] Files =
    {
        "Idle.fbx", "Run.fbx", "Jump.fbx", "Attack.fbx", "Dead.fbx", "Trhow.fbx"
    };

    [MenuItem("Herramientas/Xibalba/Configurar clips del jugador (Bake Into Pose)")]
    public static void Apply()
    {
        int changed = 0;

        foreach (string file in Files)
        {
            string path = AnimationsFolder + "/" + file;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning("[PlayerAnimationBakeFix] No se encontro importador para " + path);
                continue;
            }

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;

            if (clips == null || clips.Length == 0)
            {
                Debug.LogWarning("[PlayerAnimationBakeFix] " + file + " no tiene clips.");
                continue;
            }

            bool loop = file == "Idle.fbx" || file == "Run.fbx";
            bool dirty = false;

            for (int i = 0; i < clips.Length; i++)
            {
                ModelImporterClipAnimation c = clips[i];
                if (!c.keepOriginalOrientation ||
                    !c.keepOriginalPositionY ||
                    !c.keepOriginalPositionXZ ||
                    c.loopTime != loop)
                {
                    c.keepOriginalOrientation = true; // Root Transform Rotation -> Bake Into Pose
                    c.keepOriginalPositionY = true;   // Root Transform Position (Y) -> Bake Into Pose
                    c.keepOriginalPositionXZ = true;  // Root Transform Position (XZ) -> Bake Into Pose
                    c.loopTime = loop;                // Solo Idle y Run se repiten
                    dirty = true;
                }
            }

            if (!dirty)
                continue;

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            changed++;
            Debug.Log("[PlayerAnimationBakeFix] Configurado " + file + " (loop=" + loop + ")");
        }

        Debug.Log("[PlayerAnimationBakeFix] Listo. Archivos reimportados: " + changed);
    }
}

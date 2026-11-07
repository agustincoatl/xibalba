using UnityEngine;
using UnityEngine.Rendering;

// Sustituye el sprite del proyectil por el modelo 3D de la daga maya.
// El modelo se carga desde Resources/Dagger3D.fbx y se instancia como hijo del proyectil.
[DisallowMultipleComponent]
public class Dagger3DVisual : MonoBehaviour
{
    private const string ModelResourcePath = "Dagger3D";

    // El FBX importa las submeshes en orden alfabético de material:
    // Mat_Maya_Gold, Mat_Maya_Obsidian, Mat_Maya_Terracota.
    private static readonly string[] MaterialResourcePaths =
    {
        "DaggerGold",
        "DaggerObsidian",
        "DaggerTerracotta"
    };

    [Header("Modelo")]
    [SerializeField] private Vector3 modelOffset = Vector3.zero;
    [SerializeField] private Vector3 modelEuler = Vector3.zero;
    [SerializeField] private float modelScale = 1f;

    [Header("Render")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 10;

    public Vector3 ModelEuler => modelEuler;
    public string SortingLayerName => sortingLayerName;
    public int SortingOrder => sortingOrder;

    private static GameObject modelPrefab;
    private static Material[] modelMaterials;
    private static bool resourcesLoaded;

    private void Awake()
    {
        LoadResources();
        BuildVisual();
    }

    private static void LoadResources()
    {
        // Reintenta si los assets se descargaron o si el domain reload está desactivado.
        if (resourcesLoaded && modelPrefab != null && modelMaterials != null) return;
        resourcesLoaded = true;

        modelPrefab = Resources.Load<GameObject>(ModelResourcePath);

        modelMaterials = new Material[MaterialResourcePaths.Length];
        for (int i = 0; i < MaterialResourcePaths.Length; i++)
            modelMaterials[i] = Resources.Load<Material>(MaterialResourcePaths[i]);

        if (modelPrefab == null)
            Debug.LogError("[Dagger3D] No se encontró Resources/Dagger3D.fbx.");

        for (int i = 0; i < modelMaterials.Length; i++)
        {
            if (modelMaterials[i] == null)
                Debug.LogError($"[Dagger3D] Falta el material Resources/{MaterialResourcePaths[i]}.mat.");
        }
    }

    private void BuildVisual()
    {
        if (modelPrefab == null) return;

        // El holder recibe la orientación del modelo. La raíz del FBX se
        // normaliza porque el importador le añade una rotación de eje redundante.
        GameObject holder = new GameObject("DaggerModel");
        holder.transform.SetParent(transform, false);
        holder.transform.localPosition = modelOffset;
        holder.transform.localRotation = Quaternion.Euler(modelEuler);
        holder.transform.localScale = Vector3.one * modelScale;

        GameObject model = Instantiate(modelPrefab, holder.transform, false);
        model.name = "DaggerMesh";
        // El FBX trae una rotación de eje redundante en su raíz; la malla ya
        // está en el espacio correcto (largo en X, ancho en Y, grosor en Z).
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;

            Material[] current = renderer.sharedMaterials;
            int count = current != null ? current.Length : 0;
            if (count == 0)
                count = modelMaterials.Length;

            Material[] assigned = new Material[count];
            for (int i = 0; i < count; i++)
            {
                if (i < modelMaterials.Length && modelMaterials[i] != null)
                    assigned[i] = modelMaterials[i];
                else if (current != null && i < current.Length)
                    assigned[i] = current[i];
            }
            renderer.sharedMaterials = assigned;

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null &&
                filter.sharedMesh.subMeshCount != count)
            {
                Debug.LogWarning(
                    $"[Dagger3D] El mesh tiene {filter.sharedMesh.subMeshCount} submeshes " +
                    $"pero el renderer usa {count} materiales.");
            }
        }
    }
}

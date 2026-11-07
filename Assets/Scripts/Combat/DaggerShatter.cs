using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Explosión cosmética de la daga al impactar contra el escenario.
// El prefab se genera a partir de Resources/DaggerFragments.fbx y ya trae
// los 14 trozos sólidos con sus materiales asignados por submesh.
[DisallowMultipleComponent]
public class DaggerShatter : MonoBehaviour
{
    private const string PrefabResourcePath = "DaggerShatter";

    [Header("Animación")]
    [SerializeField] private float lifetime = 1.1f;
    [SerializeField] private float minSpeed = 2.5f;
    [SerializeField] private float maxSpeed = 5.5f;
    [SerializeField] private float gravity = 9f;
    [SerializeField] private float minAngularSpeed = -540f;
    [SerializeField] private float maxAngularSpeed = 540f;
    [SerializeField] private float shrinkDuration = 0.25f;

    private static GameObject prefab;
    private static bool warnedMissingPrefab;

    private readonly List<Fragment> fragments = new List<Fragment>();
    private GameObject root;
    private float age;
    private bool playing;

    private sealed class Fragment
    {
        public Transform transform;
        public Vector3 initialScale;
        public Vector3 velocity;
        public float angularSpeed;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        prefab = null;
        warnedMissingPrefab = false;
    }

    // position: centro de la daga en el momento del impacto.
    // rotationZ: rotación del proyectil (dirección del lanzamiento).
    // modelEuler: misma orientación local que usa Dagger3DVisual.
    public static void Play(
        Vector3 position,
        float rotationZ,
        Vector3 modelEuler,
        string sortingLayerName,
        int sortingOrder)
    {
        if (prefab == null)
        {
            prefab = Resources.Load<GameObject>(PrefabResourcePath);
            if (prefab == null)
            {
                if (!warnedMissingPrefab)
                {
                    warnedMissingPrefab = true;
                    Debug.LogError($"[DaggerShatter] No se encontró Resources/{PrefabResourcePath}.prefab.");
                }
                return;
            }
        }

        // Holder en el punto de impacto, orientado como el proyectil.
        GameObject holder = new GameObject("DaggerShatter");
        holder.transform.position = position;
        holder.transform.rotation = Quaternion.Euler(0f, 0f, rotationZ);

        // Misma orientación que el modelo intacto (180° en Z para el visual).
        GameObject visual = new GameObject("DaggerFragments");
        visual.transform.SetParent(holder.transform, false);
        visual.transform.localRotation = Quaternion.Euler(modelEuler);

        GameObject instance = Instantiate(prefab, visual.transform, false);
        instance.name = "DaggerShatterFragments";

        DaggerShatter shatter = instance.GetComponent<DaggerShatter>();
        if (shatter == null)
            shatter = instance.AddComponent<DaggerShatter>();

        shatter.root = holder;
        shatter.Begin(holder.transform.right, sortingLayerName, sortingOrder);

        // Red de seguridad por si el componente se desactiva antes de tiempo.
        Destroy(holder, shatter.lifetime + 0.5f);
    }

    private void Begin(Vector3 throwDirection, string sortingLayerName, int sortingOrder)
    {
        throwDirection.z = 0f;
        if (throwDirection.sqrMagnitude < 0.0001f)
            throwDirection = Vector3.right;
        throwDirection.Normalize();

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;

            Transform fragment = renderer.transform;
            Vector3 outward = fragment.position - transform.position;
            outward.z = 0f;
            if (outward.sqrMagnitude < 0.0001f)
            {
                Vector2 random = Random.insideUnitCircle;
                outward = new Vector3(random.x, random.y, 0f);
            }
            outward.Normalize();

            // Un pequeño sesgo hacia atrás evita que todos los trozos queden
            // enterrados en la pared y hace más legible la explosión.
            Vector3 direction = outward - throwDirection * 0.35f;
            if (direction.sqrMagnitude < 0.0001f)
                direction = outward;
            direction.Normalize();

            fragments.Add(new Fragment
            {
                transform = fragment,
                initialScale = fragment.localScale,
                velocity = direction * Random.Range(minSpeed, maxSpeed) +
                           Vector3.up * Random.Range(-0.5f, 1.2f),
                angularSpeed = Random.Range(minAngularSpeed, maxAngularSpeed)
            });
        }

        age = 0f;
        playing = true;
    }

    private void Update()
    {
        if (!playing) return;

        float dt = Time.deltaTime;
        age += dt;

        float shrinkTime = Mathf.Max(0.01f, shrinkDuration);
        float shrinkStart = Mathf.Max(0f, lifetime - shrinkTime);
        float shrink = age <= shrinkStart
            ? 1f
            : Mathf.Clamp01(1f - (age - shrinkStart) / shrinkTime);

        for (int i = 0; i < fragments.Count; i++)
        {
            Fragment fragment = fragments[i];
            if (fragment.transform == null) continue;

            fragment.velocity += Vector3.down * (gravity * dt);
            fragment.transform.position += fragment.velocity * dt;
            fragment.transform.Rotate(0f, 0f, fragment.angularSpeed * dt, Space.World);
            fragment.transform.localScale = fragment.initialScale * shrink;
        }

        if (age >= lifetime)
        {
            playing = false;
            if (root != null)
                Destroy(root);
            else
                Destroy(gameObject);
        }
    }
}

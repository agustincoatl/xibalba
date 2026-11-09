using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [SerializeField] AudioSource sfxSource;

    [Header("Sonidos")]
    public AudioClip salto;
    public AudioClip danio;
    public AudioClip muerte;
    public AudioClip cambioEscena;
    public AudioClip ataque;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Play(AudioClip clip, float volumen = 1f)
    {
        if (clip != null && sfxSource != null)
            sfxSource.PlayOneShot(clip, volumen);
    }

    // Atajos seguros: no dan error aunque no haya AudioManager en la escena
    public static void PlaySalto() { if (Instance != null) Instance.Play(Instance.salto); }
    public static void PlayDanio() { if (Instance != null) Instance.Play(Instance.danio); }
    public static void PlayMuerte() { if (Instance != null) Instance.Play(Instance.muerte); }
    public static void PlayCambioEscena() { if (Instance != null) Instance.Play(Instance.cambioEscena); }
    public static void PlayAtaque() { if (Instance != null) Instance.Play(Instance.ataque); }
}
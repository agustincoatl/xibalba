using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource musicSource;

    [Header("Sonidos")]
    public AudioClip salto;
    public AudioClip danio;
    public AudioClip muerte;
    public AudioClip cambioEscena;
    public AudioClip ataque;

    [Header("Musica")]
    public AudioClip musicaFondo;
    [Range(0f, 1f)] public float volumenMusica = 0.5f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (sfxSource == null)
            sfxSource = GetComponent<AudioSource>();

        if (sfxSource == null)
            sfxSource = gameObject.AddComponent<AudioSource>();

        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f;

        if (musicSource == null || musicSource == sfxSource)
        {
            AudioSource[] sources = GetComponents<AudioSource>();
            musicSource = null;
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] != sfxSource)
                {
                    musicSource = sources[i];
                    break;
                }
            }
            if (musicSource == null)
                musicSource = gameObject.AddComponent<AudioSource>();
        }

        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        musicSource.volume = volumenMusica;

        if (musicaFondo != null)
        {
            musicSource.clip = musicaFondo;
            musicSource.Play();
        }
    }

    public void Play(AudioClip clip, float volumen = 1f)
    {
        if (clip != null && sfxSource != null)
            sfxSource.PlayOneShot(clip, volumen);
    }

    public void PlayMusic(AudioClip clip, float volumen = -1f)
    {
        if (musicSource == null) return;
        if (clip != null) musicSource.clip = clip;
        musicSource.volume = volumen >= 0f ? volumen : volumenMusica;
        if (musicSource.clip != null && !musicSource.isPlaying)
            musicSource.Play();
    }

    public void StopMusic()
    {
        if (musicSource != null) musicSource.Stop();
    }

    // Atajos seguros: no dan error aunque no haya AudioManager en la escena
    public static void PlaySalto() { if (Instance != null) Instance.Play(Instance.salto); }
    public static void PlayDanio() { if (Instance != null) Instance.Play(Instance.danio); }
    public static void PlayMuerte() { if (Instance != null) Instance.Play(Instance.muerte); }
    public static void PlayCambioEscena() { if (Instance != null) Instance.Play(Instance.cambioEscena); }
    public static void PlayAtaque() { if (Instance != null) Instance.Play(Instance.ataque); }
}

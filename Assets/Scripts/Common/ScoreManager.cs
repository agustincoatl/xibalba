using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private string scoreFormat = "Puntos: {0}";

    public int Score { get; private set; }

    public static int CurrentScore
    {
        get
        {
            if (Instance == null) Bootstrap();
            return Instance != null ? Instance.Score : 0;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        GameObject go = new GameObject("ScoreManager");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ScoreManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (scoreText == null)
            TryBindScoreText();
        UpdateUI();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scoreText == null)
            TryBindScoreText();
        UpdateUI();
    }

    public static void AddPoints(int amount)
    {
        if (Instance == null) Bootstrap();
        if (Instance == null) return;
        Instance.Add(amount);
    }

    public void Add(int amount)
    {
        if (amount == 0) return;
        Score = Mathf.Max(0, Score + amount);
        UpdateUI();
    }

    public void ResetScore()
    {
        Score = 0;
        UpdateUI();
    }

    /// <summary>
    /// Reinicia los puntos desde cualquier script (por ejemplo al reintentar).
    /// </summary>
    public static void ResetAll()
    {
        if (Instance == null) Bootstrap();
        if (Instance != null) Instance.ResetScore();
    }

    public void SetScoreText(TMP_Text text)
    {
        scoreText = text;
        UpdateUI();
    }

    private void TryBindScoreText()
    {
        GameObject go = GameObject.Find("ScoreText");
        if (go != null)
            scoreText = go.GetComponent<TMP_Text>();
    }

    private void UpdateUI()
    {
        if (scoreText != null)
            scoreText.text = string.Format(scoreFormat, Score);
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Grava cada partida (run) do aluno logado no backend do SAVIT:
/// - acumula uma pontuação por componente: 100 pontos, -10 por erro (mínimo 0);
/// - envia a run e todas as pontuações para POST /runs no fim da partida.
/// Erro = soltar a peça fora do lugar depois de segurá-la por pelo menos minHoldForError (AssemblyEvents).
/// Criado sozinho na cena do jogo; o ranking é calculado pelo backend a partir das pontuações.
/// </summary>
public class RunRecorder : MonoBehaviour
{
    public const int PointsPerStage = 100;
    public const int PenaltyPerError = 10;
    public static readonly string[] Stages =
    {
        AssemblyEvents.Ram, AssemblyEvents.Motherboard, AssemblyEvents.PowerSupply, AssemblyEvents.Atx24, AssemblyEvents.HardDrive,
    };

    [Tooltip("Soltar a peça antes desse tempo não conta como erro (tremida do rastreio da mão).")]
    public float minHoldForError = 0.5f;

#pragma warning disable 0649 // preenchidos pelo JsonUtility
    [Serializable] private class RunData
    {
        public string started_at;
        public string finished_at;
        public string status;
        public int total_points;
        public int total_errors;
        public string app_version;
    }

    [Serializable] private class ScoreInsert { public string stage; public int points; public int errors; public float duration_seconds; }
    [Serializable] private class CreateRunBody
    {
        public RunData runData;
        public ScoreInsert[] scoresData;
    }
#pragma warning restore 0649

    private static bool hooked;

    private bool finished;
    private bool submitted;
    private DateTime startedAtUtc;
    private float stageStart;
    private TutorialManager tutorial;
    private readonly Dictionary<string, int> errors = new Dictionary<string, int>();
    private readonly HashSet<string> completed = new HashSet<string>();
    private readonly List<ScoreInsert> scores = new List<ScoreInsert>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        if (hooked) return;
        hooked = true;
        SceneManager.sceneLoaded += (scene, mode) => TryCreate();
        TryCreate();
    }

    // Só na cena do jogo (a que tem o tutorial) e com aluno logado.
    private static void TryCreate()
    {
        if (!AuthSession.IsLoggedIn) return;
        if (FindFirstObjectByType<TutorialManager>() == null) return;
        if (FindFirstObjectByType<RunRecorder>() != null) return;
        new GameObject("RunRecorder").AddComponent<RunRecorder>();
    }

    private void OnEnable()
    {
        AssemblyEvents.Missed += OnMissed;
        AssemblyEvents.Completed += OnCompleted;
    }

    private void OnDisable()
    {
        AssemblyEvents.Missed -= OnMissed;
        AssemblyEvents.Completed -= OnCompleted;
    }

    private void Start()
    {
        tutorial = FindFirstObjectByType<TutorialManager>();
        startedAtUtc = DateTime.UtcNow;
        stageStart = Time.time;
    }

    private void Update()
    {
        // O tempo da etapa conta a partir do "Entendi" (explicação do tutorial fechada).
        if (TutorialGate.InputLocked) stageStart = Time.time;

        bool tutorialEnded = tutorial != null && !tutorial.IsRunning && tutorial.CurrentStepIndex >= tutorial.steps.Length;
        if (!finished && (tutorialEnded || completed.Count >= Stages.Length))
            Finish("finished");
    }

    private void OnDestroy()
    {
        // Saiu antes de terminar: envia a run parcial como abandonada.
        if (!submitted)
            SubmitRun("abandoned", true);
    }

    private void OnMissed(string stage, float heldSeconds)
    {
        if (finished || completed.Contains(stage) || heldSeconds < minHoldForError) return;
        errors[stage] = ErrorsOf(stage) + 1;
        Debug.Log($"[RunRecorder] Erro na etapa '{stage}' (total {ErrorsOf(stage)}).");
    }

    private void OnCompleted(string stage)
    {
        if (finished || !completed.Add(stage)) return;

        int e = ErrorsOf(stage);
        var score = new ScoreInsert
        {
            stage = stage,
            errors = e,
            points = Mathf.Max(0, PointsPerStage - PenaltyPerError * e),
            duration_seconds = Mathf.Round((Time.time - stageStart) * 100f) / 100f,
        };
        stageStart = Time.time;
        scores.Add(score);
        Debug.Log($"[RunRecorder] Etapa '{stage}': {score.points} pontos ({e} erros, {score.duration_seconds}s).");
    }

    private void Finish(string status)
    {
        finished = true;
        SubmitRun(status, false);
    }

    private void SubmitRun(string status, bool fireAndForget)
    {
        if (submitted) return;
        submitted = true;

        int totalPoints = 0;
        int totalErrors = 0;
        foreach (var score in scores)
        {
            totalPoints += score.points;
            totalErrors += score.errors;
        }

        var body = new CreateRunBody
        {
            runData = new RunData
            {
                started_at = startedAtUtc.ToString("o"),
                finished_at = DateTime.UtcNow.ToString("o"),
                status = status,
                total_points = totalPoints,
                total_errors = totalErrors,
                app_version = Application.version,
            },
            scoresData = scores.ToArray(),
        };
        string json = JsonUtility.ToJson(body);

        if (fireAndForget)
        {
            SavitApi.FireAndForget("POST", "/runs", json, true);
            return;
        }

        StartCoroutine(SavitApi.Send("POST", "/runs", json, true, (httpStatus, _) =>
        {
            if (SavitApi.IsSuccess(httpStatus))
                Debug.Log($"[RunRecorder] Run {status} salva com {totalPoints} pontos e {totalErrors} erros.");
            else
                Debug.LogWarning("[RunRecorder] Não foi possível salvar a run e suas pontuações.");
        }));
    }

    private int ErrorsOf(string stage) => errors.TryGetValue(stage, out int n) ? n : 0;
}

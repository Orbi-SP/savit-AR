using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Grava cada partida (run) do aluno logado no backend do SAVIT:
/// - abre uma linha em runs quando a cena do jogo carrega;
/// - a cada componente encaixado grava uma linha em scores ligada à run: 100 pontos, -10 por erro (mínimo 0);
/// - fecha a run como "concluida" no fim do tutorial, ou "abandonada" se sair antes.
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
    [Serializable] private class RunInsert { public string app_version; }
    [Serializable] private class RunRow { public long id; }
    [Serializable] private class RunRows { public RunRow[] items; }
    [Serializable] private class RunResponse { public long id; public long runId; public RunRow run; public RunRow data; }
    [Serializable] private class ScoreInsert { public long run_id; public string stage; public int points; public int errors; public float duration_seconds; }
    [Serializable] private class RunFinish { public string status; public string finished_at; }
#pragma warning restore 0649

    private static bool hooked;

    private long runId;              // 0 = run ainda não criada no banco
    private bool finished;
    private string pendingFinish;    // status a gravar assim que a run existir
    private float stageStart;
    private TutorialManager tutorial;
    private readonly Dictionary<string, int> errors = new Dictionary<string, int>();
    private readonly HashSet<string> completed = new HashSet<string>();
    private readonly List<ScoreInsert> pendingScores = new List<ScoreInsert>();

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
        stageStart = Time.time;

        string body = JsonUtility.ToJson(new RunInsert { app_version = Application.version });
        StartCoroutine(SavitApi.Send("POST", "/runs", body, true, (status, json) =>
        {
            RunRows rows = null;
            RunResponse response = null;
            if (SavitApi.IsSuccess(status))
            {
                try
                {
                    if ((json ?? "").TrimStart().StartsWith("["))
                        rows = JsonUtility.FromJson<RunRows>("{\"items\":" + json + "}");
                    else
                        response = JsonUtility.FromJson<RunResponse>(json);
                }
                catch (Exception) { }
            }
            RunRow createdRun = rows?.items != null && rows.items.Length > 0
                ? rows.items[0]
                : response?.run ?? response?.data;
            if (createdRun == null && response != null && response.id != 0)
                createdRun = new RunRow { id = response.id };
            if (createdRun == null && response != null && response.runId != 0)
                createdRun = new RunRow { id = response.runId };
            if (createdRun == null || createdRun.id == 0)
            {
                Debug.LogWarning("[RunRecorder] Não foi possível abrir a run; a pontuação desta partida não será salva.");
                return;
            }

            runId = createdRun.id;
            Debug.Log($"[RunRecorder] Run {runId} iniciada.");
            Flush();
        }));
    }

    private void Update()
    {
        // O tempo da etapa conta a partir do "Entendi" (explicação do tutorial fechada).
        if (TutorialGate.InputLocked) stageStart = Time.time;

        bool tutorialEnded = tutorial != null && !tutorial.IsRunning && tutorial.CurrentStepIndex >= tutorial.steps.Length;
        if (!finished && (tutorialEnded || completed.Count >= Stages.Length))
            Finish("concluida");
    }

    private void OnDestroy()
    {
        // Saiu antes de terminar: marca como abandonada (sem esperar resposta, a cena está fechando).
        if (!finished && runId != 0)
            SavitApi.FireAndForget("PATCH", $"/runs/{runId}", FinishBody("abandonada"), true);
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
        pendingScores.Add(score);
        Debug.Log($"[RunRecorder] Etapa '{stage}': {score.points} pontos ({e} erros, {score.duration_seconds}s).");
        Flush();
    }

    private void Finish(string status)
    {
        finished = true;
        pendingFinish = status;
        Flush();
    }

    // Envia o que estiver pendente assim que a run existir no banco.
    private void Flush()
    {
        if (runId == 0) return;

        foreach (var score in pendingScores)
        {
            score.run_id = runId;
            StartCoroutine(SavitApi.Send("POST", $"/runs/{runId}/scores", JsonUtility.ToJson(score), true, null));
        }
        pendingScores.Clear();

        if (pendingFinish != null)
        {
            StartCoroutine(SavitApi.Send("PATCH", $"/runs/{runId}", FinishBody(pendingFinish), true, null));
            Debug.Log($"[RunRecorder] Run {runId} {pendingFinish}.");
            pendingFinish = null;
        }
    }

    private int ErrorsOf(string stage) => errors.TryGetValue(stage, out int n) ? n : 0;

    private static string FinishBody(string status) =>
        JsonUtility.ToJson(new RunFinish { status = status, finished_at = DateTime.UtcNow.ToString("o") });
}

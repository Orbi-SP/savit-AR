using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Monta a tela de ranking sobre o menu sem exigir alterações na cena.
/// </summary>
public class RankingController : MonoBehaviour
{
    [Serializable]
    private class GlobalEntry
    {
        public string user_id;
        public string username;
        public int total_points;
        public long time_spent_ms;
    }

    [Serializable]
    private class UserEntry
    {
        public string username;
        public int total_points;
        public string started_at;
        public string finished_at;
    }

    [Serializable]
    private class GlobalResponse { public GlobalEntry[] items; }
    [Serializable]
    private class UserResponse { public UserEntry[] items; }

    private Canvas canvas;
    private GameObject overlay;
    private Button rankingButton;
    private TMP_Text titleText;
    private TMP_Text statusText;
    private RectTransform listContent;
    private Button globalButton;
    private Button personalButton;
    private bool loading;
    private bool? lastLoggedIn;

    private void Start()
    {
        canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[RankingController] Nenhum Canvas no menu para montar o ranking.");
            enabled = false;
            return;
        }

        BuildUi();
        UpdateAccess();
    }

    private void Update()
    {
        UpdateAccess();
    }

    public void Open()
    {
        if (!AuthSession.IsLoggedIn)
        {
            Debug.LogWarning("[RankingController] Faça login antes de abrir o ranking.");
            return;
        }

        overlay.SetActive(true);
        LoadGlobal();
    }

    private void Close()
    {
        overlay.SetActive(false);
    }

    private void UpdateAccess()
    {
        bool loggedIn = AuthSession.IsLoggedIn;
        if (loggedIn == lastLoggedIn) return;
        lastLoggedIn = loggedIn;
        if (rankingButton != null) rankingButton.gameObject.SetActive(loggedIn);
        if (!loggedIn && overlay != null) overlay.SetActive(false);
    }

    private void LoadGlobal()
    {
        if (loading) return;
        SetTab(globalButton, personalButton);
        StartCoroutine(LoadGlobalCoroutine());
    }

    private IEnumerator LoadGlobalCoroutine()
    {
        loading = true;
        SetStatus("Carregando ranking global...");
        ClearRows();

        long status = 0;
        string json = null;
        yield return SavitApi.GetRanking((httpStatus, body) =>
        {
            status = httpStatus;
            json = body;
        });

        loading = false;
        if (!SavitApi.IsSuccess(status))
        {
            SetStatus("Não foi possível carregar o ranking global.");
            yield break;
        }

        GlobalEntry[] entries;
        if (!TryParseArray(json, out GlobalResponse response) || response.items == null)
        {
            SetStatus("Resposta inválida do ranking global.");
            Debug.LogError("[RankingController] A API devolveu um ranking global inválido.");
            yield break;
        }

        entries = BestRunPerUser(response.items);
        titleText.text = "Ranking Global";
        SetStatus(entries.Length == 0 ? "Nenhuma partida encontrada." : "");
        for (int i = 0; i < entries.Length; i++)
            AddRow($"{i + 1}º", entries[i].username, entries[i].total_points, FormatMilliseconds(entries[i].time_spent_ms));
    }

    private static GlobalEntry[] BestRunPerUser(GlobalEntry[] entries)
    {
        var bestByUser = new Dictionary<string, GlobalEntry>();
        foreach (GlobalEntry entry in entries)
        {
            if (string.IsNullOrEmpty(entry.user_id))
            {
                Debug.LogWarning("[RankingController] Run global ignorada porque não possui user_id.");
                continue;
            }

            if (!bestByUser.TryGetValue(entry.user_id, out GlobalEntry currentBest) || IsBetterRun(entry, currentBest))
                bestByUser[entry.user_id] = entry;
        }

        var bestRuns = new List<GlobalEntry>(bestByUser.Values);
        bestRuns.Sort((left, right) =>
        {
            int pointsComparison = right.total_points.CompareTo(left.total_points);
            return pointsComparison != 0 ? pointsComparison : left.time_spent_ms.CompareTo(right.time_spent_ms);
        });
        return bestRuns.ToArray();
    }

    private static bool IsBetterRun(GlobalEntry candidate, GlobalEntry currentBest)
    {
        if (candidate.total_points != currentBest.total_points)
            return candidate.total_points > currentBest.total_points;
        return candidate.time_spent_ms < currentBest.time_spent_ms;
    }

    private void LoadPersonal()
    {
        if (loading) return;
        if (!AuthSession.IsLoggedIn) return;
        SetTab(personalButton, globalButton);
        StartCoroutine(LoadPersonalCoroutine());
    }

    private IEnumerator LoadPersonalCoroutine()
    {
        loading = true;
        SetStatus("Carregando meu ranking...");
        ClearRows();

        long status = 0;
        string json = null;
        yield return SavitApi.GetUserRanking(AuthSession.UserId, (httpStatus, body) =>
        {
            status = httpStatus;
            json = body;
        });

        loading = false;
        if (!SavitApi.IsSuccess(status))
        {
            SetStatus("Não foi possível carregar seu ranking.");
            yield break;
        }

        if (!TryParseArray(json, out UserResponse response) || response.items == null)
        {
            SetStatus("Resposta inválida do seu ranking.");
            Debug.LogError("[RankingController] A API devolveu um ranking pessoal inválido.");
            yield break;
        }

        Array.Sort(response.items, (left, right) =>
        {
            bool leftValid = DateTimeOffset.TryParse(left.started_at, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTimeOffset leftDate);
            bool rightValid = DateTimeOffset.TryParse(right.started_at, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTimeOffset rightDate);
            if (leftValid != rightValid) return rightValid.CompareTo(leftValid);
            if (!leftValid) return 0;
            return rightDate.CompareTo(leftDate);
        });

        titleText.text = "Meu Ranking";
        SetStatus(response.items.Length == 0 ? "Nenhuma partida encontrada." : "");
        for (int i = 0; i < response.items.Length; i++)
        {
            UserEntry entry = response.items[i];
            AddRow("", string.IsNullOrEmpty(entry.username) ? AuthSession.DisplayName : entry.username,
                entry.total_points, FormatDateRange(entry.started_at, entry.finished_at));
        }
    }

    private void BuildUi()
    {
        var resources = new TMP_DefaultControls.Resources();
        rankingButton = ButtonWithText(canvas.transform, resources, "Ranking",
            new Vector2(0.02f, 0.88f), new Vector2(0.22f, 0.97f), Open);

        overlay = Panel("RankingOverlay", canvas.transform, Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.86f));
        var card = Panel("RankingCard", overlay.transform, new Vector2(0.05f, 0.06f), new Vector2(0.95f, 0.94f),
            new Color(0.12f, 0.12f, 0.15f, 0.98f));

        ButtonWithText(card.transform, resources, "Voltar ao Menu",
            new Vector2(0.04f, 0.90f), new Vector2(0.30f, 0.98f), Close);
        globalButton = ButtonWithText(card.transform, resources, "Ranking Global",
            new Vector2(0.34f, 0.90f), new Vector2(0.63f, 0.98f), LoadGlobal);
        personalButton = ButtonWithText(card.transform, resources, "Meu Ranking",
            new Vector2(0.67f, 0.90f), new Vector2(0.96f, 0.98f), LoadPersonal);

        titleText = Label(card.transform, "Ranking Global", new Vector2(0.05f, 0.82f), new Vector2(0.95f, 0.89f), 18f, 34f, FontStyles.Bold);
        statusText = Label(card.transform, "", new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.16f), 12f, 22f, FontStyles.Normal);

        var scrollObject = new GameObject("RankingScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollObject.transform.SetParent(card.transform, false);
        Stretch(scrollObject.GetComponent<RectTransform>(), new Vector2(0.05f, 0.17f), new Vector2(0.95f, 0.80f), Vector2.zero);
        scrollObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.2f);

        var contentObject = new GameObject("RankingItems", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.transform.SetParent(scrollObject.transform, false);
        listContent = contentObject.GetComponent<RectTransform>();
        Stretch(listContent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(8f, 8f));
        listContent.pivot = new Vector2(0.5f, 1f);
        var layout = contentObject.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = contentObject.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = scrollObject.GetComponent<ScrollRect>();
        scroll.content = listContent;
        scroll.horizontal = false;
        scroll.vertical = true;

        overlay.SetActive(false);
        overlay.transform.SetAsLastSibling();
    }

    private void AddRow(string position, string username, int points, string time)
    {
        var row = new GameObject("RankingRow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        row.transform.SetParent(listContent, false);
        row.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
        row.GetComponent<LayoutElement>().minHeight = 46f;
        var text = Label(row.transform, $"{position}  {username}\n{points} pontos  -  {time}",
            Vector2.zero, Vector2.one, 12f, 24f, FontStyles.Normal);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.margin = new Vector4(12f, 3f, 8f, 3f);
    }

    private void ClearRows()
    {
        for (int i = listContent.childCount - 1; i >= 0; i--)
            Destroy(listContent.GetChild(i).gameObject);
    }

    private void SetStatus(string text)
    {
        statusText.text = text;
    }

    private static void SetTab(Button selected, Button other)
    {
        selected.interactable = false;
        other.interactable = true;
    }

    private static string FormatMilliseconds(long milliseconds)
    {
        return FormatTime(TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)));
    }

    private static string FormatDateRange(string startedAt, string finishedAt)
    {
        if (!DateTimeOffset.TryParse(startedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset start) ||
            !DateTimeOffset.TryParse(finishedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset finish))
            return "tempo indisponível";
        return FormatTime(finish - start);
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return $"{(int)time.TotalMinutes}m {time.Seconds}seg";
    }

    private static bool TryParseArray<T>(string json, out T response)
    {
        response = default;
        if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '[') return false;
        try
        {
            response = JsonUtility.FromJson<T>("{\"items\":" + json + "}");
            return response != null;
        }
        catch (Exception exception)
        {
            Debug.LogError("[RankingController] Falha ao interpretar resposta da API: " + exception.Message);
            return false;
        }
    }

    private static GameObject Panel(string name, Transform parent, Vector2 min, Vector2 max, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>(), min, max, Vector2.zero);
        go.GetComponent<Image>().color = color;
        return go;
    }

    private static TMP_Text Label(Transform parent, string text, Vector2 min, Vector2 max,
        float minSize, float maxSize, FontStyles style)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>(), min, max, Vector2.zero);
        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontStyle = style;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = minSize;
        label.fontSizeMax = maxSize;
        label.raycastTarget = false;
        return label;
    }

    private static Button ButtonWithText(Transform parent, TMP_DefaultControls.Resources resources, string text,
        Vector2 min, Vector2 max, UnityEngine.Events.UnityAction onClick)
    {
        var go = TMP_DefaultControls.CreateButton(resources);
        go.name = "Button" + text.Replace(" ", "");
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>(), min, max, Vector2.zero);
        var label = go.GetComponentInChildren<TMP_Text>(true);
        label.text = text;
        label.enableAutoSizing = true;
        label.fontSizeMin = 11f;
        label.fontSizeMax = 24f;
        Stretch(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(4f, 2f));
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(onClick);
        return button;
    }

    private static void Stretch(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 padding)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = padding;
        rt.offsetMax = -padding;
    }
}

using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tela de login do menu (e-mail + senha no Supabase). Montada por código por cima do Canvas do menu,
/// sem mexer nos objetos da cena: enquanto ninguém entra, o "Iniciar Jogo" fica escondido.
/// As contas são criadas no site do SAVIT (não há cadastro no app). Adicionada pelo MenuController.
/// </summary>
public class LoginController : MonoBehaviour
{
    [Tooltip("Botão do menu que só aparece depois do login.")]
    public string startButtonName = "ButtonStart";

    private GameObject startButton;
    private GameObject overlay;
    private GameObject loggedBar;
    private TMP_InputField emailInput;
    private TMP_InputField passwordInput;
    private TMP_Text statusText;
    private TMP_Text greetingText;
    private Button signInButton;
    private bool busy;

    private void Start()
    {
        var canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[LoginController] Nenhum Canvas no menu para montar a tela de login.");
            return;
        }

        startButton = GameObject.Find(startButtonName);
        BuildUi(canvas.transform);
        ShowLoggedIn(false);

        // Sessão salva no aparelho: entra direto.
        SetBusy(true, "Entrando...");
        StartCoroutine(SupabaseAuth.TryRestore(ok =>
        {
            SetBusy(false, ok ? null : "");
            ShowLoggedIn(ok);
        }));
    }

    public void SignIn()
    {
        if (busy) return;
        SetBusy(true, "Entrando...");
        StartCoroutine(SupabaseAuth.SignIn(emailInput.text, passwordInput.text, OnAuthResult));
    }

    public void SignOut()
    {
        if (busy) return;
        StartCoroutine(SupabaseAuth.SignOut(() =>
        {
            passwordInput.text = "";
            SetBusy(false, "");
            ShowLoggedIn(false);
        }));
    }

    private void OnAuthResult(bool ok, string error)
    {
        SetBusy(false, ok ? null : error);
        if (ok) passwordInput.text = "";
        ShowLoggedIn(ok);
    }

    private void ShowLoggedIn(bool loggedIn)
    {
        if (overlay != null) overlay.SetActive(!loggedIn);
        if (loggedBar != null) loggedBar.SetActive(loggedIn);
        if (startButton != null) startButton.SetActive(loggedIn);
        if (loggedIn && greetingText != null) greetingText.text = $"Olá, <b>{AuthSession.Email}</b>";
    }

    private void SetBusy(bool value, string status)
    {
        busy = value;
        if (signInButton != null) signInButton.interactable = !value;
        if (statusText != null && status != null)
        {
            statusText.text = status;
            statusText.color = value ? new Color(0.85f, 0.85f, 0.85f) : new Color(1f, 0.55f, 0.45f);
        }
    }

    // ── Montagem da tela (âncoras proporcionais: funciona em qualquer resolução, celular deitado) ──

    private void BuildUi(Transform canvas)
    {
        var resources = new TMP_DefaultControls.Resources();

        overlay = Panel("LoginOverlay", canvas, Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.85f));
        var card = Panel("LoginCard", overlay.transform, new Vector2(0.28f, 0.08f), new Vector2(0.72f, 0.92f), new Color(0.12f, 0.12f, 0.15f, 0.97f));

        Label(card.transform, "Entrar no SAVIT", new Vector2(0.05f, 0.84f), new Vector2(0.95f, 0.97f), 30f, 64f, FontStyles.Bold);
        Label(card.transform, "Use o e-mail e a senha cadastrados no site do SAVIT.", new Vector2(0.05f, 0.74f), new Vector2(0.95f, 0.84f), 16f, 34f, FontStyles.Normal);

        emailInput = CreateInput(card.transform, resources, "E-mail", new Vector2(0.08f, 0.58f), new Vector2(0.92f, 0.71f), false);
        passwordInput = CreateInput(card.transform, resources, "Senha", new Vector2(0.08f, 0.43f), new Vector2(0.92f, 0.56f), true);
        passwordInput.onSubmit.AddListener(_ => SignIn());

        statusText = Label(card.transform, "", new Vector2(0.05f, 0.31f), new Vector2(0.95f, 0.42f), 14f, 30f, FontStyles.Normal);

        signInButton = ButtonWithText(card.transform, resources, "Entrar", new Vector2(0.20f, 0.15f), new Vector2(0.80f, 0.28f), SignIn);
        ButtonWithText(card.transform, resources, "Sair do jogo", new Vector2(0.32f, 0.03f), new Vector2(0.68f, 0.12f), Application.Quit);

        loggedBar = Panel("LoggedBar", canvas, new Vector2(0.60f, 0.88f), new Vector2(0.99f, 0.98f), new Color(0f, 0f, 0f, 0.55f));
        greetingText = Label(loggedBar.transform, "", new Vector2(0.03f, 0f), new Vector2(0.55f, 1f), 14f, 34f, FontStyles.Normal);
        ButtonWithText(loggedBar.transform, resources, "Trocar usuário", new Vector2(0.58f, 0.12f), new Vector2(0.98f, 0.88f), SignOut);

        overlay.transform.SetAsLastSibling();
    }

    private static GameObject Panel(string name, Transform parent, Vector2 min, Vector2 max, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>(), min, max, Vector2.zero);
        go.GetComponent<Image>().color = color;
        return go;
    }

    private static TMP_Text Label(Transform parent, string text, Vector2 min, Vector2 max, float minSize, float maxSize, FontStyles style)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>(), min, max, Vector2.zero);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = minSize;
        tmp.fontSizeMax = maxSize;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static TMP_InputField CreateInput(Transform parent, TMP_DefaultControls.Resources resources, string placeholder,
        Vector2 min, Vector2 max, bool password)
    {
        var go = TMP_DefaultControls.CreateInputField(resources);
        go.name = "Input" + placeholder;
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>(), min, max, Vector2.zero);

        // E-mail vai como foi digitado (teclado de e-mail no celular, sem correção automática).
        var input = go.GetComponent<TMP_InputField>();
        input.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.EmailAddress;
        input.characterLimit = password ? 72 : 254;
        input.lineType = TMP_InputField.LineType.SingleLine;

        // Texto proporcional à altura da tela (o Canvas do menu é Constant Pixel Size).
        float size = Mathf.Max(18f, Screen.height * 0.04f);
        input.pointSize = size;
        if (input.placeholder is TMP_Text ph) { ph.text = placeholder; ph.fontSize = size; }
        return input;
    }

    private static Button ButtonWithText(Transform parent, TMP_DefaultControls.Resources resources, string text,
        Vector2 min, Vector2 max, UnityEngine.Events.UnityAction onClick)
    {
        var go = TMP_DefaultControls.CreateButton(resources);
        go.name = "Button" + text.Replace(" ", "");
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>(), min, max, Vector2.zero);

        var label = go.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = text;
            label.color = new Color(0.1f, 0.1f, 0.1f);
            label.enableAutoSizing = true;
            label.fontSizeMin = 14f;
            label.fontSizeMax = 40f;
            Stretch(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(8f, 4f));
        }

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

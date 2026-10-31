using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tela de login do menu (e-mail + senha no Supabase). Montada por código por cima do Canvas do menu,
/// sem mexer nos objetos da cena: enquanto ninguém entra, o "Iniciar Jogo" fica escondido.
/// "Criar conta" abre o site do SAVIT (SupabaseConfig.signupUrl). Adicionada pelo MenuController.
/// O login é em pé; depois de entrar a tela volta a ficar deitada, como o resto do menu e o jogo.
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
    private float lastInputHeight;
    private RectTransform cardRect;
    private bool? lastPortrait;

    // Âncoras de cada elemento do cartão: deitado (cartão no centro) e em pé (tela inteira).
    private readonly List<(RectTransform rt, Vector2 landMin, Vector2 landMax, Vector2 portMin, Vector2 portMax)> layout =
        new List<(RectTransform, Vector2, Vector2, Vector2, Vector2)>();

    private void Update()
    {
        // Em pé o login ocupa a tela inteira; deitado, é um cartão no centro.
        bool portrait = Screen.height > Screen.width;
        if (cardRect != null && portrait != lastPortrait)
        {
            lastPortrait = portrait;
            if (portrait) Stretch(cardRect, Vector2.zero, Vector2.one, Vector2.zero);
            else Stretch(cardRect, new Vector2(0.28f, 0.08f), new Vector2(0.72f, 0.92f), Vector2.zero);
            foreach (var e in layout)
                Stretch(e.rt, portrait ? e.portMin : e.landMin, portrait ? e.portMax : e.landMax, Vector2.zero);
        }

        // A tela gira (login em pé, jogo deitado): a letra dos campos acompanha a altura real deles.
        if (emailInput == null) return;
        float height = ((RectTransform)emailInput.transform).rect.height;
        if (Mathf.Abs(height - lastInputHeight) < 0.5f) return;
        lastInputHeight = height;
        FitInputText(emailInput, height);
        FitInputText(passwordInput, height);
    }

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

        // Sessão salva no aparelho: entra direto (continua deitado). Sem sessão, o login fica em pé.
        SetBusy(true, "Entrando...");
        StartCoroutine(SupabaseAuth.TryRestore(ok =>
        {
            SetBusy(false, ok ? null : "");
            ShowLoggedIn(ok);
            ApplyOrientation(ok);
        }));
    }

    public void SignIn()
    {
        if (busy) return;
        SetBusy(true, "Entrando...");
        StartCoroutine(SupabaseAuth.SignIn(emailInput.text, passwordInput.text, OnAuthResult));
    }

    /// <summary>Cadastro é feito no site do SAVIT.</summary>
    public void OpenSignUpSite()
    {
        var config = SupabaseConfig.Load();
        if (config == null || string.IsNullOrWhiteSpace(config.signupUrl))
        {
            SetBusy(false, "Endereço do site de cadastro não configurado (SupabaseConfig > Signup Url).");
            return;
        }
        Application.OpenURL(config.signupUrl);
    }

    public void SignOut()
    {
        if (busy) return;
        StartCoroutine(SupabaseAuth.SignOut(() =>
        {
            passwordInput.text = "";
            SetBusy(false, "");
            ShowLoggedIn(false);
            ApplyOrientation(false);
        }));
    }

    private void OnAuthResult(bool ok, string error)
    {
        SetBusy(false, ok ? null : error);
        if (ok) passwordInput.text = "";
        ShowLoggedIn(ok);
        if (ok) ApplyOrientation(true);
    }

    /// <summary>
    /// Login em pé; logado, volta ao padrão do projeto (Player Settings): rotação automática só em paisagem.
    /// No Editor não tem efeito (a Game view mantém a resolução escolhida).
    /// </summary>
    private static void ApplyOrientation(bool loggedIn)
    {
        if (!loggedIn)
        {
            Screen.orientation = ScreenOrientation.Portrait;
            return;
        }

        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
        Screen.orientation = ScreenOrientation.AutoRotation;
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
        cardRect = card.GetComponent<RectTransform>();

        // Âncoras deitado na criação; as de "em pé" (tela inteira, botões empilhados) vão no Portrait(...).
        var title = Label(card.transform, "Entrar no SAVIT", new Vector2(0.05f, 0.85f), new Vector2(0.95f, 0.96f), 20f, 44f, FontStyles.Bold);
        Portrait(title, new Vector2(0.08f, 0.78f), new Vector2(0.92f, 0.86f));
        var subtitle = Label(card.transform, "Entre com seu e-mail e senha.", new Vector2(0.05f, 0.76f), new Vector2(0.95f, 0.84f), 12f, 24f, FontStyles.Normal);
        Portrait(subtitle, new Vector2(0.08f, 0.72f), new Vector2(0.92f, 0.77f));

        emailInput = CreateInput(card.transform, resources, "E-mail", new Vector2(0.08f, 0.61f), new Vector2(0.92f, 0.72f), false);
        Portrait(emailInput, new Vector2(0.08f, 0.62f), new Vector2(0.92f, 0.68f));
        passwordInput = CreateInput(card.transform, resources, "Senha", new Vector2(0.08f, 0.47f), new Vector2(0.92f, 0.58f), true);
        Portrait(passwordInput, new Vector2(0.08f, 0.54f), new Vector2(0.92f, 0.60f));
        passwordInput.onSubmit.AddListener(_ => SignIn());

        statusText = Label(card.transform, "", new Vector2(0.05f, 0.35f), new Vector2(0.95f, 0.45f), 11f, 20f, FontStyles.Normal);
        Portrait(statusText, new Vector2(0.08f, 0.46f), new Vector2(0.92f, 0.53f));

        signInButton = ButtonWithText(card.transform, resources, "Entrar", new Vector2(0.08f, 0.19f), new Vector2(0.48f, 0.31f), SignIn);
        Portrait(signInButton, new Vector2(0.08f, 0.37f), new Vector2(0.92f, 0.44f));
        var signUpButton = ButtonWithText(card.transform, resources, "Criar conta", new Vector2(0.52f, 0.19f), new Vector2(0.92f, 0.31f), OpenSignUpSite);
        Portrait(signUpButton, new Vector2(0.08f, 0.28f), new Vector2(0.92f, 0.35f));
        var quitButton = ButtonWithText(card.transform, resources, "Sair do jogo", new Vector2(0.32f, 0.05f), new Vector2(0.68f, 0.14f), Application.Quit);
        Portrait(quitButton, new Vector2(0.25f, 0.17f), new Vector2(0.75f, 0.23f));

        loggedBar = Panel("LoggedBar", canvas, new Vector2(0.60f, 0.88f), new Vector2(0.99f, 0.98f), new Color(0f, 0f, 0f, 0.55f));
        greetingText = Label(loggedBar.transform, "", new Vector2(0.03f, 0f), new Vector2(0.55f, 1f), 11f, 22f, FontStyles.Normal);
        ButtonWithText(loggedBar.transform, resources, "Trocar usuário", new Vector2(0.58f, 0.12f), new Vector2(0.98f, 0.88f), SignOut);

        overlay.transform.SetAsLastSibling();
    }

    /// <summary>Guarda as âncoras atuais (deitado) e as de em pé, para trocar quando a tela girar.</summary>
    private void Portrait(Component element, Vector2 portraitMin, Vector2 portraitMax)
    {
        var rt = (RectTransform)element.transform;
        layout.Add((rt, rt.anchorMin, rt.anchorMax, portraitMin, portraitMax));
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

        // Tamanho da letra vem do Update (FitInputText), pela altura real do campo.
        if (input.placeholder is TMP_Text ph) ph.text = placeholder;
        return input;
    }

    // Letra a ~40% da altura do campo: cabe na área do texto (que tem margem interna) sem cortar.
    private static void FitInputText(TMP_InputField input, float height)
    {
        float size = Mathf.Clamp(height * 0.4f, 12f, 40f);
        input.pointSize = size;
        if (input.placeholder is TMP_Text ph) ph.fontSize = size;
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
            label.fontSizeMax = 28f;
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

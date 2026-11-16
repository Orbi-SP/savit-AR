using System;
using System.Collections;
using System.Reflection;
using SavitGame.AR;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tutorial com legendas escritas: antes de cada encaixe mostra a explicação do componente
/// (fecha com "Entendi"), depois mostra uma legenda curta com a ação até o encaixe terminar.
/// Fica na cena (Canvas AR_UI), porque os Canvas dentro do GameScenePrefab são desligados no AR.
/// </summary>
public class TutorialManager : MonoBehaviour
{
	[Serializable]
	public class TutorialStep
	{
		public string id;
		public string title;
		[TextArea(3, 8)] public string explanation;
		[TextArea(2, 4)] public string actionHint;
		[Tooltip("Narração opcional (repositório legendas-audio).")]
		public AudioClip voice;

		[Header("Encaixe (vazio = só explicação)")]
		[Tooltip("Nome do script da peça, ex.: RAMModule, MotherboardPlacer, PowerSupplyPlacer, PowerCableConnector.")]
		public string componentType;
		[Tooltip("Propriedade bool que indica que o encaixe terminou, ex.: IsSnapped, IsConnected.")]
		public string doneProperty = "IsSnapped";

		public TutorialStep() { }

		public TutorialStep(string id, string title, string explanation, string actionHint, string componentType, string doneProperty)
		{
			this.id = id;
			this.title = title;
			this.explanation = explanation;
			this.actionHint = actionHint;
			this.componentType = componentType;
			this.doneProperty = doneProperty;
		}
	}

	[Header("Referências")]
	public ARPlacementManager placementManager;

	[Header("Explicação")]
	public GameObject explanationPanel;
	public TMP_Text titleText;
	public TMP_Text bodyText;
	public Button okButton;
	[Tooltip("Texto do botão que fecha a explicação (na cena ele vinha como \"Button\").")]
	public string okButtonText = "Got it";

	[Header("Legenda curta (durante o encaixe)")]
	public GameObject hintPanel;
	public TMP_Text hintText;

	[Header("Áudio (opcional)")]
	public AudioSource audioSource;

	[Header("Layout")]
	[Tooltip("Ajusta painéis e textos para tela deitada (âncoras proporcionais + auto-size). Desligue para usar o layout feito no Editor.")]
	public bool autoLayout = true;
	[Tooltip("Cena do menu carregada ao terminar a montagem.")]
	public string menuSceneName = "MainMenu";

	[Header("Etapas (na ordem)")]
	public TutorialStep[] steps = DefaultSteps();

	public int CurrentStepIndex { get; private set; } = -1;
	public bool IsRunning => routine != null;

	private Coroutine routine;
	private bool okPressed;
	private Button stopButton;
	private GameObject completionOverlay;

	private void Awake()
	{
		if (placementManager == null) placementManager = FindFirstObjectByType<ARPlacementManager>();
		if (okButton != null)
		{
			okButton.onClick.AddListener(OnOkPressed);
			var label = okButton.GetComponentInChildren<TMP_Text>(true);
			if (label != null && !string.IsNullOrEmpty(okButtonText)) label.text = okButtonText;
		}
		else
		{
			Debug.LogWarning("[TutorialManager] okButton não atribuído: a explicação não vai fechar.");
		}

		// Textos só exibem: não podem "roubar" o toque do botão quando ficam por cima dele.
		DisableRaycast(titleText);
		DisableRaycast(bodyText);
		DisableRaycast(hintText);

		if (autoLayout) ApplyLandscapeLayout();
		ApplyARUIReadabilityScale();
		HideAll();
	}

	private static void DisableRaycast(TMP_Text text)
	{
		if (text != null) text.raycastTarget = false;
	}

	/// <summary>
	/// O Canvas AR_UI usa Constant Pixel Size e os textos vinham com 200x50 px no centro:
	/// a legenda quebrava a cada 2-3 palavras. Aqui tudo passa a ocupar a largura da tela deitada.
	/// </summary>
	private void ApplyLandscapeLayout()
	{
		// Legenda curta: faixa embaixo, largura toda.
		if (hintPanel != null) Stretch(hintPanel.transform as RectTransform, new Vector2(0f, 0.02f), new Vector2(1f, 0.20f), Vector2.zero);
		if (hintText != null)
		{
			Stretch(hintText.rectTransform, Vector2.zero, Vector2.one, new Vector2(40f, 8f));
			FitText(hintText, 20f, 56f, TextAlignmentOptions.Center);
		}

		// Explicação: título em cima, texto no meio, "Entendi" embaixo.
		if (titleText != null)
		{
			Stretch(titleText.rectTransform, new Vector2(0.06f, 0.80f), new Vector2(0.94f, 0.95f), Vector2.zero);
			FitText(titleText, 28f, 72f, TextAlignmentOptions.Center);
		}
		if (bodyText != null)
		{
			Stretch(bodyText.rectTransform, new Vector2(0.06f, 0.24f), new Vector2(0.94f, 0.78f), Vector2.zero);
			FitText(bodyText, 20f, 52f, TextAlignmentOptions.TopLeft);
		}
		if (okButton != null)
		{
			Stretch(okButton.transform as RectTransform, new Vector2(0.35f, 0.05f), new Vector2(0.65f, 0.19f), Vector2.zero);
			var label = okButton.GetComponentInChildren<TMP_Text>(true);
			if (label != null)
			{
				Stretch(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(10f, 4f));
				FitText(label, 18f, 48f, TextAlignmentOptions.Center);
			}
		}
	}

	private static void Stretch(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 padding)
	{
		if (rt == null) return;
		rt.anchorMin = anchorMin;
		rt.anchorMax = anchorMax;
		rt.pivot = new Vector2(0.5f, 0.5f);
		rt.offsetMin = padding;
		rt.offsetMax = -padding;
	}

	private static void FitText(TMP_Text text, float minSize, float maxSize, TextAlignmentOptions alignment)
	{
		text.textWrappingMode = TextWrappingModes.Normal;
		text.enableAutoSizing = true;
		text.fontSizeMin = minSize;
		text.fontSizeMax = maxSize;
		text.alignment = alignment;
		text.overflowMode = TextOverflowModes.Overflow;
	}

	private void ApplyARUIReadabilityScale()
	{
		foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
		{
			if (IsTutorialOrHint(text.transform)) continue;

			text.fontSize *= 2f;
			text.rectTransform.sizeDelta *= 2f;
			text.textWrappingMode = TextWrappingModes.NoWrap;
			text.overflowMode = TextOverflowModes.Overflow;
		}

		foreach (Button button in GetComponentsInChildren<Button>(true))
		{
			if (IsTutorialOrHint(button.transform)) continue;

			RectTransform rect = button.transform as RectTransform;
			if (rect != null) rect.sizeDelta *= 2f;
		}

		foreach (Slider slider in GetComponentsInChildren<Slider>(true))
		{
			if (IsTutorialOrHint(slider.transform)) continue;

			slider.transform.localScale *= 2f;
		}
	}

	private bool IsTutorialOrHint(Transform candidate)
	{
		for (Transform current = candidate; current != null && current != transform; current = current.parent)
		{
			if (current.name == "TutorialPanel" || current.name == "HintPanel") return true;
		}

		return false;
	}

	private void OnDestroy()
	{
		if (okButton != null) okButton.onClick.RemoveListener(OnOkPressed);
		TutorialGate.InputLocked = false;
	}

	/// <summary>Ligar em ARPlacementManager → On Scene Placed.</summary>
	public void OnScenePlaced()
	{
		ResetTutorial();
		routine = StartCoroutine(Run());
	}

	/// <summary>Ligar no botão "Reposicionar" (On Click).</summary>
	public void ResetTutorial()
	{
		if (routine != null) StopCoroutine(routine);
		routine = null;
		if (stopButton != null) stopButton.gameObject.SetActive(false);
		if (completionOverlay != null) completionOverlay.SetActive(false);
		CurrentStepIndex = -1;
		HideAll();
	}

	public void OnOkPressed()
	{
		Debug.Log($"[TutorialManager] \"{okButtonText}\" pressionado (etapa {CurrentStepIndex}).");
		okPressed = true;
	}

	private IEnumerator Run()
	{
		GameObject root = placementManager != null ? placementManager.SpawnedScene : null;

		for (int i = 0; i < steps.Length; i++)
		{
			TutorialStep step = steps[i];
			CurrentStepIndex = i;

			// Etapa de encaixe cuja peça ainda não existe no jogo (ex.: HD): pula.
			Func<bool> isDone = null;
			if (!string.IsNullOrEmpty(step.componentType))
			{
				isDone = FindDoneCondition(root, step);
				if (isDone == null)
				{
					Debug.Log($"[TutorialManager] Etapa '{step.id}' pulada: '{step.componentType}' não encontrado na cena.");
					continue;
				}
				if (isDone()) continue;
			}

			// 1) Explicação: trava os encaixes até "Entendi".
			TutorialGate.InputLocked = true;
			ShowExplanation(step);
			okPressed = false;
			yield return new WaitUntil(() => okPressed);
			StopVoice();
			if (explanationPanel != null) explanationPanel.SetActive(false);
			TutorialGate.InputLocked = false;

			if (isDone == null) continue;

			// 2) Legenda curta até o encaixe terminar.
			ShowHint(step.actionHint);
			yield return new WaitUntil(isDone);
			ShowHint(null);
		}

		CurrentStepIndex = steps.Length;
		HideAll();
		routine = null;
		ShowStopButton();
	}

	private void ShowExplanation(TutorialStep step)
	{
		ShowHint(null);
		if (titleText != null) titleText.text = step.title;
		if (bodyText != null) bodyText.text = step.explanation;
		if (explanationPanel != null) explanationPanel.SetActive(true);

		if (audioSource != null && step.voice != null)
		{
			audioSource.Stop();
			audioSource.clip = step.voice;
			audioSource.Play();
		}
	}

	private void ShowHint(string text)
	{
		bool show = !string.IsNullOrEmpty(text);
		if (hintText != null) hintText.text = show ? text : string.Empty;
		if (hintPanel != null) hintPanel.SetActive(show);
	}

	private void StopVoice()
	{
		if (audioSource != null && audioSource.isPlaying) audioSource.Stop();
	}

	private void HideAll()
	{
		StopVoice();
		TutorialGate.InputLocked = false;
		if (explanationPanel != null) explanationPanel.SetActive(false);
		ShowHint(null);
	}

	private void ShowCompletionOptions()
	{
		Canvas canvas = FindFirstObjectByType<Canvas>();
		if (canvas == null)
		{
			Debug.LogError("[TutorialManager] Nenhum Canvas encontrado para mostrar as opções finais.");
			return;
		}

		if (completionOverlay == null)
			completionOverlay = BuildCompletionOverlay(canvas.transform);
		completionOverlay.SetActive(true);
		completionOverlay.transform.SetAsLastSibling();
	}

	private void ShowStopButton()
	{
		Canvas canvas = FindFirstObjectByType<Canvas>();
		if (canvas == null)
		{
			Debug.LogError("[TutorialManager] Nenhum Canvas encontrado para mostrar o botão Parar.");
			return;
		}

		if (stopButton == null)
		{
			var resources = new TMP_DefaultControls.Resources();
			stopButton = CreateButton(canvas.transform, resources, "Stop",
				new Vector2(0.78f, 0.88f), new Vector2(0.98f, 0.98f), OpenCompletionOptions);
			stopButton.gameObject.name = "ButtonParar";
		}

		stopButton.gameObject.SetActive(true);
		stopButton.transform.SetAsLastSibling();
	}

	private void OpenCompletionOptions()
	{
		if (stopButton != null) stopButton.gameObject.SetActive(false);
		ShowCompletionOptions();
	}

	private void ReturnToMenu()
	{
		if (completionOverlay != null) completionOverlay.SetActive(false);
		LoginController.ApplyMenuOrientation();
		SceneManager.LoadScene(menuSceneName);
	}

	private static void QuitGame()
	{
		Application.Quit();
	}

	private static GameObject BuildCompletionOverlay(Transform parent)
	{
		var resources = new TMP_DefaultControls.Resources();
		var overlay = CreatePanel("CompletionOverlay", parent, Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.88f));
		var card = CreatePanel("CompletionCard", overlay.transform, new Vector2(0.20f, 0.22f), new Vector2(0.80f, 0.78f),
			new Color(0.12f, 0.12f, 0.15f, 0.98f));
		CreateLabel(card.transform, "Assembly completed!", new Vector2(0.08f, 0.65f), new Vector2(0.92f, 0.88f),
			24f, 56f, FontStyles.Bold);
		CreateLabel(card.transform, "Choose what you would like to do:", new Vector2(0.08f, 0.49f), new Vector2(0.92f, 0.65f),
			16f, 34f, FontStyles.Normal);
		CreateButton(card.transform, resources, "Return to menu", new Vector2(0.12f, 0.30f), new Vector2(0.88f, 0.44f),
			() => FindFirstObjectByType<TutorialManager>()?.ReturnToMenu());
		CreateButton(card.transform, resources, "Quit game", new Vector2(0.12f, 0.10f), new Vector2(0.88f, 0.24f), QuitGame);
		overlay.SetActive(false);
		return overlay;
	}

	private static GameObject CreatePanel(string name, Transform parent, Vector2 min, Vector2 max, Color color)
	{
		var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
		panel.transform.SetParent(parent, false);
		Stretch(panel.GetComponent<RectTransform>(), min, max, Vector2.zero);
		panel.GetComponent<Image>().color = color;
		return panel;
	}

	private static TMP_Text CreateLabel(Transform parent, string text, Vector2 min, Vector2 max,
		float minSize, float maxSize, FontStyles style)
	{
		var labelObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
		labelObject.transform.SetParent(parent, false);
		Stretch(labelObject.GetComponent<RectTransform>(), min, max, Vector2.zero);
		var label = labelObject.GetComponent<TextMeshProUGUI>();
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

	private static Button CreateButton(Transform parent, TMP_DefaultControls.Resources resources, string text,
		Vector2 min, Vector2 max, UnityEngine.Events.UnityAction onClick)
	{
		var buttonObject = TMP_DefaultControls.CreateButton(resources);
		buttonObject.name = "Button" + text.Replace(" ", "");
		buttonObject.transform.SetParent(parent, false);
		Stretch(buttonObject.GetComponent<RectTransform>(), min, max, Vector2.zero);
		var label = buttonObject.GetComponentInChildren<TMP_Text>(true);
		label.text = text;
		label.enableAutoSizing = true;
		label.fontSizeMin = 16f;
		label.fontSizeMax = 34f;
		Stretch(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(8f, 4f));
		var button = buttonObject.GetComponent<Button>();
		button.onClick.AddListener(onClick);
		return button;
	}

	/// <summary>
	/// Procura o script da peça (pelo nome) na cena posicionada e devolve a leitura da propriedade de conclusão.
	/// Por nome para que peças futuras (ex.: HD) funcionem sem mexer aqui, bastando expor IsSnapped.
	/// </summary>
	private static Func<bool> FindDoneCondition(GameObject root, TutorialStep step)
	{
		MonoBehaviour[] candidates = root != null
			? root.GetComponentsInChildren<MonoBehaviour>(false)
			: FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

		foreach (MonoBehaviour mb in candidates)
		{
			if (mb == null || !mb.isActiveAndEnabled || mb.GetType().Name != step.componentType) continue;

			PropertyInfo prop = mb.GetType().GetProperty(step.doneProperty, BindingFlags.Public | BindingFlags.Instance);
			if (prop == null || prop.PropertyType != typeof(bool))
			{
				Debug.LogWarning($"[TutorialManager] {step.componentType} não tem a propriedade bool pública '{step.doneProperty}'.");
				return null;
			}

			MonoBehaviour target = mb;
			return () => target == null || (bool)prop.GetValue(target);
		}
		return null;
	}

	[ContextMenu("Restaurar textos padrão")]
	private void RestoreDefaultSteps()
	{
		steps = DefaultSteps();
	}

	private static TutorialStep[] DefaultSteps()
	{
		return new[]
		{
			new TutorialStep("intro", "PC Assembly",
				"Hello! I will teach you how to assemble a computer, one component at a time.\n\nBefore each installation, I will explain what the component does. Then it is your turn: close your hand to pick up the component, move it to the correct position, and open your hand to release it.",
				"", "", ""),
			new TutorialStep("ram", "RAM",
				"RAM temporarily stores the data that the processor is currently using. It is very fast, but it loses its data when the computer is turned off.\n\nThe more RAM a computer has, the more programs it can keep open at the same time.",
				"Close your hand to pick up the RAM module and move it to the motherboard slot.",
				"RAMModule", "IsSnapped"),
			new TutorialStep("motherboard", "Motherboard",
				"The motherboard is the computer's main component. All other components connect to it, including the processor, memory, storage, and power supply.\n\nIt enables communication between all the components.",
				"Pick up the motherboard and position it inside the case.",
				"MotherboardPlacer", "IsSnapped"),
			new TutorialStep("psu", "Power Supply",
				"The power supply converts electricity from the outlet into the voltages used by the computer and distributes power to all components.\n\nA high-quality power supply protects the components from power fluctuations.",
				"Pick up the power supply and install it in its place inside the case.",
				"PowerSupplyPlacer", "IsSnapped"),
			new TutorialStep("atx24", "24-Pin ATX Cable",
				"The 24-pin ATX cable is the main power cable. It carries power from the power supply to the motherboard.\n\nThe motherboard will not turn on without it connected.",
				"Pick up the cable connector and move it to the motherboard's power connector.",
				"PowerCableConnector", "IsConnected"),
			new TutorialStep("hd", "Hard Drive",
				"The hard drive is the computer's storage device. It stores the operating system, programs, and files even when the computer is turned off.\n\nIt is screwed into the case and connected to the motherboard with a SATA cable.",
				"Pick up the hard drive and install it in the drive bay.",
				"HardDrivePlacer", "IsInstalled"),
			new TutorialStep("fim", "Congratulations!",
				"You have completed the computer assembly. Well done!",
				"", "", ""),
		};
	}
}

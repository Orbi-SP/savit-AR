using System;
using System.Collections;
using System.Reflection;
using SavitGame.AR;
using TMPro;
using UnityEngine;
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
	public string okButtonText = "Entendi";

	[Header("Legenda curta (durante o encaixe)")]
	public GameObject hintPanel;
	public TMP_Text hintText;

	[Header("Áudio (opcional)")]
	public AudioSource audioSource;

	[Header("Layout")]
	[Tooltip("Ajusta painéis e textos para tela deitada (âncoras proporcionais + auto-size). Desligue para usar o layout feito no Editor.")]
	public bool autoLayout = true;

	[Header("Etapas (na ordem)")]
	public TutorialStep[] steps = DefaultSteps();

	public int CurrentStepIndex { get; private set; } = -1;
	public bool IsRunning => routine != null;

	private Coroutine routine;
	private bool okPressed;

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
			new TutorialStep("intro", "Montagem do PC",
				"Olá! Vou te ensinar a montar um computador, uma peça de cada vez.\n\nAntes de cada encaixe eu explico para que serve o componente. Depois é a sua vez: feche a mão para segurar a peça, mova até o lugar certo e abra a mão para soltar.",
				"", "", ""),
			new TutorialStep("ram", "Memória RAM",
				"A memória RAM guarda temporariamente os dados que o processador está usando naquele momento. Ela é muito rápida, mas perde tudo quando o computador é desligado.\n\nQuanto mais RAM, mais programas o PC consegue manter abertos ao mesmo tempo.",
				"Feche a mão para segurar a memória RAM e leve-a até o slot da placa-mãe.",
				"RAMModule", "IsSnapped"),
			new TutorialStep("motherboard", "Placa-mãe",
				"A placa-mãe é a peça principal do computador: é nela que todos os outros componentes se conectam, como o processador, a memória, o armazenamento e a energia.\n\nEla faz a comunicação entre todas as peças.",
				"Segure a placa-mãe e posicione-a dentro do gabinete.",
				"MotherboardPlacer", "IsSnapped"),
			new TutorialStep("psu", "Fonte de alimentação",
				"A fonte de alimentação transforma a energia da tomada nas tensões que o computador usa e distribui essa energia para todas as peças.\n\nUma fonte de qualidade protege os componentes contra variações de energia.",
				"Segure a fonte e encaixe-a no espaço dela no gabinete.",
				"PowerSupplyPlacer", "IsSnapped"),
			new TutorialStep("atx24", "Cabo ATX 24 pinos",
				"O cabo ATX de 24 pinos é o cabo principal de energia: ele leva a energia da fonte até a placa-mãe.\n\nSem ele conectado, a placa-mãe não liga.",
				"Segure o conector do cabo e leve-o até o conector de energia da placa-mãe.",
				"PowerCableConnector", "IsConnected"),
			new TutorialStep("hd", "HD (disco rígido)",
				"O HD é o armazenamento do computador: é onde ficam guardados o sistema operacional, os programas e os seus arquivos, mesmo com o PC desligado.\n\nEle é parafusado no gabinete e ligado à placa-mãe pelo cabo SATA.",
				"Segure o HD e encaixe-o na baia do gabinete.",
				"HardDrivePlacer", "IsInstalled"),
			new TutorialStep("fim", "Parabéns!",
				"Você concluiu a montagem das peças do computador. Muito bem!",
				"", "", ""),
		};
	}
}

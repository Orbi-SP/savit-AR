using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Etapa do cabo ATX 24 pinos: depois da fonte encaixada, o jogador segura o conector
/// ATX24PinMB, leva até a placa-mãe e solta para encaixar.
///
/// Fica no ATX24PinPSU (ponta do cabo presa na fonte, onde está o LineRenderer).
/// A pose do ATX24PinMB no Editor é a pose FINAL (encaixado na placa-mãe), em relação ao gabinete
/// (raiz da cena) e não à placa-mãe, que ainda se move até o snap.
/// O LineRenderer é redesenhado em runtime entre os dois conectores.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class PowerCableConnector : MonoBehaviour
{
	[Header("Referências (auto-preenchido se vazio)")]
	public Api api;
	public PowerSupplyPlacer powerSupplyPlacer;
	[Tooltip("Conector ATX24PinMB. A posição/rotação dele no Editor é a posição final do encaixe na placa-mãe.")]
	public Transform mbConnector;
	public LineRenderer cableLine;
	[Tooltip("Opcional: ponto onde o conector fica parado antes de ser pego. Se vazio, fica entre a fonte e a placa (restFraction).")]
	public Transform restPoint;

	[Header("Pontas do cabo (espaço local de cada conector)")]
	[Tooltip("Ponto de saída do cabo no ATX24PinPSU.")]
	public Vector3 cableStartLocal = new Vector3(0.000335298f, -0.0000126908f, 0.000203468f);
	[Tooltip("Ponto que define a direção em que o cabo sai do ATX24PinPSU.")]
	public Vector3 cableStartDirLocal = new Vector3(0.000455919f, -0.0000088412f, 0.000213768f);
	[Tooltip("Ponto de entrada do cabo no ATX24PinMB.")]
	public Vector3 cableEndLocal = new Vector3(0.000636701f, -0.0000155930f, 0.000234116f);
	[Tooltip("Ponto que define a direção em que o cabo entra no ATX24PinMB.")]
	public Vector3 cableEndDirLocal = new Vector3(0.000360737f, -0.0000071964f, 0.000233963f);
	[Tooltip("Liga o cabo no topo do ATX24PinMB (pelo mesh) em vez de usar cableEndLocal.")]
	public bool attachCableToConnectorTop = true;
	[Range(0f, 1f)]
	[Tooltip("Quanto a ponta do cabo entra no topo do ATX24PinMB (fração da altura do conector).")]
	public float cableEndSink = 0.15f;

	[Header("Movimento")]
	[Tooltip("Quanto o conector avança no caminho por variação da mão (0..1 da tela).")]
	public float handSensitivity = 2f;
	[Tooltip("O lado é calculado pela câmera (mão para a direita = conector para a direita na tela). Marque só se o tracking da mão vier espelhado.")]
	public bool invertHandDirection = false;
	[Range(-1f, 0f)]
	[Tooltip("Quanto o conector pode ir para o lado oposto da placa-mãe (fração do caminho), para acompanhar a mão nos dois sentidos.")]
	public float minProgress = -0.5f;
	[Range(0f, 1f)]
	[Tooltip("Posição de repouso entre a fonte (0) e o encaixe (1), usada quando restPoint está vazio.")]
	public float restFraction = 0.3f;
	[Tooltip("Altura que o conector sobe ao segurar, relativa à distância repouso → encaixe.")]
	public float liftFraction = 0.2f;

	[Header("Snap")]
	[Tooltip("BoxCollider (IsTrigger) da área de encaixe no conector de energia da placa-mãe (ATX24PinSnapZone).")]
	public BoxCollider snapZone;
	[Tooltip("Transform final do ATX24PinMB encaixado (filho SnapAnchor da snapZone).")]
	public Transform snapAnchor;
	[Tooltip("Margem de tolerância no plano X/Z para encaixe.")]
	public float snapTolerance = 0.05f;
	[Range(0f, 1f)]
	[Tooltip("Usado só se não houver snapZone: progresso mínimo do caminho (0..1) para encaixar ao soltar.")]
	public float snapThreshold = 0.85f;

	[Header("Cabo")]
	public int curveSegments = 24;
	[Range(0.05f, 1f)]
	[Tooltip("Curvatura do cabo (tamanho das tangentes em relação à distância entre as pontas).")]
	public float curveTension = 0.4f;
	[Tooltip("Esconde cabo e conector ATX24PinMB até a fonte ser encaixada.")]
	public bool hideUntilPowerSupplySnapped = true;

	[Header("Eventos")]
	public UnityEvent onConnected;

	public bool IsConnected => isConnected;

	private static readonly HashSet<Transform> claimedConnectors = new HashSet<Transform>();

	// Pose final em relação à raiz da cena (a placa-mãe se move no snap, a raiz não).
	private Transform sceneRoot;
	private Vector3 finalRootPos;
	private Quaternion finalRootRot;
	private bool hasFinalPose;
	private bool searchedSnapZone;
	private bool isIgnoredCopy;
	private Transform copyRoot;
	// Largura em unidades locais do ATX24PinPSU (com Use World Space desmarcado o Unity escala a largura pelo transform).
	private float baseLocalWidth;
	private Renderer[] connectorRenderers;
	private Renderer[] powerSupplyRenderers;

	private float progress;
	private float lift;
	private bool prevHolding;
	private float grabHandX;
	private float grabProgress;
	private float grabDir = 1f;
	private bool isConnected;

	private void Awake()
	{
		if (cableLine == null) cableLine = GetComponent<LineRenderer>();

		// Cópia do cabo dentro do próprio ATX24PinMB não é a ponta da fonte: ignora para não desenhar um segundo cabo.
		if (IsInsideMbConnector())
		{
			isIgnoredCopy = true;
			cableLine.enabled = false;
			// Esconde o PowerCable da cópia; se o conector estiver direto no ATX24PinMB, esconde só ele.
			bool parentIsMb = transform.parent == null || transform.parent.name.Equals("ATX24PinMB", System.StringComparison.OrdinalIgnoreCase);
			copyRoot = parentIsMb ? transform : transform.parent;
			Debug.LogWarning($"[PowerCableConnector] '{copyRoot.name}/{name}' está dentro de um ATX24PinMB; escondido (remova essa cópia do prefab).");
			copyRoot.gameObject.SetActive(false);
			return;
		}

		float axisScale = AxisScale();
		baseLocalWidth = cableLine.useWorldSpace && axisScale > 0f
			? cableLine.widthMultiplier / axisScale
			: cableLine.widthMultiplier;
		ResolveRefsIfNeeded();
	}

	private bool IsInsideMbConnector()
	{
		for (var t = transform.parent; t != null; t = t.parent)
		{
			if (t.name.Equals("ATX24PinMB", System.StringComparison.OrdinalIgnoreCase)) return true;
		}
		return false;
	}

	private void OnDestroy()
	{
		if (mbConnector != null) claimedConnectors.Remove(mbConnector);
	}

	private void ResolveRefsIfNeeded()
	{
		var root = transform.root;

		if (api == null && root != null) api = root.GetComponentInChildren<Api>(true);
		if (api == null) api = FindFirstObjectByType<Api>();

		if (powerSupplyPlacer == null) powerSupplyPlacer = GetComponentInParent<PowerSupplyPlacer>(true);
		if (powerSupplyPlacer == null && root != null) powerSupplyPlacer = root.GetComponentInChildren<PowerSupplyPlacer>(true);

		if (mbConnector == null && root != null)
		{
			foreach (var t in root.GetComponentsInChildren<Transform>(true))
			{
				if (t.name.Equals("ATX24PinMB", System.StringComparison.OrdinalIgnoreCase) && !claimedConnectors.Contains(t))
				{
					mbConnector = t;
					break;
				}
			}
		}

		// Procura a zona só uma vez (sem zona, o encaixe usa snapThreshold).
		if (snapZone == null && root != null && !searchedSnapZone)
		{
			searchedSnapZone = true;
			foreach (var t in root.GetComponentsInChildren<Transform>(true))
			{
				if (t.name.Equals("ATX24PinSnapZone", System.StringComparison.OrdinalIgnoreCase))
				{
					snapZone = t.GetComponent<BoxCollider>();
					break;
				}
			}
		}

		if (snapAnchor == null && snapZone != null)
		{
			foreach (var anchor in snapZone.GetComponentsInChildren<Transform>(true))
			{
				if (anchor != snapZone.transform &&
					anchor.name.Equals("SnapAnchor", System.StringComparison.OrdinalIgnoreCase))
				{
					snapAnchor = anchor;
					break;
				}
			}
		}

		if (mbConnector != null && !hasFinalPose)
		{
			claimedConnectors.Add(mbConnector);
			sceneRoot = transform.root;
			finalRootPos = sceneRoot.InverseTransformPoint(mbConnector.position);
			finalRootRot = Quaternion.Inverse(sceneRoot.rotation) * mbConnector.rotation;
			connectorRenderers = mbConnector.GetComponentsInChildren<Renderer>(true);
			hasFinalPose = true;
		}
	}

	private void Start()
	{
		if (isIgnoredCopy) return;
		ResolveRefsIfNeeded();

		// O cabo passa a ser desenhado em espaço de mundo, recalculado a cada frame.
		cableLine.useWorldSpace = true;

		Debug.Log($"[PowerCableConnector] Start: api={(api != null ? "OK" : "NULL")} " +
				  $"fonte={(powerSupplyPlacer != null ? "OK" : "NULL")} " +
				  $"ATX24PinMB={(mbConnector != null ? mbConnector.name : "NULL")}");
	}

	private bool PowerSupplyReady => powerSupplyPlacer == null || powerSupplyPlacer.IsSnapped;

	private void Update()
	{
		if (isIgnoredCopy) return;
		ResolveRefsIfNeeded();
		if (mbConnector == null || isConnected) return;

		if (!PowerSupplyReady)
		{
			prevHolding = false;
			progress = 0f;
			lift = 0f;
			return;
		}

		// Explicação do tutorial aberta: não deixa pegar o conector.
		if (TutorialGate.InputLocked) { prevHolding = false; return; }

		bool holding = api != null && api.currentScene == Api.SceneType.Gabinete && api.IsHolding;

		// Ao começar a segurar, captura o offset para evitar "teleporte" do conector.
		if (holding && !prevHolding)
		{
			grabHandX = api.HandPositionX;
			grabProgress = progress;
			grabDir = ScreenDirectionToBoard();
		}

		if (holding)
		{
			float target = grabProgress + (api.HandPositionX - grabHandX) * handSensitivity * grabDir;
			progress = Mathf.Lerp(progress, Mathf.Clamp(target, minProgress, 1f), Time.deltaTime * 10f);
		}
		else
		{
			progress = Mathf.Lerp(progress, 0f, Time.deltaTime * 5f);
		}

		lift = Mathf.Lerp(lift, holding ? 1f : 0f, Time.deltaTime * 5f);

		// Snap só ao SOLTAR e se o conector chegou perto o suficiente da placa-mãe.
		bool canSnap = snapZone != null ? IsAlignedXZ(mbConnector.position) : progress >= snapThreshold;
		if (prevHolding && !holding && canSnap)
		{
			Connect();
			return;
		}

		prevHolding = holding;
	}

	private void LateUpdate()
	{
		if (isIgnoredCopy)
		{
			// O ARPlacementManager reativa a hierarquia ao posicionar a cena: esconde a cópia de novo.
			if (copyRoot != null && copyRoot.gameObject.activeSelf) copyRoot.gameObject.SetActive(false);
			return;
		}

		if (mbConnector == null) return;

		bool visible = isConnected || !hideUntilPowerSupplySnapped || PowerSupplyReady;
		SetVisible(visible);
		if (!visible) return;

		if (!isConnected) UpdateConnectorPose();
		UpdateCable();
	}

	/// <summary>
	/// +1 se a placa-mãe está à direita do repouso na tela, -1 se à esquerda.
	/// Assim a mão indo para a direita sempre leva o conector para a direita, de qualquer ângulo do AR.
	/// </summary>
	private float ScreenDirectionToBoard()
	{
		GetPath(out Vector3 restPos, out Vector3 finalPos, out _);

		Camera cam = Camera.main;
		float side = 1f;
		if (cam != null)
		{
			Vector3 a = cam.WorldToScreenPoint(restPos);
			Vector3 b = cam.WorldToScreenPoint(finalPos);
			side = b.x >= a.x ? 1f : -1f;
		}

		return invertHandDirection ? -side : side;
	}

	// Checagem em X/Z no espaço local da zona (mesmo critério da fonte e da placa-mãe).
	private bool IsAlignedXZ(Vector3 worldPos)
	{
		Vector3 local = snapZone.transform.InverseTransformPoint(worldPos) - snapZone.center;
		Vector3 half = snapZone.size * 0.5f;

		return Mathf.Abs(local.x) <= half.x + snapTolerance &&
			   Mathf.Abs(local.z) <= half.z + snapTolerance;
	}

	private void GetPath(out Vector3 restPos, out Vector3 finalPos, out Quaternion finalRot)
	{
		if (snapAnchor != null)
		{
			finalPos = snapAnchor.position;
			finalRot = snapAnchor.rotation;
		}
		else
		{
			finalPos = sceneRoot.TransformPoint(finalRootPos);
			finalRot = sceneRoot.rotation * finalRootRot;
		}

		restPos = restPoint != null
			? restPoint.position
			: Vector3.Lerp(transform.TransformPoint(cableStartLocal), finalPos, restFraction);
	}

	private void UpdateConnectorPose()
	{
		GetPath(out Vector3 restPos, out Vector3 finalPos, out Quaternion finalRot);

		float height = Vector3.Distance(restPos, finalPos) * liftFraction * lift;
		Vector3 pos = Vector3.LerpUnclamped(restPos, finalPos, progress) + transform.root.up * height;

		mbConnector.SetPositionAndRotation(pos, finalRot);

		// Nunca atravessa a fonte: se encostar na caixa da fonte, sobe até ficar por cima dela.
		if (TryGetPowerSupplyBounds(out Bounds psuBounds) && TryGetConnectorBounds(out Bounds connBounds) &&
			connBounds.Intersects(psuBounds))
		{
			float raise = psuBounds.max.y - connBounds.min.y + connBounds.size.y * 0.1f;
			if (raise > 0f) mbConnector.position += Vector3.up * raise;
		}
	}

	private void UpdateCable()
	{
		Vector3 p0 = transform.TransformPoint(cableStartLocal);
		Vector3 p3 = mbConnector.TransformPoint(cableEndLocal);
		// O cableEndLocal desenhado fica acima do conector: prende a ponta no topo do mesh do ATX24PinMB.
		if (attachCableToConnectorTop && TryGetConnectorBounds(out Bounds endBounds))
		{
			p3 = new Vector3(endBounds.center.x, endBounds.max.y - endBounds.size.y * cableEndSink, endBounds.center.z);
		}
		float len = Vector3.Distance(p0, p3) * curveTension;

		Vector3 exitDir = SafeDir(transform.TransformPoint(cableStartDirLocal) - p0);
		Vector3 entryDir = SafeDir(mbConnector.TransformPoint(cableEndDirLocal) - p3);

		// O cabo chega no conector da placa por cima (a placa fica deitada no gabinete).
		if (Vector3.Dot(entryDir, Vector3.up) < 0f) entryDir = -entryDir;
		if (attachCableToConnectorTop) entryDir = Vector3.up;

		bool hasPsu = TryGetPowerSupplyBounds(out Bounds psuBounds);
		// O cabo sai do conector da fonte para fora dela, nunca para dentro.
		if (hasPsu && Vector3.Dot(exitDir, p0 - psuBounds.center) < 0f) exitDir = -exitDir;

		Vector3 c1 = p0 + exitDir * len;
		Vector3 c2 = p3 + entryDir * len;

		// Em runtime o cabo é desenhado em mundo: converte a largura local para a escala atual (AR usa instanceScale).
		cableLine.widthMultiplier = baseLocalWidth * AxisScale();

		// Sobe a curvatura até o cabo passar por cima da fonte em vez de atravessá-la.
		if (hasPsu)
		{
			psuBounds.Expand(cableLine.startWidth);
			float step = Mathf.Max(len, 1e-4f) * 0.25f;
			for (int k = 0; k < 16 && CurveCrosses(psuBounds, p0, c1, c2, p3); k++)
			{
				c1 += Vector3.up * step;
				c2 += Vector3.up * step;
			}
		}

		int count = Mathf.Max(2, curveSegments + 1);
		if (cableLine.positionCount != count) cableLine.positionCount = count;

		for (int i = 0; i < count; i++)
		{
			cableLine.SetPosition(i, Bezier(p0, c1, c2, p3, i / (float)(count - 1)));
		}
	}

	private static Vector3 Bezier(Vector3 p0, Vector3 c1, Vector3 c2, Vector3 p3, float t)
	{
		float u = 1f - t;
		return u * u * u * p0 + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * p3;
	}

	private static bool CurveCrosses(Bounds bounds, Vector3 p0, Vector3 c1, Vector3 c2, Vector3 p3)
	{
		// Ignora o comecinho: a ponta do cabo nasce encostada no conector da fonte.
		for (int i = 2; i <= 24; i++)
		{
			if (bounds.Contains(Bezier(p0, c1, c2, p3, i / 24f))) return true;
		}
		return false;
	}

	private bool TryGetPowerSupplyBounds(out Bounds bounds)
	{
		bounds = default;
		if (powerSupplyPlacer == null) return false;

		// Corpo da fonte, sem o próprio cabo/conector (que são filhos dela).
		if (powerSupplyRenderers == null)
		{
			var list = new List<Renderer>();
			foreach (var r in powerSupplyPlacer.GetComponentsInChildren<MeshRenderer>(true))
			{
				bool isCable = r.transform.IsChildOf(transform) ||
					(transform.parent != null && transform.parent != powerSupplyPlacer.transform && r.transform.IsChildOf(transform.parent));
				if (!isCable) list.Add(r);
			}
			powerSupplyRenderers = list.ToArray();
		}

		return Encapsulate(powerSupplyRenderers, out bounds);
	}

	private bool TryGetConnectorBounds(out Bounds bounds)
	{
		return Encapsulate(connectorRenderers, out bounds);
	}

	private static bool Encapsulate(Renderer[] renderers, out Bounds bounds)
	{
		bounds = default;
		bool has = false;
		if (renderers == null) return false;

		foreach (var r in renderers)
		{
			if (r == null || r is LineRenderer || !r.gameObject.activeInHierarchy) continue;
			if (!has) { bounds = r.bounds; has = true; }
			else bounds.Encapsulate(r.bounds);
		}
		return has;
	}

	private float AxisScale()
	{
		return transform.TransformVector(Vector3.right).magnitude;
	}

	private static Vector3 SafeDir(Vector3 v)
	{
		return v.sqrMagnitude > 1e-12f ? v.normalized : Vector3.up;
	}

	private void SetVisible(bool visible)
	{
		if (cableLine.enabled != visible) cableLine.enabled = visible;
		if (connectorRenderers == null) return;
		foreach (var r in connectorRenderers)
		{
			if (r != null && r.enabled != visible) r.enabled = visible;
		}
	}

	private void Connect()
	{
		isConnected = true;
		prevHolding = false;
		progress = 1f;
		lift = 0f;

		GetPath(out _, out Vector3 finalPos, out Quaternion finalRot);
		mbConnector.SetPositionAndRotation(finalPos, finalRot);
		UpdateCable();

		Debug.Log("[PowerCableConnector] Cabo ATX 24 pinos conectado na placa-mãe.");
		onConnected?.Invoke();
	}
}

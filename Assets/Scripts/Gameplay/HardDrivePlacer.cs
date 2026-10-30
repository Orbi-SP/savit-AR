using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class HardDrivePlacer : MonoBehaviour
{
	[Header("Referências")]
	public Api api;
	public BoxCollider snapZone;
	public Transform snapAnchor;
	public PowerSupplyPlacer powerSupplyPlacer;
	[Tooltip("Cabo ATX 24 pinos: o HD só é liberado depois que ele estiver conectado.")]
	public PowerCableConnector powerCable;
	[Tooltip("Objeto vazio na placa-mãe onde o cabo SATA é ligado (SATAPort_Placa).")]
	public Transform sataPortPlaca;

	[Header("Movimento")]
	public bool useAxisX = true;
	public float moveSpeed = 1.0f;
	[Tooltip("Deslocamento vertical local aplicado enquanto o HD está sendo segurado.")]
	public float liftY = 3.0f;
	public Vector2 moveLimits = new Vector2(-10f, 10f);

	[Header("Snap")]
	public float snapTolerance = 0.05f;

	[Header("Animação - Colocar na baia")]
	[Tooltip("De onde o HD começa a deslizar para dentro da baia, no forward do SnapAnchor (em tamanhos do HD).")]
	public float entryOffset = 0.8f;
	public float approachDuration = 0.3f;
	public float slideDuration = 0.6f;

	[Header("Animação - Parafusos")]
	[Tooltip("Quanto cada parafuso começa afastado da lateral (em relação à meia-largura do HD).")]
	public float screwOutDistance = 0.3f;
	public float screwTurns = 3f;
	public float screwDuration = 0.4f;

	[Header("Cabo SATA")]
	public Material cableMaterial;
	public Color cableColor = new Color(0.75f, 0.1f, 0.1f);
	public float cableDuration = 0.8f;
	[Tooltip("Espessura do cabo em relação ao tamanho do HD.")]
	public float cableWidth = 0.04f;
	[Tooltip("Barriga do cabo em relação à distância entre o HD e a placa-mãe.")]
	public float cableSag = 0.25f;
	public int cableSegments = 16;

	[Header("Eventos")]
	public UnityEvent onInstalled;

	public bool IsSnapped => isSnapped;
	public bool IsInstalled { get; private set; }

	private Vector3 originalLocalPos;
	private Quaternion originalLocalRot;
	private float accum;
	private bool prevHolding;
	private bool isSnapped;
	private float grabHandX;
	private float grabAccum;

	private readonly List<Transform> screws = new List<Transform>();
	private Transform sataConnector;
	private LineRenderer cable;
	private float cableProgress;
	private float hdSize;

	private void ResolveRefsIfNeeded()
	{
		var root = transform.root;

		if (api == null && root != null) api = root.GetComponentInChildren<Api>(true);
		if (api == null) api = FindFirstObjectByType<Api>();

		if (powerSupplyPlacer == null && root != null)
			powerSupplyPlacer = root.GetComponentInChildren<PowerSupplyPlacer>(true);
		if (powerSupplyPlacer == null)
			powerSupplyPlacer = FindFirstObjectByType<PowerSupplyPlacer>();

		if (powerCable == null && root != null)
			powerCable = root.GetComponentInChildren<PowerCableConnector>(true);
		if (powerCable == null)
			powerCable = FindFirstObjectByType<PowerCableConnector>();
	}

	private void Start()
	{
		originalLocalPos = transform.localPosition;
		originalLocalRot = transform.localRotation;
		ResolveRefsIfNeeded();

		var rb = GetComponent<Rigidbody>();
		if (rb != null)
		{
			rb.isKinematic = true;
			rb.useGravity = false;
		}

		// Parafusos e conector vêm do HardDrive.fbx (Tools/gerar_hd_fbx.py).
		foreach (var t in GetComponentsInChildren<Transform>(true))
		{
			if (t.name.StartsWith("Screw_", System.StringComparison.OrdinalIgnoreCase)) screws.Add(t);
			else if (t.name.Equals("HD_SATA_Data", System.StringComparison.OrdinalIgnoreCase)) sataConnector = t;
		}
		screws.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

		// Os parafusos só aparecem quando o HD é parafusado no gabinete.
		foreach (var screw in screws) SetRenderersEnabled(screw, false);

		Debug.Log($"[HardDrivePlacer] Start: api={(api != null ? "OK" : "NULL")} " +
				  $"fonte={(powerSupplyPlacer != null ? "OK" : "NULL")} caboATX={(powerCable != null ? "OK" : "NULL")} " +
				  $"parafusos={screws.Count} " +
				  $"sataPlaca={(sataPortPlaca != null ? "OK" : "NULL")}");
	}

	private void Update()
	{
		if (isSnapped) return;

		ResolveRefsIfNeeded();
		if (api == null || api.currentScene != Api.SceneType.Gabinete) return;
		if (powerSupplyPlacer == null || !powerSupplyPlacer.IsSnapped) return;
		// Mesmo gesto do conector ATX: só libera o HD depois do cabo ligado na placa-mãe.
		if (powerCable != null && !powerCable.IsConnected) return;
		// Explicação do tutorial aberta: não deixa pegar a peça.
		if (TutorialGate.InputLocked) { prevHolding = false; return; }

		bool holding = api.IsHolding;

		if (holding && !prevHolding)
		{
			grabHandX = api.HandPositionX;
			grabAccum = accum;
		}

		if (holding)
		{
			float movementRange = (moveLimits.y - moveLimits.x) * moveSpeed;
			float targetAccum = grabAccum + (api.HandPositionX - grabHandX) * movementRange;
			accum = Mathf.Lerp(accum, targetAccum, Time.deltaTime * 10f);
		}
		else
		{
			accum = Mathf.Lerp(accum, 0f, Time.deltaTime * 5f);
		}

		accum = Mathf.Clamp(accum, moveLimits.x, moveLimits.y);

		Vector3 targetLocal = originalLocalPos;
		if (useAxisX) targetLocal.x = originalLocalPos.x + accum;
		else targetLocal.z = originalLocalPos.z + accum;
		targetLocal.y = holding ? originalLocalPos.y + liftY : originalLocalPos.y;

		transform.localPosition = Vector3.Lerp(transform.localPosition, targetLocal, Time.deltaTime * 5f);
		transform.localRotation = originalLocalRot;

		if (prevHolding && !holding && IsAlignedXZ(transform.position))
		{
			prevHolding = holding;
			BeginInstall();
			return;
		}

		prevHolding = holding;
	}

	private void LateUpdate()
	{
		// Atualiza todo frame: a cena AR pode ser reancorada depois da instalação.
		if (cable != null) UpdateCable();
	}

	private bool IsAlignedXZ(Vector3 worldPos)
	{
		if (snapZone == null) return false;

		Vector3 local = snapZone.transform.InverseTransformPoint(worldPos);
		Vector3 half = snapZone.size * 0.5f;

		return Mathf.Abs(local.x) <= half.x + snapTolerance &&
			   Mathf.Abs(local.z) <= half.z + snapTolerance;
	}

	private void BeginInstall()
	{
		if (snapAnchor == null)
		{
			Debug.LogWarning("[HardDrivePlacer] SnapAnchor nao esta atribuido.");
			return;
		}

		isSnapped = true;
		hdSize = GetSize();
		StartCoroutine(InstallSequence());
	}

	private IEnumerator InstallSequence()
	{
		// 1) Colocar: o HD vai até a frente da baia e desliza para dentro.
		Vector3 bayEntry = snapAnchor.position + snapAnchor.forward * (hdSize * entryOffset);
		yield return MoveTo(bayEntry, snapAnchor.rotation, approachDuration);
		yield return MoveTo(snapAnchor.position, snapAnchor.rotation, slideDuration);
		Debug.Log("[HardDrivePlacer] HD colocado na baia.");

		// 2) Parafusar: um parafuso por vez, entrando e girando.
		foreach (var screw in screws)
			yield return ScrewIn(screw);
		Debug.Log("[HardDrivePlacer] HD parafusado no gabinete.");

		// 3) Ligar na placa-mãe: o cabo SATA sai do HD até a porta da placa.
		if (sataPortPlaca != null)
			yield return ConnectCable();
		else
			Debug.LogWarning("[HardDrivePlacer] SATAPort_Placa nao atribuido; cabo SATA nao sera desenhado.");

		IsInstalled = true;
		Debug.Log("✅ HD instalado (colocado, parafusado e ligado na placa-mãe).");
		onInstalled?.Invoke();
	}

	private IEnumerator MoveTo(Vector3 targetPos, Quaternion targetRot, float duration)
	{
		Vector3 fromPos = transform.position;
		Quaternion fromRot = transform.rotation;

		for (float t = 0f; t < duration; t += Time.deltaTime)
		{
			float k = Mathf.SmoothStep(0f, 1f, t / duration);
			transform.SetPositionAndRotation(Vector3.Lerp(fromPos, targetPos, k), Quaternion.Slerp(fromRot, targetRot, k));
			yield return null;
		}

		transform.SetPositionAndRotation(targetPos, targetRot);
	}

	private IEnumerator ScrewIn(Transform screw)
	{
		SetRenderersEnabled(screw, true);

		Vector3 seatedPos = screw.position;
		Quaternion seatedRot = screw.rotation;

		// O parafuso entra pela lateral, no eixo X do HD, vindo do lado de fora.
		Vector3 local = transform.InverseTransformPoint(GetCenter(screw));
		Vector3 outward = transform.TransformVector(new Vector3(local.x * screwOutDistance, 0f, 0f));
		Vector3 axis = transform.right;

		for (float t = 0f; t < screwDuration; t += Time.deltaTime)
		{
			float k = t / screwDuration;
			screw.SetPositionAndRotation(
				seatedPos + outward * (1f - k),
				Quaternion.AngleAxis(screwTurns * 360f * k, axis) * seatedRot);
			yield return null;
		}

		screw.SetPositionAndRotation(seatedPos, seatedRot);
	}

	private IEnumerator ConnectCable()
	{
		cable = CreateCable();

		for (float t = 0f; t < cableDuration; t += Time.deltaTime)
		{
			cableProgress = t / cableDuration;
			yield return null;
		}

		cableProgress = 1f;
		CreatePlug();
	}

	private LineRenderer CreateCable()
	{
		var go = new GameObject("SATA_Cable");
		go.transform.SetParent(transform, false);

		var line = go.AddComponent<LineRenderer>();
		line.useWorldSpace = true;
		line.positionCount = 0;
		line.numCapVertices = 4;
		line.numCornerVertices = 2;
		line.widthMultiplier = hdSize * cableWidth;
		line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		line.startColor = cableColor;
		line.endColor = cableColor;

		if (cableMaterial != null)
		{
			line.material = cableMaterial;
		}
		else
		{
			var shader = Shader.Find("Sprites/Default");
			if (shader != null) line.material = new Material(shader);
		}

		return line;
	}

	private void UpdateCable()
	{
		if (sataPortPlaca == null) return;

		Vector3 start = sataConnector != null ? GetCenter(sataConnector) : transform.position;
		Vector3 end = sataPortPlaca.position;
		Vector3 control = (start + end) * 0.5f + Vector3.down * (Vector3.Distance(start, end) * cableSag);

		int segments = Mathf.Max(2, cableSegments);
		cable.positionCount = segments + 1;
		for (int i = 0; i <= segments; i++)
		{
			float s = cableProgress * i / segments;
			float u = 1f - s;
			cable.SetPosition(i, u * u * start + 2f * u * s * control + s * s * end);
		}
	}

	private void CreatePlug()
	{
		var plug = GameObject.CreatePrimitive(PrimitiveType.Cube);
		plug.name = "SATA_Plug";
		Destroy(plug.GetComponent<Collider>());

		plug.transform.SetPositionAndRotation(sataPortPlaca.position, sataPortPlaca.rotation);
		plug.transform.localScale = Vector3.one * (hdSize * cableWidth * 2.5f);
		plug.transform.SetParent(sataPortPlaca, worldPositionStays: true);

		var rend = plug.GetComponent<Renderer>();
		if (rend != null) rend.material.color = Color.black;
	}

	private float GetSize()
	{
		bool any = false;
		Bounds b = new Bounds(transform.position, Vector3.zero);
		foreach (var r in GetComponentsInChildren<Renderer>())
		{
			if (!r.enabled) continue;
			if (!any) { b = r.bounds; any = true; }
			else b.Encapsulate(r.bounds);
		}
		return any ? Mathf.Max(b.size.x, b.size.y, b.size.z) : 0.1f;
	}

	private static Vector3 GetCenter(Transform t)
	{
		var r = t.GetComponentInChildren<Renderer>();
		return r != null && r.enabled ? r.bounds.center : t.position;
	}

	private static void SetRenderersEnabled(Transform t, bool enabled)
	{
		foreach (var r in t.GetComponentsInChildren<Renderer>(true))
			r.enabled = enabled;
	}
}

using UnityEngine;

public class PowerSupplyPlacer : MonoBehaviour
{
	[Header("Referências")]
	public Api api;
	public BoxCollider snapZone;
	public Transform snapAnchor;
	public MotherboardPlacer motherboardPlacer;

	[Header("Movimento")]
	public bool useAxisX = true;
	public float moveSpeed = 3.0f;
	[Tooltip("Deslocamento vertical local aplicado enquanto a fonte está sendo segurada.")]
	public float liftY = 4.0f;
	public Vector2 moveLimits = new Vector2(-10f, 10f);

	[Header("Snap")]
	public float snapTolerance = 0.05f;

	private Vector3 originalLocalPos;
	private Quaternion originalLocalRot;
	private float accum;
	private bool prevHolding;
	private bool isSnapped;
	private float grabHandX;
	private float grabAccum;

	public bool IsSnapped => isSnapped;

	private void ResolveRefsIfNeeded()
	{
		var root = transform.root;

		if (api == null && root != null) api = root.GetComponentInChildren<Api>(true);
		if (api == null) api = FindFirstObjectByType<Api>();

		if (motherboardPlacer == null && root != null)
			motherboardPlacer = root.GetComponentInChildren<MotherboardPlacer>(true);
		if (motherboardPlacer == null)
			motherboardPlacer = FindFirstObjectByType<MotherboardPlacer>();
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

		Debug.Log($"[PowerSupplyPlacer] Start: api={(api != null ? "OK" : "NULL")} " +
				  $"motherboard={(motherboardPlacer != null ? "OK" : "NULL")}");
	}

	private void Update()
	{
		if (isSnapped) return;

		ResolveRefsIfNeeded();
		if (api == null || api.currentScene != Api.SceneType.Gabinete) return;
		if (motherboardPlacer == null || !motherboardPlacer.IsSnapped) return;

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
			DoSnap();
			return;
		}

		prevHolding = holding;
	}

	private bool IsAlignedXZ(Vector3 worldPos)
	{
		if (snapZone == null) return false;

		Vector3 local = snapZone.transform.InverseTransformPoint(worldPos);
		Vector3 half = snapZone.size * 0.5f;

		return Mathf.Abs(local.x) <= half.x + snapTolerance &&
			   Mathf.Abs(local.z) <= half.z + snapTolerance;
	}

	private void DoSnap()
	{
		if (snapAnchor == null)
		{
			Debug.LogWarning("[PowerSupplyPlacer] SnapAnchor nao esta atribuido.");
			return;
		}

		transform.SetPositionAndRotation(snapAnchor.position, snapAnchor.rotation);
		isSnapped = true;

		var rb = GetComponent<Rigidbody>();
		if (rb != null)
		{
			rb.isKinematic = true;
			rb.useGravity = false;
		}

		Debug.Log("Power supply encaixada (Snap aplicado).");
	}
}

#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace SavitGame.Editor.HardDriveSetup {
    /// <summary>
    /// Monta o HD nos prefabs seguindo o padrão das outras peças (fonte, cabo ATX):
    /// instância do HardDrive.fbx na raiz, HardDriveSnapZone/SnapAnchor e SATAPort_Placa na placa-mãe.
    /// As posições saem dos bounds reais das peças; o ajuste fino é mover os objetos no prefab.
    /// Menu: SAVIT > Configurar HD nos prefabs. Idempotente: não mexe em prefab que já tem HardDrive.
    /// </summary>
    public static class HardDrivePrefabSetup {
        private const string ModelPath = "Assets/Fries and Seagull/Interior 01/PCParts/HD/HardDrive.fbx";
        private const string MainPrefabPath = "Assets/GameScenePrefab.prefab";
        private const string PreviewPrefabPath = "Assets/GameScenePreviewPrefab.prefab";

        // HD 3,5" (147 mm) ÷ fonte SFX (125 mm).
        private const float HdToPsuRatio = 1.18f;

        [MenuItem("SAVIT/Configurar HD nos prefabs")]
        public static void Run() {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) {
                Debug.LogError($"[HardDrivePrefabSetup] Modelo não encontrado em '{ModelPath}'. Rode Tools/gerar_hd_fbx.py.");
                return;
            }

            Setup(MainPrefabPath, model, addPlacer: true);
            Setup(PreviewPrefabPath, model, addPlacer: false);
            AssetDatabase.SaveAssets();
        }

        private static void Setup(string prefabPath, GameObject model, bool addPlacer) {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try {
                Transform r = root.transform;

                if (FindExact(r, "HardDrive") != null) {
                    Debug.LogWarning($"[HardDrivePrefabSetup] {prefabPath} já tem HardDrive; nada foi alterado.");
                    return;
                }

                Transform caseT = FindContains(r, "PCCaseMM");
                Transform psu = FindExact(r, "PowerSupply");
                Transform psuZone = FindContains(r, "PowerSupplySnapZone");
                Transform motherboard = FindContains(r, "Motherboards");
                Transform atxMb = FindContains(r, "ATX24PinMB");
                Transform monitor = FindContains(r, "MonitorD10");

                if (caseT == null || psu == null || motherboard == null) {
                    Debug.LogError($"[HardDrivePrefabSetup] {prefabPath}: gabinete, fonte ou placa-mãe não encontrados.");
                    return;
                }

                // Tudo no espaço local da raiz do prefab (Y = cima, a raiz fica apoiada no plano do AR).
                Bounds caseB = LocalBounds(caseT, r);
                Bounds psuB = LocalBounds(psu, r);
                float tableY = Mathf.Min(caseB.min.y, psuB.min.y);

                // 1) HD: instância do FBX, deitado, na proporção real em relação à fonte.
                var hd = (GameObject)PrefabUtility.InstantiatePrefab(model, r);
                hd.name = "HardDrive";
                hd.transform.localRotation = Quaternion.identity;
                hd.transform.localScale = Vector3.one;
                Bounds hdB = LocalBounds(hd.transform, r);
                float scale = MaxSize(psuB) * HdToPsuRatio / Mathf.Max(MaxSize(hdB), 1e-6f);
                hd.transform.localScale = Vector3.one * scale;
                hdB = LocalBounds(hd.transform, r);

                // 2) Baia: no chão do gabinete, do lado oposto ao da fonte no eixo horizontal mais longo
                //    (fonte atrás, gaiola de HD na frente).
                Vector3 psuFinal = psuZone != null ? r.InverseTransformPoint(FindSnapAnchor(psuZone).position) : psuB.center;
                bool longIsX = caseB.size.x >= caseB.size.z;
                Vector3 bay = psuFinal;
                if (longIsX) bay.x = Mathf.Clamp(2f * caseB.center.x - psuFinal.x, caseB.min.x + hdB.extents.x, caseB.max.x - hdB.extents.x);
                else bay.z = Mathf.Clamp(2f * caseB.center.z - psuFinal.z, caseB.min.z + hdB.extents.z, caseB.max.z - hdB.extents.z);
                bay.y = caseB.min.y + caseB.size.y * 0.08f;

                // 3) Início: fora do gabinete, na mesma "raia" Z da baia (o gesto move o HD só no eixo X).
                float gap = hdB.size.x;
                float nearSide = bay.x >= caseB.center.x ? caseB.max.x + gap : caseB.min.x - gap;
                float farSide = bay.x >= caseB.center.x ? caseB.min.x - gap : caseB.max.x + gap;
                Vector3 start = new Vector3(nearSide, tableY, bay.z);
                Bounds startB = new Bounds(start + Vector3.up * hdB.extents.y, hdB.size);
                bool blocked = startB.Intersects(psuB) ||
                               (monitor != null && startB.Intersects(LocalBounds(monitor, r)));
                if (blocked) start.x = farSide;
                hd.transform.localPosition = start;

                // 4) HardDriveSnapZone + SnapAnchor (mesmo padrão do ATX24PinSnapZone, filho da raiz).
                var zone = new GameObject("HardDriveSnapZone");
                zone.transform.SetParent(r, false);
                zone.transform.localPosition = bay;
                var box = zone.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(hdB.size.x * 1.6f, hdB.size.y * 2f, hdB.size.z * 1.6f);

                var anchor = new GameObject("SnapAnchor");
                anchor.transform.SetParent(zone.transform, false);

                // 5) SATAPort_Placa: na placa-mãe, ao lado do conector de 24 pinos.
                var port = new GameObject("SATAPort_Placa");
                port.transform.SetParent(motherboard, true);
                Vector3 atxLocal = atxMb != null ? r.InverseTransformPoint(atxMb.position) : LocalBounds(motherboard, r).center;
                Vector3 towardBay = Vector3.ProjectOnPlane(bay - atxLocal, Vector3.up).normalized;
                port.transform.position = r.TransformPoint(atxLocal + towardBay * hdB.size.z * 0.25f);

                // 6) HardDrivePlacer com referências ligadas (como o PowerSupplyPlacer no prefab).
                if (addPlacer) {
                    var placer = hd.AddComponent<HardDrivePlacer>();
                    placer.snapZone = box;
                    placer.snapAnchor = anchor.transform;
                    placer.sataPortPlaca = port.transform;
                    placer.powerSupplyPlacer = psu.GetComponent<PowerSupplyPlacer>();
                    placer.powerCable = root.GetComponentInChildren<PowerCableConnector>(true);

                    // Meio movimento da mão (0,5) leva o HD do início até a baia.
                    float d = bay.x - start.x;
                    float dist = Mathf.Abs(d);
                    placer.useAxisX = true;
                    placer.moveLimits = d >= 0f ? new Vector2(-0.2f * dist, 1.3f * dist) : new Vector2(-1.3f * dist, 0.2f * dist);
                    placer.moveSpeed = 2f * dist / Mathf.Max(placer.moveLimits.y - placer.moveLimits.x, 1e-6f);
                    placer.liftY = caseB.size.y * 1.1f;
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"[HardDrivePrefabSetup] HD configurado em {prefabPath}: início={start} baia={bay} " +
                          $"escala={scale:F4} sata={r.InverseTransformPoint(port.transform.position)}" +
                          (blocked ? " (lado oposto: o lado da baia estava ocupado)" : ""));
            }
            finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Bounds das malhas no espaço de 'space'. Usa sharedMesh.bounds: funciona com objeto inativo.
        // Ignora cabos/conectores ATX para não esticar a caixa da fonte até a placa-mãe.
        private static Bounds LocalBounds(Transform part, Transform space) {
            bool any = false;
            var b = new Bounds();
            foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true)) {
                if (mf.sharedMesh == null || IsCable(mf.transform, part)) continue;
                Matrix4x4 m = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++) {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return any ? b : new Bounds(space.InverseTransformPoint(part.position), Vector3.zero);
        }

        private static bool IsCable(Transform t, Transform stop) {
            for (; t != null && t != stop; t = t.parent) {
                if (t.name.IndexOf("Cable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.name.IndexOf("ATX", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static float MaxSize(Bounds b) => Mathf.Max(b.size.x, b.size.y, b.size.z);

        private static Transform FindSnapAnchor(Transform zone) {
            foreach (var t in zone.GetComponentsInChildren<Transform>(true)) {
                if (t != zone && t.name.Equals("SnapAnchor", StringComparison.OrdinalIgnoreCase)) return t;
            }
            return zone;
        }

        private static Transform FindExact(Transform root, string name) {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) {
                if (t.name.Equals(name, StringComparison.OrdinalIgnoreCase)) return t;
            }
            return null;
        }

        private static Transform FindContains(Transform root, string name) {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) {
                if (t.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return t;
            }
            return null;
        }
    }
}
#endif

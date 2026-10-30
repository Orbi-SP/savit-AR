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

        /// <summary>
        /// Só leitura: loga no Console (e no Editor.log) onde cada peça está no espaço da raiz do prefab,
        /// para conferir/ajustar a posição do HD sem precisar de print. Não salva nada.
        /// </summary>
        [MenuItem("SAVIT/Diagnosticar posições do HD")]
        public static void Diagnose() {
            GameObject root = PrefabUtility.LoadPrefabContents(MainPrefabPath);
            try {
                Transform r = root.transform;
                var sb = new System.Text.StringBuilder("[HardDriveDiag] posições no espaço da raiz de " + MainPrefabPath + "\n");

                string[] points = { "HardDrive", "HardDriveSnapZone", "SATAPort_Placa", "PowerSupply", "PowerSupplySnapZone",
                                    "MotherboardSnapZone", "ATX24PinMB", "ATX24PinSnapZone", "Motherboards", "PCCaseMM", "MonitorD10", "Ram" };
                foreach (var n in points) {
                    Transform t = FindExact(r, n) ?? FindContains(r, n);
                    if (t == null) { sb.AppendLine($"  {n}: NÃO ENCONTRADO"); continue; }
                    string anchor = "";
                    if (n.EndsWith("SnapZone")) anchor = $" anchor={r.InverseTransformPoint(FindSnapAnchor(t).position)}";
                    sb.AppendLine($"  {n}: pos={r.InverseTransformPoint(t.position)} ativo={t.gameObject.activeInHierarchy}{anchor}");
                }

                string[] parts = { "PCCaseMM", "PowerSupply", "Motherboards", "MonitorD10", "HardDrive", "Ram" };
                foreach (var n in parts) {
                    Transform t = FindExact(r, n) ?? FindContains(r, n);
                    if (t == null) continue;
                    Bounds all = LocalBounds(t, r);
                    Bounds visible = LocalBounds(t, r, onlyVisible: true);
                    sb.AppendLine($"  bounds {n}: todas={all.center} tam={all.size} | visíveis={visible.center} tam={visible.size}");
                }

                // Malhas do gabinete: as que estão longe do centro visível podem estar esticando os bounds.
                Transform caseT = FindContains(r, "PCCaseMM");
                if (caseT != null) {
                    Bounds visible = LocalBounds(caseT, r, onlyVisible: true);
                    sb.AppendLine("  malhas do gabinete mais distantes do centro visível:");
                    var list = new System.Collections.Generic.List<(float d, string line)>();
                    foreach (var mf in caseT.GetComponentsInChildren<MeshFilter>(true)) {
                        if (mf.sharedMesh == null) continue;
                        Bounds b = MeshBounds(mf, r);
                        var rend = mf.GetComponent<Renderer>();
                        list.Add((Vector3.Distance(b.center, visible.center),
                            $"    {mf.name}: centro={b.center} tam={b.size} ativo={mf.gameObject.activeInHierarchy} renderer={(rend != null && rend.enabled)}"));
                    }
                    list.Sort((a, b) => b.d.CompareTo(a.d));
                    for (int i = 0; i < Mathf.Min(8, list.Count); i++) sb.AppendLine(list[i].line);
                }

                Debug.Log(sb.ToString());
            }
            finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
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
                // Exato primeiro: "Motherboards" por substring (sem caixa) casa com "MotherboardSnapZone".
                Transform motherboard = FindExact(r, "Motherboards") ?? FindContains(r, "Motherboards");
                Transform atxMb = FindContains(r, "ATX24PinMB");

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

                // 2) Baia: dentro do gabinete, ao lado da fonte instalada (lado das gavetas), no mesmo nível
                //    dela e com a borda do fundo alinhada à da fonte. Não usa os bounds do gabinete em X:
                //    uma malha do PCCaseMM se estende ~14 unidades para fora do gabinete visível.
                Vector3 psuFinal = psuZone != null ? r.InverseTransformPoint(FindSnapAnchor(psuZone).position) : psuB.center;
                var bay = new Vector3(
                    psuFinal.x + psuB.extents.x + hdB.size.x * 0.05f + hdB.extents.x,
                    psuFinal.y - psuB.extents.y,
                    psuFinal.z - (hdB.extents.z - psuB.extents.z));

                // 3) Início: à esquerda da fonte, na mesma "raia" Z da baia (o gesto move o HD só no eixo X).
                var start = new Vector3(psuB.min.x - hdB.size.x * 0.25f - hdB.extents.x, tableY, bay.z);
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
                          $"escala={scale:F4} sata={r.InverseTransformPoint(port.transform.position)}");
            }
            finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ── Geometria real do gabinete (para posicionar o HD na gaveta) ─────────────

        private const string GeometryPath = "Library/SavitDiag/geometry.json";
        private const string LayoutPath = "Tools/hd_layout.json";

        /// <summary>
        /// Só leitura: simula a montagem final (placa-mãe e fonte nos anchors) e exporta as malhas visíveis
        /// do gabinete, placa, fonte e HD no espaço da raiz do prefab. Não salva o prefab.
        /// </summary>
        [MenuItem("SAVIT/Exportar geometria do gabinete")]
        public static void ExportGeometry() {
            GameObject root = PrefabUtility.LoadPrefabContents(MainPrefabPath);
            try {
                Transform r = root.transform;
                var mb = root.GetComponentInChildren<MotherboardPlacer>(true);
                var psu = root.GetComponentInChildren<PowerSupplyPlacer>(true);
                var hd = root.GetComponentInChildren<HardDrivePlacer>(true);

                var sb = new System.Text.StringBuilder(1 << 20);
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                string F(float v) => v.ToString("0.#####", inv);
                string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
                string M(Matrix4x4 m) {
                    var parts = new string[16];
                    for (int i = 0; i < 16; i++) parts[i] = F(m[i / 4, i % 4]);
                    return "[" + string.Join(",", parts) + "]";
                }

                sb.Append("{\"transforms\":{");
                if (hd != null) sb.Append($"\"hdStart\":{{\"pos\":{V(r.InverseTransformPoint(hd.transform.position))},\"m\":{M(r.worldToLocalMatrix * hd.transform.localToWorldMatrix)}}},");
                if (mb != null) sb.Append($"\"mbStart\":{{\"m\":{M(r.worldToLocalMatrix * mb.transform.localToWorldMatrix)}}},");

                // Montagem final simulada (como o DoSnap de cada peça).
                if (mb != null && mb.snapAnchor != null) mb.transform.SetPositionAndRotation(mb.snapAnchor.position, mb.snapAnchor.rotation);
                if (psu != null && psu.snapAnchor != null) psu.transform.SetPositionAndRotation(psu.snapAnchor.position, psu.snapAnchor.rotation);
                if (hd != null && hd.snapAnchor != null) hd.transform.SetPositionAndRotation(hd.snapAnchor.position, hd.snapAnchor.rotation);

                if (mb != null) sb.Append($"\"mbFinal\":{{\"m\":{M(r.worldToLocalMatrix * mb.transform.localToWorldMatrix)}}},");
                foreach (var n in new[] { "PowerSupplySnapZone", "MotherboardSnapZone", "ATX24PinSnapZone", "HardDriveSnapZone", "SATAPort_Placa", "ATX24PinMB" }) {
                    Transform t = FindExact(r, n);
                    if (t == null) continue;
                    var box = t.GetComponent<BoxCollider>();
                    string size = box != null ? $",\"box\":{V(Vector3.Scale(box.size, t.lossyScale))}" : "";
                    sb.Append($"\"{n}\":{{\"pos\":{V(r.InverseTransformPoint(t.position))}{size}}},");
                }
                sb.Length--; // vírgula final
                sb.Append("},\"meshes\":[");

                int count = 0, skipped = 0;
                void Export(Transform part, string group, bool withUv) {
                    if (part == null) return;
                    foreach (var mf in part.GetComponentsInChildren<MeshFilter>(false)) {
                        var rend = mf.GetComponent<Renderer>();
                        Mesh mesh = mf.sharedMesh;
                        if (mesh == null || rend == null || !rend.enabled) continue;
                        Vector3[] verts;
                        try { verts = mesh.vertices; }
                        catch (Exception e) { skipped++; Debug.LogWarning($"[HardDriveGeo] {mf.name}: {e.Message}"); continue; }

                        Matrix4x4 toRoot = r.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                        if (count++ > 0) sb.Append(',');
                        sb.Append($"{{\"group\":\"{group}\",\"name\":\"{mf.name}\",\"v\":[");
                        for (int i = 0; i < verts.Length; i++) {
                            Vector3 p = toRoot.MultiplyPoint3x4(verts[i]);
                            if (i > 0) sb.Append(',');
                            sb.Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z));
                        }
                        sb.Append("]");
                        if (withUv) {
                            Vector2[] uv = mesh.uv;
                            sb.Append(",\"uv\":[");
                            for (int i = 0; i < uv.Length; i++) {
                                if (i > 0) sb.Append(',');
                                sb.Append(F(uv[i].x)).Append(',').Append(F(uv[i].y));
                            }
                            sb.Append("]");
                        }
                        sb.Append(",\"subs\":[");
                        var mats = rend.sharedMaterials;
                        for (int s = 0; s < mesh.subMeshCount; s++) {
                            Material mat = s < mats.Length ? mats[s] : null;
                            Color c = mat != null && mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                                    : mat != null && mat.HasProperty("_Color") ? mat.color : Color.gray;
                            Texture tex = mat != null ? mat.mainTexture : null;
                            string texPath = tex != null ? AssetDatabase.GetAssetPath(tex).Replace("\\", "/") : "";
                            if (s > 0) sb.Append(',');
                            sb.Append($"{{\"mat\":\"{(mat != null ? mat.name : "")}\",\"color\":{V(new Vector3(c.r, c.g, c.b))},\"tex\":\"{texPath}\",\"t\":[");
                            int[] tris = mesh.GetTriangles(s);
                            for (int i = 0; i < tris.Length; i++) {
                                if (i > 0) sb.Append(',');
                                sb.Append(tris[i]);
                            }
                            sb.Append("]}");
                        }
                        sb.Append("]}");
                    }
                }

                Export(FindContains(r, "PCCaseMM"), "case", false);
                Export(mb != null ? mb.transform : FindExact(r, "Motherboards"), "mb", true);
                Export(psu != null ? psu.transform : FindExact(r, "PowerSupply"), "psu", false);
                Export(hd != null ? hd.transform : FindExact(r, "HardDrive"), "hd", false);
                sb.Append("]}");

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(GeometryPath));
                System.IO.File.WriteAllText(GeometryPath, sb.ToString());
                Debug.Log($"[HardDriveGeo] {count} malhas exportadas ({skipped} puladas) em {GeometryPath}.");
            }
            finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

#pragma warning disable 0649 // preenchido pelo JsonUtility
        [Serializable]
        private class HdLayout {
            public Vector3 hdStartPos;
            public Vector3 hdStartEuler;
            public float hdScale;
            public Vector3 bayPos;
            public Vector3 bayEuler;
            public Vector3 zoneSize;
            public Vector3 sataLocalInBoard;
            public Vector2 moveLimits;
            public float moveSpeed;
            public float liftY;
            public float cableSag;
        }
#pragma warning restore 0649

        /// <summary>
        /// Aplica Tools/hd_layout.json (calculado a partir da geometria exportada) nos dois prefabs:
        /// HD no início, gaveta (zona + anchor de lado) e SATAPort_Placa como filha da placa-mãe.
        /// </summary>
        [MenuItem("SAVIT/Aplicar layout do HD")]
        public static void ApplyLayout() {
            if (!System.IO.File.Exists(LayoutPath)) {
                Debug.LogError($"[HardDriveLayout] '{LayoutPath}' não encontrado.");
                return;
            }
            var layout = JsonUtility.FromJson<HdLayout>(System.IO.File.ReadAllText(LayoutPath));
            ApplyLayout(MainPrefabPath, layout);
            ApplyLayout(PreviewPrefabPath, layout);
            AssetDatabase.SaveAssets();
        }

        private static void ApplyLayout(string prefabPath, HdLayout layout) {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try {
                Transform r = root.transform;
                Transform hd = FindExact(r, "HardDrive");
                Transform zone = FindExact(r, "HardDriveSnapZone");
                Transform port = FindExact(r, "SATAPort_Placa");
                var mbPlacer = root.GetComponentInChildren<MotherboardPlacer>(true);
                Transform board = mbPlacer != null ? mbPlacer.transform : FindExact(r, "Motherboards");
                if (hd == null || zone == null || board == null) {
                    Debug.LogError($"[HardDriveLayout] {prefabPath}: HardDrive, HardDriveSnapZone ou placa-mãe não encontrados. Rode antes 'Configurar HD nos prefabs'.");
                    return;
                }

                hd.localPosition = layout.hdStartPos;
                hd.localRotation = Quaternion.Euler(layout.hdStartEuler);
                hd.localScale = Vector3.one * layout.hdScale;

                zone.localPosition = layout.bayPos;
                zone.localRotation = Quaternion.identity;
                var box = zone.GetComponent<BoxCollider>();
                if (box != null) box.size = layout.zoneSize;
                Transform anchor = FindSnapAnchor(zone);
                if (anchor != zone) {
                    anchor.localPosition = Vector3.zero;
                    anchor.localRotation = Quaternion.Euler(layout.bayEuler);
                }

                if (port == null) port = new GameObject("SATAPort_Placa").transform;
                port.SetParent(board, false);
                port.localPosition = layout.sataLocalInBoard;
                port.localRotation = Quaternion.identity;
                port.localScale = Vector3.one;

                var placer = hd.GetComponent<HardDrivePlacer>();
                if (placer != null) {
                    placer.moveLimits = layout.moveLimits;
                    placer.moveSpeed = layout.moveSpeed;
                    placer.liftY = layout.liftY;
                    if (layout.cableSag > 0f) placer.cableSag = layout.cableSag;
                    placer.sataPortPlaca = port;
                    placer.sataNextToAtx24 = false;
                    placer.snapAnchor = anchor;
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"[HardDriveLayout] Layout aplicado em {prefabPath}: início={layout.hdStartPos} baia={layout.bayPos} sata(local placa)={layout.sataLocalInBoard}");
            }
            finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Bounds das malhas no espaço de 'space'. Usa sharedMesh.bounds: funciona com objeto inativo.
        // Ignora cabos/conectores ATX para não esticar a caixa da fonte até a placa-mãe.
        private static Bounds LocalBounds(Transform part, Transform space, bool onlyVisible = false) {
            bool any = false;
            var b = new Bounds();
            foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true)) {
                if (mf.sharedMesh == null || IsCable(mf.transform, part)) continue;
                if (onlyVisible) {
                    var rend = mf.GetComponent<Renderer>();
                    if (!mf.gameObject.activeInHierarchy || rend == null || !rend.enabled) continue;
                }
                Bounds mb = MeshBounds(mf, space);
                if (!any) { b = mb; any = true; }
                else b.Encapsulate(mb);
            }
            return any ? b : new Bounds(space.InverseTransformPoint(part.position), Vector3.zero);
        }

        private static Bounds MeshBounds(MeshFilter mf, Transform space) {
            Matrix4x4 m = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Bounds mb = mf.sharedMesh.bounds;
            var b = new Bounds(m.MultiplyPoint3x4(mb.min), Vector3.zero);
            for (int i = 0; i < 8; i++) {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                b.Encapsulate(m.MultiplyPoint3x4(corner));
            }
            return b;
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

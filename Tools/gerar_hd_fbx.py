"""
Gera o modelo 3D de um HD SATA 3.5" para o SAVIT.

Uso:
    python Tools/gerar_hd_fbx.py          -> Assets/Fries and Seagull/Interior 01/PCParts/HD/HardDrive.fbx (FBX 7.4 ASCII)
    python Tools/gerar_hd_fbx.py --obj    -> .../PCParts/HD/HardDrive.obj (+ .mtl), contingência

Medidas reais em centímetros (UnitScaleFactor = 1). No Unity, deixe "Convert Units" marcado.
Filhos gerados: HD_Body, HD_PCB, HD_Label, HD_SATA_Data, HD_SATA_Power, Screw_0..Screw_3.
Os parafusos têm pivô no próprio centro (só no FBX) para o HardDrivePlacer girá-los.
"""
import argparse
import math
import os
from datetime import datetime

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# Mesmo padrão da fonte (PCParts/Fonte).
OUT_DIR = os.path.join(REPO, "Assets", "Fries and Seagull", "Interior 01", "PCParts", "HD")

# Medidas do HD 3.5" (cm): largura (X), altura (Y), comprimento (Z). Traseira (conectores) em -Z.
W, H, L = 10.16, 2.61, 14.70
PCB_H = 0.25

MATERIAIS = {
    "HD_Aluminio": (0.72, 0.73, 0.75),
    "HD_PCB": (0.05, 0.35, 0.15),
    "HD_Etiqueta": (0.92, 0.92, 0.90),
    "HD_Conector": (0.08, 0.08, 0.08),
    "HD_Parafuso": (0.55, 0.55, 0.58),
}


# ── Geometria ──────────────────────────────────────────────────────────────

def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


class Mesh:
    """Polígonos com normais por vértice-do-polígono e um material por polígono."""

    def __init__(self):
        self.verts = []
        self.polys = []  # (indices, normais, material)

    def add_poly(self, pts, normals, face_normal, material):
        # FBX é destro: polígono de frente em sentido anti-horário visto de fora.
        if dot(cross(sub(pts[1], pts[0]), sub(pts[2], pts[0])), face_normal) < 0:
            pts = list(reversed(pts))
            normals = list(reversed(normals))
        base = len(self.verts)
        self.verts.extend(pts)
        self.polys.append((list(range(base, base + len(pts))), list(normals), material))

    def box(self, mn, mx, material):
        x0, y0, z0 = mn
        x1, y1, z1 = mx
        faces = [
            ((1, 0, 0), [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)]),
            ((-1, 0, 0), [(x0, y0, z0), (x0, y0, z1), (x0, y1, z1), (x0, y1, z0)]),
            ((0, 1, 0), [(x0, y1, z0), (x0, y1, z1), (x1, y1, z1), (x1, y1, z0)]),
            ((0, -1, 0), [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)]),
            ((0, 0, 1), [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]),
            ((0, 0, -1), [(x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0)]),
        ]
        for n, pts in faces:
            self.add_poly(pts, [n] * 4, n, material)

    def cylinder_x(self, xa, xb, radius, segments, material, smooth=True):
        """Cilindro no eixo X, centrado em y = z = 0, de xa até xb."""
        ring = []
        for i in range(segments):
            ang = 2 * math.pi * i / segments
            ring.append((math.cos(ang), math.sin(ang)))
        for i in range(segments):
            (ca, sa), (cb, sb) = ring[i], ring[(i + 1) % segments]
            pts = [(xa, ca * radius, sa * radius), (xa, cb * radius, sb * radius),
                   (xb, cb * radius, sb * radius), (xb, ca * radius, sa * radius)]
            mid = 2 * math.pi * (i + 0.5) / segments
            face_n = (0.0, math.cos(mid), math.sin(mid))
            if smooth:
                normals = [(0.0, ca, sa), (0.0, cb, sb), (0.0, cb, sb), (0.0, ca, sa)]
            else:
                normals = [face_n] * 4
            self.add_poly(pts, normals, face_n, material)
        for x, n in ((xa, (-1, 0, 0) if xa < xb else (1, 0, 0)), (xb, (1, 0, 0) if xa < xb else (-1, 0, 0))):
            pts = [(x, c * radius, s * radius) for c, s in ring]
            self.add_poly(pts, [n] * len(pts), n, material)


def montar_hd():
    """Retorna [(nome, pivô, Mesh)] com a geometria em coordenadas locais ao pivô."""
    partes = []

    body = Mesh()
    body.box((-W / 2, PCB_H, -L / 2), (W / 2, H, L / 2), "HD_Aluminio")
    partes.append(("HD_Body", (0, 0, 0), body))

    pcb = Mesh()
    pcb.box((-W / 2 + 0.2, 0.0, -L / 2), (W / 2 - 0.2, PCB_H, L / 2 - 0.3), "HD_PCB")
    partes.append(("HD_PCB", (0, 0, 0), pcb))

    label = Mesh()
    label.box((-3.8, H, -2.5), (3.8, H + 0.02, 5.5), "HD_Etiqueta")
    partes.append(("HD_Label", (0, 0, 0), label))

    sata_data = Mesh()
    sata_data.box((1.0, PCB_H, -L / 2 - 0.25), (2.2, PCB_H + 0.6, -L / 2 + 0.3), "HD_Conector")
    partes.append(("HD_SATA_Data", (0, 0, 0), sata_data))

    sata_power = Mesh()
    sata_power.box((-2.6, PCB_H, -L / 2 - 0.25), (0.4, PCB_H + 0.6, -L / 2 + 0.3), "HD_Conector")
    partes.append(("HD_SATA_Power", (0, 0, 0), sata_power))

    # Parafusos laterais (eixo X). Pivô na lateral do HD; a cabeça fica do lado de fora.
    furos = [(1, -4.5), (1, 4.0), (-1, -4.5), (-1, 4.0)]
    for i, (lado, z) in enumerate(furos):
        screw = Mesh()
        s = lado
        screw.cylinder_x(s * -0.45, s * 0.15, 0.17, 12, "HD_Parafuso")
        screw.cylinder_x(s * 0.15, s * 0.30, 0.33, 6, "HD_Parafuso", smooth=False)
        # Fenda em cruz na cabeça: deixa a rotação visível.
        xa, xb = sorted((s * 0.30, s * 0.32))
        screw.box((xa, -0.22, -0.04), (xb, 0.22, 0.04), "HD_Conector")
        screw.box((xa, -0.04, -0.22), (xb, 0.04, 0.22), "HD_Conector")
        partes.append((f"Screw_{i}", (s * W / 2, 0.635, z), screw))

    return partes


# ── FBX 7.4 ASCII ──────────────────────────────────────────────────────────

def fmt(v):
    return f"{v:.6f}".rstrip("0").rstrip(".") if v != 0 else "0"


def array_block(nome, valores, indent):
    tab = "\t" * indent
    return f"{tab}{nome}: *{len(valores)} {{\n{tab}\ta: {','.join(valores)}\n{tab}}}\n"


def uv_box(p, n):
    ax = max(range(3), key=lambda k: abs(n[k]))
    u, v = [(1, 2), (0, 2), (0, 1)][ax]
    return (p[u] / 10.0, p[v] / 10.0)


def escrever_fbx(caminho, partes):
    ids = iter(range(1000000, 9999999))
    root_id, root_attr = next(ids), next(ids)
    mat_ids = {nome: next(ids) for nome in MATERIAIS}

    objetos, conexoes = [], []

    objetos.append(f'\tNodeAttribute: {root_attr}, "NodeAttribute::", "Null" {{\n\t\tTypeFlags: "Null"\n\t}}\n')
    objetos.append(
        f'\tModel: {root_id}, "Model::HardDrive", "Null" {{\n'
        f'\t\tVersion: 232\n\t\tProperties70:  {{\n'
        f'\t\t\tP: "Lcl Translation", "Lcl Translation", "", "A",0,0,0\n'
        f'\t\t}}\n\t\tShading: Y\n\t\tCulling: "CullingOff"\n\t}}\n')
    conexoes.append(f'\tC: "OO",{root_attr},{root_id}\n')
    conexoes.append(f'\tC: "OO",{root_id},0\n')

    for nome, (r, g, b) in MATERIAIS.items():
        objetos.append(
            f'\tMaterial: {mat_ids[nome]}, "Material::{nome}", "" {{\n'
            f'\t\tVersion: 102\n\t\tShadingModel: "phong"\n\t\tMultiLayer: 0\n\t\tProperties70:  {{\n'
            f'\t\t\tP: "DiffuseColor", "Color", "", "A",{fmt(r)},{fmt(g)},{fmt(b)}\n'
            f'\t\t\tP: "SpecularColor", "Color", "", "A",0.2,0.2,0.2\n'
            f'\t\t\tP: "Shininess", "double", "Number", "",20\n'
            f'\t\t}}\n\t}}\n')

    for nome, pivo, mesh in partes:
        geo_id, model_id = next(ids), next(ids)
        mats = []
        for _, _, m in mesh.polys:
            if m not in mats:
                mats.append(m)

        verts, pvi, normais, uvs, mat_idx = [], [], [], [], []
        for p in mesh.verts:
            verts.extend(fmt(c) for c in p)
        for indices, ns, m in mesh.polys:
            for k, idx in enumerate(indices):
                pvi.append(str(idx if k < len(indices) - 1 else -idx - 1))
                normais.extend(fmt(c) for c in ns[k])
                uvs.extend(fmt(c) for c in uv_box(mesh.verts[idx], ns[k]))
            mat_idx.append(str(mats.index(m)))
        n_pv = len(pvi)

        geo = f'\tGeometry: {geo_id}, "Geometry::{nome}", "Mesh" {{\n'
        geo += array_block("Vertices", verts, 2)
        geo += array_block("PolygonVertexIndex", pvi, 2)
        geo += "\t\tGeometryVersion: 124\n"
        geo += ('\t\tLayerElementNormal: 0 {\n\t\t\tVersion: 101\n\t\t\tName: ""\n'
                '\t\t\tMappingInformationType: "ByPolygonVertex"\n\t\t\tReferenceInformationType: "Direct"\n')
        geo += array_block("Normals", normais, 3)
        geo += "\t\t}\n"
        geo += ('\t\tLayerElementUV: 0 {\n\t\t\tVersion: 101\n\t\t\tName: "UVMap"\n'
                '\t\t\tMappingInformationType: "ByPolygonVertex"\n\t\t\tReferenceInformationType: "IndexToDirect"\n')
        geo += array_block("UV", uvs, 3)
        geo += array_block("UVIndex", [str(i) for i in range(n_pv)], 3)
        geo += "\t\t}\n"
        geo += ('\t\tLayerElementMaterial: 0 {\n\t\t\tVersion: 101\n\t\t\tName: ""\n'
                '\t\t\tMappingInformationType: "ByPolygon"\n\t\t\tReferenceInformationType: "IndexToDirect"\n')
        geo += array_block("Materials", mat_idx, 3)
        geo += "\t\t}\n"
        geo += ('\t\tLayer: 0 {\n\t\t\tVersion: 100\n'
                '\t\t\tLayerElement:  {\n\t\t\t\tType: "LayerElementNormal"\n\t\t\t\tTypedIndex: 0\n\t\t\t}\n'
                '\t\t\tLayerElement:  {\n\t\t\t\tType: "LayerElementMaterial"\n\t\t\t\tTypedIndex: 0\n\t\t\t}\n'
                '\t\t\tLayerElement:  {\n\t\t\t\tType: "LayerElementUV"\n\t\t\t\tTypedIndex: 0\n\t\t\t}\n'
                '\t\t}\n\t}\n')
        objetos.append(geo)

        px, py, pz = pivo
        objetos.append(
            f'\tModel: {model_id}, "Model::{nome}", "Mesh" {{\n'
            f'\t\tVersion: 232\n\t\tProperties70:  {{\n'
            f'\t\t\tP: "Lcl Translation", "Lcl Translation", "", "A",{fmt(px)},{fmt(py)},{fmt(pz)}\n'
            f'\t\t\tP: "DefaultAttributeIndex", "int", "Integer", "",0\n'
            f'\t\t}}\n\t\tShading: T\n\t\tCulling: "CullingOff"\n\t}}\n')

        conexoes.append(f'\tC: "OO",{model_id},{root_id}\n')
        conexoes.append(f'\tC: "OO",{geo_id},{model_id}\n')
        for m in mats:
            conexoes.append(f'\tC: "OO",{mat_ids[m]},{model_id}\n')

    n_models = len(partes) + 1
    agora = datetime.now()
    out = "; FBX 7.4.0 project file\n; Gerado por Tools/gerar_hd_fbx.py (SAVIT)\n\n"
    out += ("FBXHeaderExtension:  {\n\tFBXHeaderVersion: 1003\n\tFBXVersion: 7400\n"
            "\tCreationTimeStamp:  {\n\t\tVersion: 1000\n"
            f"\t\tYear: {agora.year}\n\t\tMonth: {agora.month}\n\t\tDay: {agora.day}\n"
            f"\t\tHour: {agora.hour}\n\t\tMinute: {agora.minute}\n\t\tSecond: {agora.second}\n"
            "\t\tMillisecond: 0\n\t}\n"
            '\tCreator: "SAVIT gerar_hd_fbx.py"\n}\n')
    out += ('GlobalSettings:  {\n\tVersion: 1000\n\tProperties70:  {\n'
            '\t\tP: "UpAxis", "int", "Integer", "",1\n'
            '\t\tP: "UpAxisSign", "int", "Integer", "",1\n'
            '\t\tP: "FrontAxis", "int", "Integer", "",2\n'
            '\t\tP: "FrontAxisSign", "int", "Integer", "",1\n'
            '\t\tP: "CoordAxis", "int", "Integer", "",0\n'
            '\t\tP: "CoordAxisSign", "int", "Integer", "",1\n'
            '\t\tP: "OriginalUpAxis", "int", "Integer", "",1\n'
            '\t\tP: "OriginalUpAxisSign", "int", "Integer", "",1\n'
            '\t\tP: "UnitScaleFactor", "double", "Number", "",1\n'
            '\t\tP: "OriginalUnitScaleFactor", "double", "Number", "",1\n'
            '\t}\n}\n')
    out += ('Documents:  {\n\tCount: 1\n\tDocument: 999999, "", "Scene" {\n\t\tProperties70:  {\n'
            '\t\t\tP: "SourceObject", "object", "", ""\n'
            '\t\t\tP: "ActiveAnimStackName", "KString", "", "", ""\n'
            '\t\t}\n\t\tRootNode: 0\n\t}\n}\n')
    out += "References:  {\n}\n"
    out += ("Definitions:  {\n\tVersion: 100\n"
            f"\tCount: {1 + 1 + n_models + len(partes) + len(MATERIAIS)}\n"
            '\tObjectType: "GlobalSettings" {\n\t\tCount: 1\n\t}\n'
            '\tObjectType: "NodeAttribute" {\n\t\tCount: 1\n\t}\n'
            f'\tObjectType: "Model" {{\n\t\tCount: {n_models}\n\t}}\n'
            f'\tObjectType: "Geometry" {{\n\t\tCount: {len(partes)}\n\t}}\n'
            f'\tObjectType: "Material" {{\n\t\tCount: {len(MATERIAIS)}\n\t}}\n'
            "}\n")
    out += "Objects:  {\n" + "".join(objetos) + "}\n"
    out += "Connections:  {\n" + "".join(conexoes) + "}\n"

    with open(caminho, "w", encoding="ascii", newline="\n") as f:
        f.write(out)


# ── OBJ (contingência) ─────────────────────────────────────────────────────

def escrever_obj(caminho, partes):
    """No OBJ não há pivô por objeto: os parafusos ficam com pivô na origem do HD."""
    mtl_nome = os.path.splitext(os.path.basename(caminho))[0] + ".mtl"
    with open(os.path.join(os.path.dirname(caminho), mtl_nome), "w", encoding="ascii", newline="\n") as f:
        for nome, (r, g, b) in MATERIAIS.items():
            f.write(f"newmtl {nome}\nKd {fmt(r)} {fmt(g)} {fmt(b)}\nKs 0.2 0.2 0.2\nNs 20\n\n")

    linhas = [f"# Gerado por Tools/gerar_hd_fbx.py (SAVIT)\nmtllib {mtl_nome}\n"]
    v_ofs = n_ofs = 0
    for nome, pivo, mesh in partes:
        linhas.append(f"o {nome}\n")
        for p in mesh.verts:
            linhas.append(f"v {fmt(p[0] + pivo[0])} {fmt(p[1] + pivo[1])} {fmt(p[2] + pivo[2])}\n")
        n_local = 0
        faces = []
        material_atual = None
        for indices, ns, m in mesh.polys:
            if m != material_atual:
                faces.append(f"usemtl {m}\n")
                material_atual = m
            refs = []
            for k, idx in enumerate(indices):
                linhas.append(f"vn {fmt(ns[k][0])} {fmt(ns[k][1])} {fmt(ns[k][2])}\n")
                n_local += 1
                refs.append(f"{v_ofs + idx + 1}//{n_ofs + n_local}")
            faces.append("f " + " ".join(refs) + "\n")
        linhas.extend(faces)
        v_ofs += len(mesh.verts)
        n_ofs += n_local

    with open(caminho, "w", encoding="ascii", newline="\n") as f:
        f.writelines(linhas)


def main():
    parser = argparse.ArgumentParser(description='Gera o modelo 3D do HD SATA 3.5" do SAVIT.')
    parser.add_argument("--obj", action="store_true", help="exporta OBJ em vez de FBX (contingência)")
    args = parser.parse_args()

    os.makedirs(OUT_DIR, exist_ok=True)
    partes = montar_hd()
    if args.obj:
        caminho = os.path.join(OUT_DIR, "HardDrive.obj")
        escrever_obj(caminho, partes)
    else:
        caminho = os.path.join(OUT_DIR, "HardDrive.fbx")
        escrever_fbx(caminho, partes)
    print(f"Gerado: {os.path.relpath(caminho, REPO)} ({len(partes)} objetos)")


if __name__ == "__main__":
    main()

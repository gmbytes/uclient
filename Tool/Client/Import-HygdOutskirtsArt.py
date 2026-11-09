from pathlib import Path
import argparse, collections, json, os, re, subprocess, sys

parser = argparse.ArgumentParser(description="Import original HYGD outskirts art and its GUID dependencies.")
parser.add_argument("--source", type=Path, default=Path(r"E:/hygd/work/client"))
parser.add_argument("--write", action="store_true", help="Copy the inspected dependency graph into this client.")
args = parser.parse_args()
source = args.source.resolve()
target = Path(__file__).resolve().parents[2]
if not (target / "ProjectSettings/ProjectVersion.txt").is_file():
    raise RuntimeError("The importer must live in Tool/Client inside the Unity client.")
os.chdir(target)
bundle = Path("Assets/Content/CityOutskirts/HYGD")
common = "Assets/Art/Environment/Common/main_map/Prefabs/"
roots = {
    "RuntimeHome": common + "field2_playertown_YG_lv1_07.prefab",
    "RuntimeBase": common + "field2_playertown_YG_base_01.prefab",
    "RuntimeSword": common + "field2_playertown_YG_sword_01.prefab",
    "County": common + "wm_playertown_north_lv1.prefab",
    "Camp": common + "field2_camp_lv2_01.prefab",
    "Fence": common + "PlayerBuilding/wm_playerbuild_fence_01.prefab",
    "FenceLow": common + "PlayerBuilding/wm_playerbuild_fence_02.prefab",
    "Border": "Assets/Art/VFX/Prefab/WorldMap/FX_WorldMap_Occupy_10.prefab",
    "Ground": "Assets/GameMain/Scenes/Map_World_04/Ground/GroundTile_9_21.prefab",
}
for kind in ("wood", "ore", "iron", "food"):
    for level in range(1, 9):
        roots[f"Resource_{kind}_{level}"] = common + f"resource/field_{kind}_lv{level:02d}.prefab"

reference = re.compile(r"guid: ([0-9a-f]{32})")
builtin = re.compile(r"^0{16}[a-f0-9]0{15}$")

def index(root):
    result = subprocess.run(
        ["rg", "--no-heading", "--with-filename", "-g", "*.meta", "^guid: [0-9a-f]{32}$", "Assets"],
        cwd=root, check=True, capture_output=True, encoding="utf-8")
    return {line.rsplit(":guid: ", 1)[1]: Path(line.rsplit(":guid: ", 1)[0]).with_suffix("")
            for line in result.stdout.splitlines() if ":guid: " in line}

def read(path):
    return path.read_text(encoding="utf-8-sig")

def drop_scripts(data):
    docs = re.split(r"(?m)(?=^--- !u!)", data)
    ids = set()
    kept = []
    for doc in docs:
        script = re.match(r"--- !u!114 &(-?\d+)", doc)
        if script:
            ids.add(script[1])
        else:
            kept.append(doc)
    def keep_component(match):
        return "" if match[1] in ids else match[0]
    return re.sub(r"(?m)^  - component: \{fileID: (-?\d+)\}\r?\n", keep_component, "".join(kept))

src = index(source)
dst = index(target)
shader_guid = reference.search(read(target / "Assets/Content/Shaders/OutskirtsSurface.shader.meta"))[1]
queue = collections.deque(Path(p) for p in roots.values())
included = {}
unknown = collections.defaultdict(set)
skipped = collections.Counter()
while queue:
    path = queue.popleft()
    if path in included:
        continue
    data = None
    if path.suffix.lower() in {".prefab", ".mat", ".asset", ".controller", ".anim", ".overridecontroller", ".playable"}:
        with (source / path).open("rb") as stream:
            is_yaml = stream.read(5) == b"%YAML"
        if is_yaml:
            data = read(source / path)
    if path.suffix == ".prefab" and data:
        data = drop_scripts(data)
        data = re.sub(r"(?m)^  m_TagString: .*$", "  m_TagString: Untagged", data)
    if path.suffix == ".mat" and data:
        data = drop_scripts(data)
        data = re.sub(r"(?m)^  m_Shader: .*$", "  m_Shader: {fileID: 4800000, guid: " + shader_guid + ", type: 3}", data)
    included[path] = data
    # External FBX material remaps can introduce source shaders and cross-project materials.
    meta = read(source / (str(path) + ".meta"))
    own = reference.search(meta)[1]
    refs = set(reference.findall(data or "")) | set(reference.findall(meta))
    for guid in refs - {own, shader_guid}:
        if builtin.match(guid) or guid == "0" * 32:
            continue
        ref_path = src.get(guid)
        if ref_path is None:
            unknown[guid].add(str(path))
            continue
        if ref_path.suffix.lower() in {".cs", ".dll", ".shader", ".shadergraph", ".hlsl", ".cginc", ".compute", ".asmdef"}:
            skipped[ref_path.suffix.lower()] += 1
            continue
        if ref_path.suffix.lower() not in {".prefab", ".mat", ".fbx", ".obj", ".asset", ".controller", ".anim", ".overridecontroller", ".playable", ".png", ".tga", ".psd", ".exr", ".jpg", ".jpeg", ".dds", ".cubemap", ".hdr", ".rendertexture", ".wav", ".ogg", ".mp3"}:
            skipped[ref_path.suffix.lower()] += 1
            continue
        if (source / ref_path).is_file():
            queue.append(ref_path)
        else:
            unknown[guid].add(str(path))

print("ROOTS", len(roots))
print("DEPENDENCIES", len(included), collections.Counter(p.suffix.lower() for p in included))
print("BYTES", sum((source / p).stat().st_size for p in included))
print("SKIPPED", dict(skipped))
print("UNRESOLVED", {g: sorted(p)[:2] for g,p in unknown.items()})
if not args.write:
    sys.exit(0)
for path,data in included.items():
    dest = bundle / path.relative_to("Assets")
    meta = read(source / (str(path) + ".meta"))
    guid = reference.search(meta)[1]
    if guid in dst and dst[guid] != dest:
        raise RuntimeError(f"Destination GUID collision: {guid}: {dst[guid]} vs {dest}")
    original = source / path
    with original.open("rb") as stream:
        if stream.read(40).startswith(b"version https://git-lfs.github.com/spec/"):
            raise RuntimeError(f"Unhydrated LFS asset {original}")
for path,data in included.items():
    dest = bundle / path.relative_to("Assets")
    dest.parent.mkdir(parents=True, exist_ok=True)
    if data is None:
        dest.write_bytes((source / path).read_bytes())
    else:
        dest.write_text(data, encoding="utf-8", newline="\n")
    dest.with_name(dest.name + ".meta").write_bytes((source / (str(path) + ".meta")).read_bytes())
report = {"source": str(source), "roots": {key: str(bundle / Path(path).relative_to("Assets")).replace("\\","/") for key,path in roots.items()},
          "assets": [p.as_posix() for p in sorted(included)], "unresolvedSourceReferences": {g: sorted(p) for g,p in unknown.items()}}
Path("Builds/CityOutskirts/hygd-art-import.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")



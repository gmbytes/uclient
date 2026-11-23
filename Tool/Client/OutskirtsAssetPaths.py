"""Project-owned locations for migrated outskirts assets."""
from pathlib import Path


def destination_for(source_path):
    path = Path(source_path)
    parts = path.parts
    suffix = path.suffix.lower()
    terrain = "Ground" in parts or "Map_World" in parts
    effects = "VFX" in parts
    group = "Terrain" if terrain else "Effects" if effects else "Environment"
    scene = next((p.replace("Map_World_", "Map") for p in parts if p.startswith("Map_World_")), None)
    base = Path("Assets/Content")
    if suffix == ".prefab":
        group = "Resources" if "resource" in parts else group
        folder = base / "Prefabs/Outskirts" / group
    elif suffix in {".fbx", ".obj"}:
        folder = base / "Models/Outskirts" / group
    elif suffix == ".mat":
        folder = base / "Materials/Outskirts" / group
    elif suffix in {".anim", ".controller", ".overridecontroller", ".playable"}:
        folder = base / "Animations/Outskirts"
    elif suffix in {".wav", ".ogg", ".mp3"}:
        folder = base / "Audio/Outskirts"
    else:
        folder = base / "Textures/Outskirts" / ("Lightmaps" if suffix == ".exr" else group)
    if suffix in {".fbx", ".prefab"} and "Ground" in parts and scene:
        folder /= scene
    if "Historical2023" in parts:
        folder /= "Legacy"
    return folder / path.name

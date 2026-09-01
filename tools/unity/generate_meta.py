#!/usr/bin/env python3
"""Create Unity .meta files for any asset under unity-project/Assets that lacks one.

Unity normally writes these itself, but the project is edited outside the
Editor often enough (CI, scripts, agents) that new files would otherwise land
without one, and Unity would then invent a fresh GUID on import. That breaks
every scene reference to the file. GUIDs generated here are deterministic
(an MD5 of the asset path) so two clones agree, and the four core scripts
keep the GUIDs the committed scenes already reference.

Usage: python tools/unity/generate_meta.py [--check]
  --check   exit 1 if any .meta is missing instead of creating it
"""

from __future__ import annotations

import hashlib
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "unity-project" / "Assets"

# GUIDs referenced by Assets/Scenes/DSpaceMain.unity. Do not change.
PINNED = {
    "Scripts/Core/ServiceLocator.cs": "a1b2c3d4e5f60001a1b2c3d4e5f60001",
    "Scripts/Core/DSpaceManager.cs": "a1b2c3d4e5f60002a1b2c3d4e5f60002",
    "Scripts/Core/DarknetBootstrap.cs": "a1b2c3d4e5f60003a1b2c3d4e5f60003",
    "Scripts/Core/DSpaceSceneSetup.cs": "a1b2c3d4e5f60004a1b2c3d4e5f60004",
}

FOLDER = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

DEFAULT = """fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

MONO = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

SHADER = """fileFormatVersion: 2
guid: {guid}
ShaderImporter:
  externalObjects: {{}}
  defaultTextures: []
  nonModifiableTextures: []
  preprocessorOverride: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""

ASMDEF = """fileFormatVersion: 2
guid: {guid}
AssemblyDefinitionImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

# Android-only plugin source (Assets/Plugins/Android/*.java)
ANDROID_PLUGIN = """fileFormatVersion: 2
guid: {guid}
PluginImporter:
  externalObjects: {{}}
  serializedVersion: 2
  iconMap: {{}}
  executionOrder: {{}}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Android: Android
    second:
      enabled: 1
      settings: {{}}
  - first:
      Any:
    second:
      enabled: 0
      settings: {{}}
  - first:
      Editor: Editor
    second:
      enabled: 0
      settings:
        DefaultValueInitialized: true
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def guid_for(rel_path: str) -> str:
    if rel_path in PINNED:
        return PINNED[rel_path]
    return hashlib.md5(f"daemon-vision/{rel_path}".encode("utf-8")).hexdigest()


def template_for(path: Path, rel_path: str) -> str:
    if path.is_dir():
        return FOLDER
    suffix = path.suffix.lower()
    if suffix == ".cs":
        return MONO
    if suffix == ".shader":
        return SHADER
    if suffix == ".asmdef":
        return ASMDEF
    if suffix == ".java" and rel_path.startswith("Plugins/Android/"):
        return ANDROID_PLUGIN
    return DEFAULT


def main(argv: list[str]) -> int:
    check_only = "--check" in argv
    created: list[str] = []

    for path in sorted(ASSETS.rglob("*")):
        if path.name.endswith(".meta"):
            continue
        meta = Path(str(path) + ".meta")
        if meta.exists():
            continue

        rel_path = path.relative_to(ASSETS).as_posix()
        if check_only:
            created.append(rel_path)
            continue

        content = template_for(path, rel_path).format(guid=guid_for(rel_path))
        meta.write_text(content, encoding="utf-8", newline="\n")
        created.append(rel_path)

    if check_only:
        if created:
            print("missing .meta files:")
            for c in created:
                print(f"  {c}")
            return 1
        print("all assets have .meta files")
        return 0

    print(f"created {len(created)} .meta file(s)")
    for c in created:
        print(f"  {c}.meta")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

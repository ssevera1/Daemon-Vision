#!/usr/bin/env python3
"""Repository consistency checks that run without a Unity licence.

Checks:
  1. manifest.json and every .asmdef parse as JSON
  2. every asset and folder under unity-project/Assets has a .meta, and no
     .meta is orphaned; GUIDs are unique
  3. the scene files reference the GUIDs of the core scripts they need
  4. port numbers and packet prefixes agree between the Unity app and the
     Android companion app (they were out of sync once; never again)
  5. CLAUDE.md is not tracked
  6. ProjectSettings/ProjectVersion.txt names an editor version
  7. workflow YAML parses
  8. the Gradle wrapper is present
"""

from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UNITY = ROOT / "unity-project"
ASSETS = UNITY / "Assets"
COMPANION = ROOT / "companion-app"

failures: list[str] = []


def ok(msg: str) -> None:
    print(f"ok    {msg}")


def fail(msg: str) -> None:
    failures.append(msg)
    print(f"FAIL  {msg}")


def rel(p: Path) -> str:
    return p.relative_to(ROOT).as_posix()


def check_json() -> None:
    files = [UNITY / "Packages" / "manifest.json", *sorted(ASSETS.rglob("*.asmdef"))]
    for f in files:
        try:
            json.loads(f.read_text(encoding="utf-8"))
            ok(f"json      {rel(f)}")
        except (OSError, ValueError) as e:
            fail(f"{rel(f)}: invalid JSON ({e})")


def check_meta_files() -> dict[str, Path]:
    missing: list[Path] = []
    orphans: list[Path] = []
    guids: dict[str, Path] = {}
    guid_re = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)

    for p in sorted(ASSETS.rglob("*")):
        if p.name.endswith(".meta"):
            target = Path(str(p)[: -len(".meta")])
            if not target.exists():
                orphans.append(p)
                continue
            m = guid_re.search(p.read_text(encoding="utf-8", errors="replace"))
            if not m:
                fail(f"{rel(p)}: no guid line")
                continue
            g = m.group(1)
            if g in guids:
                fail(f"duplicate guid {g}: {rel(guids[g])} and {rel(p)}")
            guids[g] = target
            continue

        if not Path(str(p) + ".meta").exists():
            missing.append(p)

    if missing:
        for p in missing:
            fail(f"missing .meta for {rel(p)} (run tools/unity/generate_meta.py)")
    else:
        ok(f"meta      every asset under {rel(ASSETS)} has a .meta ({len(guids)} guids)")

    for p in orphans:
        fail(f"orphaned .meta {rel(p)}")

    return guids


def check_scene_references(guids: dict[str, Path]) -> None:
    by_asset = {v.relative_to(ASSETS).as_posix(): k for k, v in guids.items()}
    required = {
        "Scenes/DSpaceMain.unity": [
            "Scripts/Core/ServiceLocator.cs",
            "Scripts/Core/DSpaceManager.cs",
            "Scripts/Core/DarknetBootstrap.cs",
            "Scripts/Core/DSpaceSceneSetup.cs",
        ],
    }
    for scene, scripts in required.items():
        scene_path = ASSETS / scene
        if not scene_path.exists():
            fail(f"scene missing: {scene}")
            continue
        text = scene_path.read_text(encoding="utf-8", errors="replace")
        referenced = set(re.findall(r"m_Script: \{fileID: 11500000, guid: ([0-9a-f]{32})", text))
        for script in scripts:
            g = by_asset.get(script)
            if g is None:
                fail(f"{scene}: no .meta guid known for {script}")
            elif g not in referenced:
                fail(f"{scene}: does not reference {script} (guid {g})")
            else:
                ok(f"scene     {scene} -> {script}")


def grab(path: Path, pattern: str) -> str | None:
    m = re.search(pattern, path.read_text(encoding="utf-8", errors="replace"))
    return m.group(1) if m else None


def check_shared_protocol() -> None:
    cs_mesh = grab(ASSETS / "Scripts/Network/MeshNetworkManager.cs", r"DefaultMeshPort\s*=\s*(\d+)")
    cs_disc = grab(ASSETS / "Scripts/Network/PeerDiscovery.cs", r"DefaultDiscoveryPort\s*=\s*(\d+)")
    cs_beacon = grab(ASSETS / "Scripts/Network/PeerDiscovery.cs", r'BeaconPrefix\s*=\s*"([^"]+)"')
    cs_relay = grab(ASSETS / "Scripts/Spatial/CompanionLocationReceiver.cs", r"DefaultPort\s*=\s*(\d+)")
    cs_gps = grab(ASSETS / "Scripts/Spatial/CompanionLocationReceiver.cs", r'PacketPrefix\s*=\s*"([^"]+)"')
    cs_ack = grab(ASSETS / "Scripts/Spatial/CompanionLocationReceiver.cs", r'AckPrefix\s*=\s*"([^"]+)"')

    java = COMPANION / "app/src/main/java/com/daemon/vision/companion/RelayProtocol.java"
    j_relay = grab(java, r"GPS_RELAY_PORT\s*=\s*(\d+)")
    j_disc = grab(java, r"DISCOVERY_PORT\s*=\s*(\d+)")
    j_gps = grab(java, r'GPS_PREFIX\s*=\s*"([^"]+)"')
    j_ack = grab(java, r'ACK_PREFIX\s*=\s*"([^"]+)"')
    j_beacon = grab(java, r'BEACON_PREFIX\s*=\s*"([^"]+)"')

    pairs = [
        ("GPS relay port", cs_relay, j_relay),
        ("discovery port", cs_disc, j_disc),
        ("GPS packet prefix", cs_gps, j_gps),
        ("ACK prefix", cs_ack, j_ack),
        ("beacon prefix", cs_beacon, j_beacon),
    ]
    for label, cs, jv in pairs:
        if cs is None or jv is None:
            fail(f"protocol  could not read {label} (unity={cs!r}, companion={jv!r})")
        elif cs != jv:
            fail(f"protocol  {label} differs: unity={cs} companion={jv}")
        else:
            ok(f"protocol  {label} = {cs}")

    ports = {cs_mesh, cs_disc, cs_relay}
    if None in ports or len(ports) != 3:
        fail(f"protocol  mesh/discovery/relay ports must be three distinct values, got {cs_mesh}, {cs_disc}, {cs_relay}")

    # docs must mention the same numbers
    building = (ROOT / "docs/BUILDING.md").read_text(encoding="utf-8", errors="replace")
    for label, value in (("mesh", cs_mesh), ("discovery", cs_disc), ("relay", cs_relay)):
        if value and value not in building:
            fail(f"docs      docs/BUILDING.md does not mention the {label} port {value}")


def check_claude_md_untracked() -> None:
    try:
        out = subprocess.run(
            ["git", "ls-files", "--", "CLAUDE.md", "**/CLAUDE.md"],
            cwd=ROOT, capture_output=True, text=True, check=False,
        ).stdout.strip()
    except OSError:
        ok("git       not available; skipping CLAUDE.md check")
        return
    if out:
        fail(f"CLAUDE.md is tracked: {out}")
    else:
        ok("git       CLAUDE.md is not tracked")


def check_project_version() -> None:
    p = UNITY / "ProjectSettings/ProjectVersion.txt"
    if not p.exists():
        fail(f"{rel(p)} is missing (Unity Hub and GameCI read the editor version from it)")
        return
    m = re.search(r"^m_EditorVersion:\s*(\d+\.\d+\.\d+[abfp]\d+)\s*$", p.read_text(encoding="utf-8"), re.M)
    if m:
        ok(f"unity     editor version {m.group(1)}")
    else:
        fail(f"{rel(p)}: no m_EditorVersion line")


def check_workflows() -> None:
    try:
        import yaml  # type: ignore
    except ImportError:
        ok("yaml      pyyaml not installed; skipping workflow parse")
        return
    for f in sorted((ROOT / ".github/workflows").glob("*.yml")):
        try:
            doc = yaml.safe_load(f.read_text(encoding="utf-8"))
            # "on" parses as boolean True in YAML 1.1; either key is fine
            if not isinstance(doc, dict) or ("on" not in doc and True not in doc) or "jobs" not in doc:
                fail(f"{rel(f)}: missing on/jobs")
            else:
                ok(f"workflow  {f.name}")
        except yaml.YAMLError as e:
            fail(f"{rel(f)}: {e}")


def check_gradle_wrapper() -> None:
    needed = [
        COMPANION / "gradlew",
        COMPANION / "gradle/wrapper/gradle-wrapper.jar",
        COMPANION / "gradle/wrapper/gradle-wrapper.properties",
        COMPANION / "app/proguard-rules.pro",
    ]
    for p in needed:
        if p.exists():
            ok(f"gradle    {rel(p)}")
        else:
            fail(f"gradle    missing {rel(p)}")

    manifest = COMPANION / "app/src/main/AndroidManifest.xml"
    if re.search(r"<manifest[^>]*\spackage=", manifest.read_text(encoding="utf-8")):
        fail("gradle    AndroidManifest.xml still has a package attribute (AGP 8 rejects it)")


def main() -> int:
    print(f"validate_project: {ROOT}")
    check_json()
    guids = check_meta_files()
    check_scene_references(guids)
    check_shared_protocol()
    check_claude_md_untracked()
    check_project_version()
    check_workflows()
    check_gradle_wrapper()

    print()
    if failures:
        print(f"{len(failures)} problem(s):")
        for f in failures:
            print(f"  - {f}")
        return 1
    print("all checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())

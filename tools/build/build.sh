#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════
# Daemon Vision — Build Script
# ═══════════════════════════════════════════════════════════════════
#
# Usage:  ./build.sh <target>
#
# Targets:
#   quest       — Build Unity APK for Meta Quest (Android XR)
#   androidxr   — Build Unity APK for Android XR glasses
#   phone       — Build Unity APK for Android phone (dev/testing)
#   ios         — Build Unity Xcode project for iOS
#   companion   — Build companion app APK via Gradle
#   all         — Build all targets
#
# Environment variables:
#   UNITY_PATH  — Path to Unity editor (auto-detected if not set)
#   ANDROID_HOME — Android SDK path (required for companion build)
#   BUILD_CONFIG — "debug" or "release" (default: debug)
#

set -euo pipefail

# ─── Configuration ────────────────────────────────────────────────

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
UNITY_PROJECT="$PROJECT_ROOT/unity-project"
COMPANION_PROJECT="$PROJECT_ROOT/companion-app"
BUILDS_DIR="$PROJECT_ROOT/builds"
BUILD_CONFIG="${BUILD_CONFIG:-debug}"
TIMESTAMP="$(date +%Y%m%d_%H%M%S)"

# Unity editor path detection
if [ -z "${UNITY_PATH:-}" ]; then
    if [ -d "/Applications/Unity/Hub/Editor" ]; then
        # macOS Unity Hub
        UNITY_PATH="$(ls -d /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity 2>/dev/null | tail -1)"
    elif [ -d "$HOME/Unity/Hub/Editor" ]; then
        UNITY_PATH="$(ls -d "$HOME/Unity/Hub/Editor"/*/Editor/Unity 2>/dev/null | tail -1)"
    elif [ -d "/c/Program Files/Unity/Hub/Editor" ] || [ -d "C:/Program Files/Unity/Hub/Editor" ]; then
        # Windows (Git Bash / MSYS2)
        WIN_UNITY_BASE="/c/Program Files/Unity/Hub/Editor"
        [ -d "$WIN_UNITY_BASE" ] || WIN_UNITY_BASE="C:/Program Files/Unity/Hub/Editor"
        UNITY_PATH="$(ls -d "$WIN_UNITY_BASE"/*/Editor/Unity.exe 2>/dev/null | tail -1)"
    fi
fi

# ─── Color Output ─────────────────────────────────────────────────

RED='\033[0;31m'
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
BOLD='\033[1m'
NC='\033[0m'

log()   { echo -e "${CYAN}[DSPACE]${NC} $*"; }
ok()    { echo -e "${GREEN}[  OK ]${NC} $*"; }
warn()  { echo -e "${YELLOW}[ WARN]${NC} $*"; }
err()   { echo -e "${RED}[ERROR]${NC} $*"; }

# ─── Helpers ──────────────────────────────────────────────────────

check_unity() {
    if [ -z "${UNITY_PATH:-}" ] || [ ! -f "$UNITY_PATH" ]; then
        err "Unity editor not found."
        err "Set UNITY_PATH environment variable to the Unity executable."
        err "Example: export UNITY_PATH=\"/Applications/Unity/Hub/Editor/6000.1.0f1/Unity.app/Contents/MacOS/Unity\""
        return 1
    fi
    log "Unity: $UNITY_PATH"
}

ensure_builds_dir() {
    mkdir -p "$BUILDS_DIR"
    mkdir -p "$BUILDS_DIR/quest"
    mkdir -p "$BUILDS_DIR/androidxr"
    mkdir -p "$BUILDS_DIR/phone"
    mkdir -p "$BUILDS_DIR/ios"
    mkdir -p "$BUILDS_DIR/companion"
}

unity_build() {
    local build_target="$1"
    local output_path="$2"
    local build_method="$3"
    local extra_args="${4:-}"

    log "Starting Unity build: target=$build_target, config=$BUILD_CONFIG"

    "$UNITY_PATH" \
        -batchmode \
        -nographics \
        -quit \
        -projectPath "$UNITY_PROJECT" \
        -executeMethod "$build_method" \
        -buildTarget "$build_target" \
        -outputPath "$output_path" \
        -logFile "$BUILDS_DIR/${build_target}_build_${TIMESTAMP}.log" \
        $extra_args

    if [ $? -eq 0 ]; then
        ok "Unity build succeeded: $build_target"
    else
        err "Unity build failed: $build_target"
        err "Check log: $BUILDS_DIR/${build_target}_build_${TIMESTAMP}.log"
        return 1
    fi
}

# ─── Build Targets ────────────────────────────────────────────────

build_quest() {
    log "Building for Meta Quest..."
    check_unity || return 1
    ensure_builds_dir

    local output="$BUILDS_DIR/quest/DaemonVision_Quest_${TIMESTAMP}.apk"

    unity_build "Android" "$output" \
        "DaemonVision.Editor.BuildPipeline.BuildQuest" \
        "-define:DSPACE_QUEST"

    if [ -f "$output" ]; then
        local size
        size=$(du -h "$output" | cut -f1)
        ok "Quest APK: $output ($size)"
        # Copy as latest
        cp "$output" "$BUILDS_DIR/quest/DaemonVision_Quest_latest.apk"
    fi
}

build_androidxr() {
    log "Building for Android XR glasses..."
    check_unity || return 1
    ensure_builds_dir

    local output="$BUILDS_DIR/androidxr/DaemonVision_AndroidXR_${TIMESTAMP}.apk"

    unity_build "Android" "$output" \
        "DaemonVision.Editor.BuildPipeline.BuildAndroidXR" \
        "-define:DSPACE_ANDROIDXR"

    if [ -f "$output" ]; then
        local size
        size=$(du -h "$output" | cut -f1)
        ok "Android XR APK: $output ($size)"
        cp "$output" "$BUILDS_DIR/androidxr/DaemonVision_AndroidXR_latest.apk"
    fi
}

build_phone() {
    log "Building for Android phone (dev)..."
    check_unity || return 1
    ensure_builds_dir

    local output="$BUILDS_DIR/phone/DaemonVision_Phone_${TIMESTAMP}.apk"

    unity_build "Android" "$output" \
        "DaemonVision.Editor.BuildPipeline.BuildPhone" \
        "-define:DSPACE_PHONE"

    if [ -f "$output" ]; then
        local size
        size=$(du -h "$output" | cut -f1)
        ok "Phone APK: $output ($size)"
        cp "$output" "$BUILDS_DIR/phone/DaemonVision_Phone_latest.apk"
    fi
}

build_ios() {
    log "Building for iOS..."
    check_unity || return 1
    ensure_builds_dir

    local output="$BUILDS_DIR/ios/DaemonVision_iOS_${TIMESTAMP}"

    unity_build "iOS" "$output" \
        "DaemonVision.Editor.BuildPipeline.BuildIOS" \
        "-define:DSPACE_IOS"

    if [ -d "$output" ]; then
        ok "iOS Xcode project: $output"
        log "Open in Xcode to build IPA: open \"$output/Unity-iPhone.xcworkspace\""
    fi
}

build_companion() {
    log "Building companion app..."
    ensure_builds_dir

    if [ ! -f "$COMPANION_PROJECT/gradlew" ] && [ ! -f "$COMPANION_PROJECT/gradle/wrapper/gradle-wrapper.jar" ]; then
        warn "Gradle wrapper not found. Attempting to use system Gradle."
        local GRADLE_CMD="gradle"
    else
        local GRADLE_CMD="$COMPANION_PROJECT/gradlew"
        chmod +x "$GRADLE_CMD" 2>/dev/null || true
    fi

    local build_task="assembleDebug"
    if [ "$BUILD_CONFIG" = "release" ]; then
        build_task="assembleRelease"
    fi

    log "Running: $GRADLE_CMD $build_task"

    (cd "$COMPANION_PROJECT" && "$GRADLE_CMD" "$build_task" \
        --no-daemon \
        2>&1 | tee "$BUILDS_DIR/companion_build_${TIMESTAMP}.log")

    local apk_path
    if [ "$BUILD_CONFIG" = "release" ]; then
        apk_path="$COMPANION_PROJECT/app/build/outputs/apk/release/app-release.apk"
    else
        apk_path="$COMPANION_PROJECT/app/build/outputs/apk/debug/app-debug.apk"
    fi

    if [ -f "$apk_path" ]; then
        local dest="$BUILDS_DIR/companion/DaemonVision_Companion_${TIMESTAMP}.apk"
        cp "$apk_path" "$dest"
        cp "$apk_path" "$BUILDS_DIR/companion/DaemonVision_Companion_latest.apk"
        local size
        size=$(du -h "$dest" | cut -f1)
        ok "Companion APK: $dest ($size)"
    else
        err "Companion APK not found at: $apk_path"
        err "Check log: $BUILDS_DIR/companion_build_${TIMESTAMP}.log"
        return 1
    fi
}

# ─── Build Summary ────────────────────────────────────────────────

show_summary() {
    echo ""
    echo -e "${BOLD}═══════════════════════════════════════════════════════${NC}"
    echo -e "${CYAN}  DAEMON VISION — BUILD SUMMARY${NC}"
    echo -e "${BOLD}═══════════════════════════════════════════════════════${NC}"
    echo ""
    echo -e "  Timestamp:    ${TIMESTAMP}"
    echo -e "  Config:       ${BUILD_CONFIG}"
    echo -e "  Output dir:   ${BUILDS_DIR}"
    echo ""

    local total_size=0

    for target_dir in quest androidxr phone companion; do
        local latest="$BUILDS_DIR/$target_dir/DaemonVision_*_latest.apk"
        if compgen -G "$latest" > /dev/null 2>&1; then
            local file
            file=$(ls -1 $latest 2>/dev/null | head -1)
            local size
            size=$(du -h "$file" | cut -f1)
            echo -e "  ${GREEN}[OK]${NC}  ${target_dir}:  ${size}"
        else
            echo -e "  ${YELLOW}[--]${NC}  ${target_dir}:  not built"
        fi
    done

    local ios_dir="$BUILDS_DIR/ios"
    if [ -d "$ios_dir" ] && ls "$ios_dir"/DaemonVision_iOS_* > /dev/null 2>&1; then
        echo -e "  ${GREEN}[OK]${NC}  ios:       Xcode project ready"
    else
        echo -e "  ${YELLOW}[--]${NC}  ios:       not built"
    fi

    echo ""
    echo -e "${BOLD}═══════════════════════════════════════════════════════${NC}"
    echo ""
}

# ─── Main ─────────────────────────────────────────────────────────

main() {
    local target="${1:-}"

    if [ -z "$target" ]; then
        echo ""
        echo "Usage: $0 <target>"
        echo ""
        echo "Targets:"
        echo "  quest       Build for Meta Quest"
        echo "  androidxr   Build for Android XR glasses"
        echo "  phone       Build for Android phone (dev)"
        echo "  ios         Build for iOS"
        echo "  companion   Build companion phone app"
        echo "  all         Build all targets"
        echo ""
        echo "Environment:"
        echo "  UNITY_PATH    Path to Unity executable"
        echo "  BUILD_CONFIG  debug (default) or release"
        echo ""
        exit 1
    fi

    log "Daemon Vision Build System"
    log "Target: $target | Config: $BUILD_CONFIG"
    echo ""

    local failed=0

    case "$target" in
        quest)
            build_quest || failed=1
            ;;
        androidxr)
            build_androidxr || failed=1
            ;;
        phone)
            build_phone || failed=1
            ;;
        ios)
            build_ios || failed=1
            ;;
        companion)
            build_companion || failed=1
            ;;
        all)
            build_quest      || { warn "Quest build failed"; failed=1; }
            build_androidxr  || { warn "AndroidXR build failed"; failed=1; }
            build_phone      || { warn "Phone build failed"; failed=1; }
            build_ios        || { warn "iOS build failed"; failed=1; }
            build_companion  || { warn "Companion build failed"; failed=1; }
            ;;
        *)
            err "Unknown target: $target"
            err "Valid targets: quest, androidxr, phone, ios, companion, all"
            exit 1
            ;;
    esac

    show_summary

    if [ $failed -ne 0 ]; then
        err "One or more builds failed. Check logs in $BUILDS_DIR/"
        exit 1
    fi

    ok "All requested builds completed successfully."
}

main "$@"

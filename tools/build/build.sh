#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════
# Daemon Vision - Build Script
# ═══════════════════════════════════════════════════════════════════
#
# Usage:  ./build.sh <target>
#
# Targets:
#   quest       Build Unity APK for Meta Quest (Android XR)
#   androidxr   Build Unity APK for Android XR glasses
#   phone       Build Unity APK for Android phone (dev/testing)
#   ios         Build Unity Xcode project for iOS
#   companion   Build companion app APK via Gradle
#   all         Build all targets
#
# Environment variables:
#   UNITY_PATH   Path to Unity editor (auto-detected if not set)
#   ANDROID_HOME Android SDK path (required for companion build)
#   BUILD_CONFIG "debug" or "release" (default: debug). Read by
#                DaemonVisionBuildConfig.cs to pick development vs release.
#

set -euo pipefail

# ─── Configuration ────────────────────────────────────────────────

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
UNITY_PROJECT="$PROJECT_ROOT/unity-project"
COMPANION_PROJECT="$PROJECT_ROOT/companion-app"
BUILDS_DIR="$PROJECT_ROOT/builds"
BUILD_CONFIG="${BUILD_CONFIG:-debug}"
export BUILD_CONFIG
TIMESTAMP="$(date +%Y%m%d_%H%M%S)"

# Entry points in unity-project/Assets/Editor/DaemonVisionBuildConfig.cs
BUILD_CLASS="DaemonVision.Editor.DaemonVisionBuildConfig"

# Unity editor path detection
if [ -z "${UNITY_PATH:-}" ]; then
    if [ -d "/Applications/Unity/Hub/Editor" ]; then
        # macOS Unity Hub
        UNITY_PATH="$(find /Applications/Unity/Hub/Editor -maxdepth 4 -path '*/Unity.app/Contents/MacOS/Unity' 2>/dev/null | sort | tail -1)"
    elif [ -d "$HOME/Unity/Hub/Editor" ]; then
        UNITY_PATH="$(find "$HOME/Unity/Hub/Editor" -maxdepth 3 -name Unity -type f 2>/dev/null | sort | tail -1)"
    elif [ -d "/c/Program Files/Unity/Hub/Editor" ]; then
        # Windows (Git Bash / MSYS2)
        UNITY_PATH="$(find "/c/Program Files/Unity/Hub/Editor" -maxdepth 3 -name Unity.exe -type f 2>/dev/null | sort | tail -1)"
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
        err "Example: export UNITY_PATH=\"/Applications/Unity/Hub/Editor/2022.3.62f1/Unity.app/Contents/MacOS/Unity\""
        return 1
    fi
    log "Unity: $UNITY_PATH"
}

ensure_builds_dir() {
    mkdir -p "$BUILDS_DIR/quest" "$BUILDS_DIR/androidxr" "$BUILDS_DIR/phone" \
             "$BUILDS_DIR/ios" "$BUILDS_DIR/companion"
}

# unity_build <buildTarget> <outputPath> <method>
unity_build() {
    local build_target="$1"
    local output_path="$2"
    local build_method="$3"
    local log_file="$BUILDS_DIR/${build_target}_build_${TIMESTAMP}.log"

    log "Starting Unity build: target=$build_target, config=$BUILD_CONFIG"

    if "$UNITY_PATH" \
        -batchmode \
        -nographics \
        -quit \
        -projectPath "$UNITY_PROJECT" \
        -executeMethod "$build_method" \
        -buildTarget "$build_target" \
        -outputPath "$output_path" \
        -logFile "$log_file"; then
        ok "Unity build succeeded: $build_target"
    else
        err "Unity build failed: $build_target"
        err "Check log: $log_file"
        return 1
    fi
}

report_apk() {
    local label="$1"
    local output="$2"
    local latest="$3"

    if [ -f "$output" ]; then
        local size
        size=$(du -h "$output" | cut -f1)
        ok "$label APK: $output ($size)"
        cp "$output" "$latest"
    else
        err "$label APK was not produced at $output"
        return 1
    fi
}

# ─── Build Targets ────────────────────────────────────────────────

build_quest() {
    log "Building for Meta Quest..."
    check_unity || return 1
    ensure_builds_dir

    local output="$BUILDS_DIR/quest/DaemonVision_Quest_${TIMESTAMP}.apk"
    unity_build "Android" "$output" "$BUILD_CLASS.BuildMetaQuest" || return 1
    report_apk "Quest" "$output" "$BUILDS_DIR/quest/DaemonVision_Quest_latest.apk"
}

build_androidxr() {
    log "Building for Android XR glasses..."
    check_unity || return 1
    ensure_builds_dir

    local output="$BUILDS_DIR/androidxr/DaemonVision_AndroidXR_${TIMESTAMP}.apk"
    unity_build "Android" "$output" "$BUILD_CLASS.BuildAndroidXR" || return 1
    report_apk "Android XR" "$output" "$BUILDS_DIR/androidxr/DaemonVision_AndroidXR_latest.apk"
}

build_phone() {
    log "Building for Android phone (dev)..."
    check_unity || return 1
    ensure_builds_dir

    local output="$BUILDS_DIR/phone/DaemonVision_Phone_${TIMESTAMP}.apk"
    unity_build "Android" "$output" "$BUILD_CLASS.BuildPhoneAR" || return 1
    report_apk "Phone" "$output" "$BUILDS_DIR/phone/DaemonVision_Phone_latest.apk"
}

build_ios() {
    log "Building for iOS..."
    check_unity || return 1
    ensure_builds_dir

    local output="$BUILDS_DIR/ios/DaemonVision_iOS_${TIMESTAMP}"
    unity_build "iOS" "$output" "$BUILD_CLASS.BuildIOS" || return 1

    if [ -d "$output" ]; then
        ok "iOS Xcode project: $output"
        log "Open in Xcode to build IPA: open \"$output/Unity-iPhone.xcworkspace\""
    fi
}

build_companion() {
    log "Building companion app..."
    ensure_builds_dir

    local gradle_cmd
    if [ -f "$COMPANION_PROJECT/gradlew" ]; then
        gradle_cmd="$COMPANION_PROJECT/gradlew"
        chmod +x "$gradle_cmd" 2>/dev/null || true
    else
        warn "Gradle wrapper not found. Attempting to use system Gradle."
        gradle_cmd="gradle"
    fi

    local build_task="assembleDebug"
    if [ "$BUILD_CONFIG" = "release" ]; then
        build_task="assembleRelease"
    fi

    log "Running: $gradle_cmd $build_task"

    (cd "$COMPANION_PROJECT" && "$gradle_cmd" "$build_task" \
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
        report_apk "Companion" "$dest" "$BUILDS_DIR/companion/DaemonVision_Companion_latest.apk"
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
    echo -e "${CYAN}  DAEMON VISION - BUILD SUMMARY${NC}"
    echo -e "${BOLD}═══════════════════════════════════════════════════════${NC}"
    echo ""
    echo -e "  Timestamp:    ${TIMESTAMP}"
    echo -e "  Config:       ${BUILD_CONFIG}"
    echo -e "  Output dir:   ${BUILDS_DIR}"
    echo ""

    local target_dir latest size
    for target_dir in quest androidxr phone companion; do
        latest=""
        for candidate in "$BUILDS_DIR/$target_dir"/DaemonVision_*_latest.apk; do
            [ -f "$candidate" ] && latest="$candidate" && break
        done

        if [ -n "$latest" ]; then
            size=$(du -h "$latest" | cut -f1)
            echo -e "  ${GREEN}[OK]${NC}  ${target_dir}:  ${size}"
        else
            echo -e "  ${YELLOW}[--]${NC}  ${target_dir}:  not built"
        fi
    done

    local ios_built=""
    for candidate in "$BUILDS_DIR/ios"/DaemonVision_iOS_*; do
        [ -d "$candidate" ] && ios_built="$candidate" && break
    done
    if [ -n "$ios_built" ]; then
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

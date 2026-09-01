#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════
# Daemon Vision - Deployment Script
# ═══════════════════════════════════════════════════════════════════
#
# Usage:  ./deploy.sh <target> [--launch] [--device <serial>] [--apk <path>]
#
# Targets:
#   quest       Deploy D-Space APK to Meta Quest headset
#   phone       Deploy D-Space phone APK to Android device
#   companion   Deploy companion app APK to Android phone
#
# Options:
#   --launch           Launch the app after installation
#   --device <serial>  Target a specific device (adb -s <serial>)
#   --apk <path>       Use a specific APK file instead of latest
#

set -euo pipefail

# ─── Configuration ────────────────────────────────────────────────

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
BUILDS_DIR="$PROJECT_ROOT/builds"

# Package names for each target. The D-Space id must match
# DaemonVisionBuildConfig.BundleIdentifier and ProjectSettings.asset.
declare -A PACKAGE_NAMES=(
    [quest]="com.daemon.vision.dspace"
    [phone]="com.daemon.vision.dspace"
    [companion]="com.daemon.vision.companion"
)

# Launch activities
declare -A LAUNCH_ACTIVITIES=(
    [quest]="com.daemon.vision.dspace/com.unity3d.player.UnityPlayerActivity"
    [phone]="com.daemon.vision.dspace/com.unity3d.player.UnityPlayerActivity"
    [companion]="com.daemon.vision.companion/.CompanionActivity"
)

# adb command as an array so an optional "-s <serial>" survives quoting.
ADB=(adb)

# ─── Color Output ─────────────────────────────────────────────────

RED='\033[0;31m'
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
BOLD='\033[1m'
DIM='\033[2m'
NC='\033[0m'

log()   { echo -e "${CYAN}[DSPACE]${NC} $*"; }
ok()    { echo -e "${GREEN}[  OK ]${NC} $*"; }
warn()  { echo -e "${YELLOW}[ WARN]${NC} $*"; }
err()   { echo -e "${RED}[ERROR]${NC} $*"; }

# ─── Helpers ──────────────────────────────────────────────────────

check_adb() {
    if ! command -v adb &> /dev/null; then
        err "adb not found in PATH."
        err "Install Android SDK Platform Tools or set ANDROID_HOME."
        exit 1
    fi
}

show_device_info() {
    echo ""
    echo -e "${BOLD}─── Device Information ───${NC}"

    local model manufacturer serial android_ver sdk_ver battery
    model=$("${ADB[@]}" shell getprop ro.product.model 2>/dev/null || echo "unknown")
    manufacturer=$("${ADB[@]}" shell getprop ro.product.manufacturer 2>/dev/null || echo "unknown")
    serial=$("${ADB[@]}" get-serialno 2>/dev/null || echo "unknown")
    android_ver=$("${ADB[@]}" shell getprop ro.build.version.release 2>/dev/null || echo "?")
    sdk_ver=$("${ADB[@]}" shell getprop ro.build.version.sdk 2>/dev/null || echo "?")
    battery=$("${ADB[@]}" shell dumpsys battery 2>/dev/null | awk '/level:/ {print $2}' || echo "?")

    echo -e "  Manufacturer:  ${BOLD}${manufacturer}${NC}"
    echo -e "  Model:         ${BOLD}${model}${NC}"
    echo -e "  Serial:        ${DIM}${serial}${NC}"
    echo -e "  Android:       ${android_ver} (SDK ${sdk_ver})"
    echo -e "  Battery:       ${battery:-?}%"
    echo ""
}

find_latest_apk() {
    local target="$1"
    local apk_dir="$BUILDS_DIR/$target"

    if [ ! -d "$apk_dir" ]; then
        err "Build directory not found: $apk_dir"
        err "Run build.sh $target first."
        return 1
    fi

    local latest_apk
    case "$target" in
        quest)     latest_apk="$apk_dir/DaemonVision_Quest_latest.apk" ;;
        phone)     latest_apk="$apk_dir/DaemonVision_Phone_latest.apk" ;;
        companion) latest_apk="$apk_dir/DaemonVision_Companion_latest.apk" ;;
        *)
            err "Unknown target: $target"
            return 1
            ;;
    esac

    if [ ! -f "$latest_apk" ]; then
        err "No APK found: $latest_apk"
        err "Run build.sh $target first."
        return 1
    fi

    echo "$latest_apk"
}

wait_for_device() {
    local timeout=30

    log "Waiting for device..."
    local count=0
    while ! "${ADB[@]}" get-state &>/dev/null; do
        count=$((count + 1))
        if [ $count -ge $timeout ]; then
            err "Timed out waiting for device after ${timeout}s."
            err "Make sure the device is connected and USB debugging is enabled."
            exit 1
        fi
        sleep 1
    done

    local state
    state=$("${ADB[@]}" get-state 2>/dev/null)
    if [ "$state" != "device" ]; then
        err "Device state is '$state', expected 'device'."
        err "Check USB debugging authorization on the device."
        exit 1
    fi

    ok "Device connected."
}

package_installed() {
    local package="$1"
    "${ADB[@]}" shell pm list packages 2>/dev/null | grep -q "^package:${package}$"
}

# ─── Deploy ───────────────────────────────────────────────────────

deploy() {
    local target="$1"
    local apk_path="$2"
    local do_launch="$3"

    local package="${PACKAGE_NAMES[$target]}"
    local activity="${LAUNCH_ACTIVITIES[$target]}"
    local apk_size
    apk_size=$(du -h "$apk_path" | cut -f1)

    echo ""
    echo -e "${BOLD}═══════════════════════════════════════════════════════${NC}"
    echo -e "${CYAN}  DAEMON VISION - DEPLOYING: ${BOLD}${target}${NC}"
    echo -e "${BOLD}═══════════════════════════════════════════════════════${NC}"
    echo ""

    show_device_info

    log "APK:     $apk_path"
    log "Size:    $apk_size"
    log "Package: $package"
    echo ""

    local install_flags=(-r)  # Replace existing
    if package_installed "$package"; then
        log "Package already installed. Will update."
        install_flags+=(-d)   # Allow downgrade
    fi

    log "Installing APK..."
    local install_start
    install_start=$(date +%s)

    if ! "${ADB[@]}" install "${install_flags[@]}" "$apk_path"; then
        err "Installation failed."
        if package_installed "$package"; then
            warn "The package is installed but could not be updated."
            warn "Try: ${ADB[*]} uninstall $package"
        fi
        exit 1
    fi

    local install_end install_time
    install_end=$(date +%s)
    install_time=$((install_end - install_start))
    ok "Installation completed in ${install_time}s."

    log "Verifying installation..."
    if package_installed "$package"; then
        local version
        version=$("${ADB[@]}" shell dumpsys package "$package" 2>/dev/null | awk -F= '/versionName/ {print $2; exit}')
        ok "Verified: $package v${version:-?}"
    else
        err "Verification failed: package not found after installation."
        exit 1
    fi

    log "Granting runtime permissions..."
    local permissions=(
        "android.permission.ACCESS_FINE_LOCATION"
        "android.permission.ACCESS_COARSE_LOCATION"
        "android.permission.CAMERA"
    )
    for perm in "${permissions[@]}"; do
        "${ADB[@]}" shell pm grant "$package" "$perm" 2>/dev/null || true
    done
    ok "Permissions granted."

    if [ "$do_launch" = "true" ]; then
        echo ""
        log "Launching $target..."
        if "${ADB[@]}" shell am start -n "$activity"; then
            ok "App launched: $activity"
        else
            warn "Failed to launch app. You may need to start it manually."
        fi
    fi

    echo ""
    echo -e "${BOLD}─── Deployment Complete ───${NC}"
    echo -e "  Target:   ${BOLD}${target}${NC}"
    echo -e "  Package:  ${package}"
    echo -e "  Status:   ${GREEN}INSTALLED${NC}"
    if [ "$do_launch" = "true" ]; then
        echo -e "  Launched: ${GREEN}YES${NC}"
    fi
    echo ""
}

# ─── Main ─────────────────────────────────────────────────────────

main() {
    local target=""
    local device_serial=""
    local do_launch="false"
    local custom_apk=""

    while [ $# -gt 0 ]; do
        case "$1" in
            quest|phone|companion)
                target="$1"
                ;;
            --launch)
                do_launch="true"
                ;;
            --device)
                shift
                device_serial="${1:-}"
                if [ -z "$device_serial" ]; then
                    err "--device requires a serial number."
                    exit 1
                fi
                ;;
            --apk)
                shift
                custom_apk="${1:-}"
                if [ -z "$custom_apk" ]; then
                    err "--apk requires a file path."
                    exit 1
                fi
                ;;
            --help|-h)
                echo ""
                echo "Usage: $0 <target> [--launch] [--device <serial>] [--apk <path>]"
                echo ""
                echo "Targets:"
                echo "  quest       Deploy to Meta Quest headset"
                echo "  phone       Deploy to Android phone (D-Space app)"
                echo "  companion   Deploy companion app to Android phone"
                echo ""
                echo "Options:"
                echo "  --launch           Launch app after installation"
                echo "  --device <serial>  Target specific device"
                echo "  --apk <path>       Use specific APK file"
                echo ""
                exit 0
                ;;
            *)
                err "Unknown argument: $1"
                err "Run '$0 --help' for usage."
                exit 1
                ;;
        esac
        shift
    done

    if [ -z "$target" ]; then
        err "No target specified."
        echo ""
        echo "Usage: $0 <target> [--launch] [--device <serial>]"
        echo "Targets: quest, phone, companion"
        exit 1
    fi

    if [[ ! -v PACKAGE_NAMES[$target] ]]; then
        err "Invalid target: $target"
        err "Valid targets: quest, phone, companion"
        exit 1
    fi

    check_adb

    if [ -n "$device_serial" ]; then
        ADB+=(-s "$device_serial")
    fi

    wait_for_device

    local apk_path
    if [ -n "$custom_apk" ]; then
        if [ ! -f "$custom_apk" ]; then
            err "APK not found: $custom_apk"
            exit 1
        fi
        apk_path="$custom_apk"
    else
        apk_path=$(find_latest_apk "$target") || exit 1
    fi

    deploy "$target" "$apk_path" "$do_launch"
}

main "$@"

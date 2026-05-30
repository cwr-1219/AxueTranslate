#!/usr/bin/env bash
#
# Build a Debian (.deb) package for SpeedTranslate.Linux.
#
# Usage:
#   ./build-deb.sh                # x86_64 (amd64) build
#   ARCH=arm64 RID=linux-arm64 ./build-deb.sh
#
# Output: ../../ReleaseSetup/axue-translate_<VERSION>_<ARCH>.deb
#
# Requirements: dotnet 8 SDK, dpkg-deb, fakeroot.

set -euo pipefail

# --- Paths --------------------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$PROJECT_DIR/.." && pwd)"
RELEASE_DIR="$REPO_ROOT/ReleaseSetup"

# --- Configuration (override via env) -----------------------------------------
PKG_NAME="${PKG_NAME:-axue-translate}"
VERSION="${VERSION:-1.0.1}"
ARCH="${ARCH:-amd64}"
RID="${RID:-linux-x64}"
CONFIG="${CONFIG:-Release}"

# --- Tooling check ------------------------------------------------------------
need() {
    if ! command -v "$1" >/dev/null 2>&1; then
        echo "ERROR: required tool '$1' is not on PATH." >&2
        echo "       Install with: $2" >&2
        exit 1
    fi
}
need dotnet    "https://dot.net/ or your distro's dotnet-sdk-8.0 package"
need dpkg-deb  "sudo apt-get install dpkg-dev"
need fakeroot  "sudo apt-get install fakeroot"

# --- Workspace ----------------------------------------------------------------
STAGE_ROOT="$(mktemp -d -t axue-deb-XXXXXX)"
trap 'rm -rf "$STAGE_ROOT"' EXIT

PUBLISH_DIR="$STAGE_ROOT/publish"
PKG_DIR="$STAGE_ROOT/pkg"

mkdir -p "$RELEASE_DIR" "$PUBLISH_DIR" "$PKG_DIR"

echo "==> Publishing self-contained .NET 8 binary ($RID, $CONFIG)..."
dotnet publish "$PROJECT_DIR/SpeedTranslate.Linux.csproj" \
    -c "$CONFIG" \
    -r "$RID" \
    --self-contained true \
    -p:PublishSingleFile=false \
    -p:DebugType=none \
    -p:DebugSymbols=false \
    -o "$PUBLISH_DIR" \
    --nologo \
    --verbosity quiet

# --- Layout the deb file tree -------------------------------------------------
# /usr/lib/axue-translate/             — runtime files
# /usr/bin/axue-translate              — wrapper that exec's the binary
# /usr/share/applications/*.desktop    — desktop entry
# /usr/share/icons/hicolor/<size>/apps — icons
# DEBIAN/{control,postinst,postrm}     — package metadata + maintainer scripts
echo "==> Staging package tree at $PKG_DIR..."

INSTALL_DIR="$PKG_DIR/usr/lib/$PKG_NAME"
mkdir -p \
    "$INSTALL_DIR" \
    "$PKG_DIR/usr/bin" \
    "$PKG_DIR/usr/share/applications" \
    "$PKG_DIR/usr/share/icons/hicolor/256x256/apps" \
    "$PKG_DIR/usr/share/icons/hicolor/128x128/apps" \
    "$PKG_DIR/usr/share/icons/hicolor/64x64/apps" \
    "$PKG_DIR/usr/share/icons/hicolor/48x48/apps" \
    "$PKG_DIR/DEBIAN"

cp -a "$PUBLISH_DIR"/. "$INSTALL_DIR/"
chmod 755 "$INSTALL_DIR/AxueTranslate"

install -m 755 "$SCRIPT_DIR/axue-translate.launcher" "$PKG_DIR/usr/bin/$PKG_NAME"
install -m 644 "$SCRIPT_DIR/axue-translate.desktop"  "$PKG_DIR/usr/share/applications/$PKG_NAME.desktop"

install -m 644 "$SCRIPT_DIR/icons/axue-translate.png"     "$PKG_DIR/usr/share/icons/hicolor/256x256/apps/$PKG_NAME.png"
install -m 644 "$SCRIPT_DIR/icons/axue-translate-128.png" "$PKG_DIR/usr/share/icons/hicolor/128x128/apps/$PKG_NAME.png"
install -m 644 "$SCRIPT_DIR/icons/axue-translate-64.png"  "$PKG_DIR/usr/share/icons/hicolor/64x64/apps/$PKG_NAME.png"
install -m 644 "$SCRIPT_DIR/icons/axue-translate-48.png"  "$PKG_DIR/usr/share/icons/hicolor/48x48/apps/$PKG_NAME.png"

install -m 755 "$SCRIPT_DIR/debian/postinst" "$PKG_DIR/DEBIAN/postinst"
install -m 755 "$SCRIPT_DIR/debian/postrm"   "$PKG_DIR/DEBIAN/postrm"

# --- Generate control file from template -------------------------------------
INSTALLED_KB=$(du -sk "$PKG_DIR" --exclude=DEBIAN | awk '{print $1}')
sed \
    -e "s/{{VERSION}}/$VERSION/g" \
    -e "s/{{ARCH}}/$ARCH/g" \
    -e "s/{{SIZE}}/$INSTALLED_KB/g" \
    "$SCRIPT_DIR/debian/control.in" > "$PKG_DIR/DEBIAN/control"

# --- Build the .deb -----------------------------------------------------------
DEB_NAME="${PKG_NAME}_${VERSION}_${ARCH}.deb"
DEB_OUTPUT="$RELEASE_DIR/$DEB_NAME"

echo "==> Building $DEB_NAME ..."
fakeroot dpkg-deb --build --root-owner-group -Zxz "$PKG_DIR" "$DEB_OUTPUT" >/dev/null

echo ""
echo "Built: $DEB_OUTPUT"
echo "Size:  $(du -h "$DEB_OUTPUT" | cut -f1)"
echo ""
echo "Install with:"
echo "  sudo apt install $DEB_OUTPUT"
echo "or:"
echo "  sudo dpkg -i $DEB_OUTPUT && sudo apt-get install -f"

#!/usr/bin/env bash
# Builds Xray-core with the patches in ./patches applied (restores the TLS `allowInsecure` option).
#
# Usage: build-xray.sh <GOOS> <GOARCH> <output-dir>
# Needs git and a Go toolchain (GOTOOLCHAIN=auto fetches the version Xray's go.mod asks for).
set -euo pipefail

GOOS_TARGET="$1"
GOARCH_TARGET="$2"
OUT_DIR="$3"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
XRAY_VERSION="$(tr -d '[:space:]' < "$HERE/XRAY_VERSION")"
SRC="${XRAY_SRC_DIR:-${RUNNER_TEMP:-/tmp}/xray-core-src}"

if [[ ! -d "$SRC/.git" ]]; then
  git clone --depth 1 --branch "$XRAY_VERSION" https://github.com/XTLS/Xray-core.git "$SRC"
  git -C "$SRC" apply "$HERE"/patches/*.patch
fi

EXE=xray
[[ "$GOOS_TARGET" == windows ]] && EXE=xray.exe
mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"

cd "$SRC"
export GOTOOLCHAIN="${GOTOOLCHAIN:-auto}"
BUILD_ID="${XRAY_VERSION}-allowInsecure"
CGO_ENABLED=0 GOOS="$GOOS_TARGET" GOARCH="$GOARCH_TARGET" \
  go build -o "$OUT_DIR/$EXE" -trimpath -buildvcs=false -gcflags="all=-l=4" \
  -ldflags="-X github.com/xtls/xray-core/core.build=${BUILD_ID} -s -w -buildid=" ./main

echo "Built $OUT_DIR/$EXE (Xray $XRAY_VERSION, $GOOS_TARGET/$GOARCH_TARGET)"

#!/usr/bin/env bash
# Dragonfire — bash equivalent of pack-all.ps1
# Builds and packs every Dragonfire library to ./artifacts/ at one shared version.
#
# Usage:
#   ./pack-all.sh                      # use Version from Directory.Build.props
#   ./pack-all.sh -v 1.4.0             # override version
#   ./pack-all.sh -v 1.4.0 --push      # pack and push to NuGet (NUGET_API_KEY required)

set -euo pipefail

# ---------------------------------------------------------------------------
# Args
# ---------------------------------------------------------------------------
VERSION=""
CONFIG="Release"
OUTDIR="$(cd "$(dirname "$0")" && pwd)/artifacts"
NO_BUILD=0
PUSH=0
API_KEY="${NUGET_API_KEY:-}"
SOURCE="https://api.nuget.org/v3/index.json"

while [[ $# -gt 0 ]]; do
  case "$1" in
    -v|--version)        VERSION="$2"; shift 2 ;;
    -c|--configuration)  CONFIG="$2"; shift 2 ;;
    -o|--output)         OUTDIR="$2"; shift 2 ;;
    --no-build)          NO_BUILD=1; shift ;;
    --push)              PUSH=1; shift ;;
    --api-key)           API_KEY="$2"; shift 2 ;;
    --source)            SOURCE="$2"; shift 2 ;;
    -h|--help)
      grep -E '^#( |!)' "$0" | sed 's/^# \{0,1\}//' | head -20
      exit 0 ;;
    *)
      echo "Unknown argument: $1" >&2; exit 1 ;;
  esac
done

cd "$(dirname "$0")"

# ---------------------------------------------------------------------------
# Resolve version from Directory.Build.props if not overridden.
# ---------------------------------------------------------------------------
if [[ -z "$VERSION" ]]; then
  VERSION=$(grep -oE '<Version>[^<]+</Version>' Directory.Build.props | head -1 | sed -E 's|</?Version>||g')
  if [[ -z "$VERSION" ]]; then
    echo "Could not read <Version> from Directory.Build.props" >&2
    exit 1
  fi
fi

echo ""
echo "============================================================"
echo "  Dragonfire pack-all — version $VERSION"
echo "============================================================"
echo ""

# ---------------------------------------------------------------------------
# Discover packable projects:
#   - skip tests/, samples/, bin/, obj/
#   - skip any *Sample*.csproj or *SampleApp.csproj
#   - skip projects with <IsPackable>false</IsPackable>
# ---------------------------------------------------------------------------
mapfile -t projects < <(
  find . -name "*.csproj" \
    -not -path "*/tests/*" -not -path "*/samples/*" \
    -not -path "*/bin/*"   -not -path "*/obj/*" \
  | while read -r f; do
      base=$(basename "$f")
      case "$base" in
        *Sample.csproj|*SampleApp.csproj) continue ;;
      esac
      if ! grep -qE '<IsPackable>\s*false\s*</IsPackable>' "$f"; then
        echo "$f"
      fi
    done | sort
)

if [[ ${#projects[@]} -eq 0 ]]; then
  echo "No packable projects discovered." >&2
  exit 1
fi

echo "Discovered ${#projects[@]} packable project(s):"
for p in "${projects[@]}"; do
  echo "  - $p"
done
echo ""

# ---------------------------------------------------------------------------
# Prepare output directory.
# ---------------------------------------------------------------------------
mkdir -p "$OUTDIR"
rm -f "$OUTDIR"/*.nupkg "$OUTDIR"/*.snupkg

# ---------------------------------------------------------------------------
# Build (unless skipped).
# ---------------------------------------------------------------------------
if [[ $NO_BUILD -eq 0 ]]; then
  echo "-- Build (Release) --"
  for p in "${projects[@]}"; do
    dotnet build "$p" -c "$CONFIG" --nologo --verbosity quiet -p:Version="$VERSION"
  done
fi

# ---------------------------------------------------------------------------
# Pack.
# ---------------------------------------------------------------------------
echo "-- Pack --"
for p in "${projects[@]}"; do
  name=$(basename "$p" .csproj)
  echo "  -> $name"
  pack_args=(pack "$p" -c "$CONFIG" -o "$OUTDIR" --nologo --verbosity quiet "-p:Version=$VERSION")
  if [[ $NO_BUILD -eq 1 ]]; then
    pack_args+=(--no-build)
  fi
  dotnet "${pack_args[@]}"
done

# ---------------------------------------------------------------------------
# Summary.
# ---------------------------------------------------------------------------
echo ""
mapfile -t nupkgs < <(find "$OUTDIR" -maxdepth 1 -name "*.nupkg" | sort)
echo "-- Produced ${#nupkgs[@]} package(s) in $OUTDIR --"
for f in "${nupkgs[@]}"; do
  echo "  ok $(basename "$f")"
done
echo ""

# ---------------------------------------------------------------------------
# Optional push.
# ---------------------------------------------------------------------------
if [[ $PUSH -eq 1 ]]; then
  if [[ -z "$API_KEY" ]]; then
    echo "Push requested but no API key supplied. Pass --api-key or set NUGET_API_KEY." >&2
    exit 1
  fi
  echo "-- Pushing ${#nupkgs[@]} package(s) to $SOURCE --"
  for pkg in "${nupkgs[@]}"; do
    echo "  -> $(basename "$pkg")"
    dotnet nuget push "$pkg" --api-key "$API_KEY" --source "$SOURCE" --skip-duplicate
  done
  echo ""
  echo "Pushed ${#nupkgs[@]} package(s) at version $VERSION."
fi

echo "Done."

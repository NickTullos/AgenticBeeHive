#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_PATH="${SOLUTION_DIR}/src/ABHive.Web/ABHive.Web.csproj"
VERSION_FILE="${SOLUTION_DIR}/version.json"
OUT_DIR="${SCRIPT_DIR}/out"
DIST_DIR="${SCRIPT_DIR}/dist"
PACK_DIR="${DIST_DIR}/.pack"

RIDS=("win-x64" "win-arm64" "linux-x64" "linux-arm64" "osx-x64" "osx-arm64")

if [[ ! -f "${VERSION_FILE}" ]]; then
  echo "Missing version file: ${VERSION_FILE}" >&2
  exit 1
fi

VERSION="$(sed -nE 's/^[[:space:]]*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/p' "${VERSION_FILE}" | head -n 1)"
if [[ -z "${VERSION}" ]]; then
  echo "Failed to read version from ${VERSION_FILE}" >&2
  exit 1
fi

mkdir -p "${OUT_DIR}" "${DIST_DIR}"
rm -rf "${OUT_DIR:?}/"*
rm -rf "${DIST_DIR:?}/"*
mkdir -p "${PACK_DIR}"

for rid in "${RIDS[@]}"; do
  os="${rid%-*}"
  arch="${rid##*-}"
  publish_dir="${OUT_DIR}/${rid}"
  zip_name="agenticbeehive-v${VERSION}-build-${os}-${arch}.zip"
  zip_base="${zip_name%.zip}"
  zip_path="${DIST_DIR}/${zip_name}"
  package_root="${PACK_DIR}/${zip_base}"

  echo "Publishing ${rid}..."
  dotnet publish "${PROJECT_PATH}" \
    -c Release \
    -r "${rid}" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -o "${publish_dir}"

  if [[ -f "${publish_dir}/abHive.Web" ]]; then
    chmod +x "${publish_dir}/abHive.Web"
  fi

  if [[ "${os}" != "win" ]]; then
    cat > "${publish_dir}/start-abhive.sh" <<'EOF'
#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "${SCRIPT_DIR}"

if [[ ! -x "./abHive.Web" && -f "./abHive.Web" ]]; then
  chmod +x "./abHive.Web" 2>/dev/null || true
fi

exec ./abHive.Web
EOF
    chmod +x "${publish_dir}/start-abhive.sh"
  fi

  if [[ "${os}" == "osx" ]]; then
    cat > "${publish_dir}/start-abhive.command" <<'EOF'
#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "${SCRIPT_DIR}"

if [[ ! -x "./abHive.Web" && -f "./abHive.Web" ]]; then
  chmod +x "./abHive.Web" 2>/dev/null || true
fi

./abHive.Web
EXIT_CODE=$?
echo
echo "ABHive exited with code ${EXIT_CODE}. Press Enter to close."
read -r _
exit ${EXIT_CODE}
EOF
    chmod +x "${publish_dir}/start-abhive.command"
  fi

  echo "Packaging ${zip_name}..."
  rm -rf "${package_root}"
  mkdir -p "${package_root}"
  cp -R "${publish_dir}/." "${package_root}/"
  (cd "${PACK_DIR}" && zip -qr "${zip_path}" "${zip_base}")
done

checksum_cmd="sha256sum"
if ! command -v sha256sum >/dev/null 2>&1; then
  checksum_cmd="shasum -a 256"
fi

manifest_path="${DIST_DIR}/release-manifest.txt"
{
  echo "appName=Agentic BeeHive"
  echo "version=${VERSION}"
  echo "generatedAtUtc=$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
  echo
  echo "sha256 filename"
} > "${manifest_path}"

for rid in "${RIDS[@]}"; do
  expected_asset="$(sed -nE "s/^[[:space:]]*\"${rid}\"[[:space:]]*:[[:space:]]*\"([^\"]+)\".*/\\1/p" "${VERSION_FILE}" | head -n 1)"
  os="${rid%-*}"
  arch="${rid##*-}"
  zip_name="agenticbeehive-v${VERSION}-build-${os}-${arch}.zip"
  zip_path="${DIST_DIR}/${zip_name}"

  if [[ ! -f "${zip_path}" ]]; then
    echo "Missing expected artifact: ${zip_path}" >&2
    exit 1
  fi

  if [[ -n "${expected_asset}" && "${expected_asset}" != "${zip_name}" ]]; then
    echo "Asset name mismatch for ${rid}: expected '${expected_asset}' but built '${zip_name}'." >&2
    exit 1
  fi

  checksum="$(${checksum_cmd} "${zip_path}" | awk '{print $1}')"
  echo "${checksum} ${zip_name}" >> "${manifest_path}"
done

echo "Release build complete."
echo "Artifacts: ${DIST_DIR}"
echo "Manifest: ${manifest_path}"
rm -rf "${PACK_DIR}"

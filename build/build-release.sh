#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_PATH="${SOLUTION_DIR}/src/ABHive.Web/ABHive.Web.csproj"
VERSION_FILE="${SOLUTION_DIR}/src/version.json"
OUT_DIR="${SCRIPT_DIR}/out"
DIST_DIR="${SCRIPT_DIR}/dist"
PACK_DIR="${DIST_DIR}/.pack"

RIDS=("win-x64" "win-arm64" "linux-x64" "linux-arm64" "osx-x64" "osx-arm64")

map_os_label_from_rid_os() {
  local rid_os="$1"
  case "${rid_os}" in
    win) echo "windows" ;;
    linux) echo "linux" ;;
    osx) echo "macos" ;;
    *) return 1 ;;
  esac
}

expected_asset_for_rid() {
  local rid="$1"
  local rid_os="${rid%-*}"
  local arch="${rid##*-}"
  local os_label
  if ! os_label="$(map_os_label_from_rid_os "${rid_os}")"; then
    return 1
  fi
  printf "agenticbeehive-v%s-%s-%s.zip" "${VERSION}" "${os_label}" "${arch}"
}

if [[ ! -f "${VERSION_FILE}" ]]; then
  echo "Missing version file: ${VERSION_FILE}" >&2
  exit 1
fi

VERSION="$(sed -nE 's/^[[:space:]]*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/p' "${VERSION_FILE}" | head -n 1)"
if [[ -z "${VERSION}" ]]; then
  echo "Failed to read version from ${VERSION_FILE}" >&2
  exit 1
fi

semver_regex='^([0-9]|[1-9][0-9]*)\.([0-9]|[1-9][0-9]*)\.([0-9]|[1-9][0-9]*)(-([0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*))?(\+([0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*))?$'
if [[ ! "${VERSION}" =~ ${semver_regex} ]]; then
  echo "Version '${VERSION}' in ${VERSION_FILE} is not valid SemVer." >&2
  exit 1
fi

SEMVER_MAJOR="${BASH_REMATCH[1]}"
SEMVER_MINOR="${BASH_REMATCH[2]}"
SEMVER_PATCH="${BASH_REMATCH[3]}"
ASSEMBLY_FILE_VERSION="${SEMVER_MAJOR}.${SEMVER_MINOR}.${SEMVER_PATCH}.0"
INFORMATIONAL_VERSION="${VERSION}"

echo "[version] SemVer=${VERSION}"
echo "[version] AssemblyVersion/FileVersion=${ASSEMBLY_FILE_VERSION}"

if ! grep -Eq '"assets"[[:space:]]*:' "${VERSION_FILE}"; then
  echo "Missing required 'assets' object in ${VERSION_FILE}" >&2
  exit 1
fi

sync_rids=()
sync_old_assets=()
sync_new_assets=()

for rid in "${RIDS[@]}"; do
  expected_asset="$(expected_asset_for_rid "${rid}")" || {
    echo "Unsupported RID '${rid}' while computing expected asset name." >&2
    exit 1
  }
  current_asset="$(sed -nE "s/^[[:space:]]*\"${rid}\"[[:space:]]*:[[:space:]]*\"([^\"]+)\".*/\\1/p" "${VERSION_FILE}" | head -n 1)"
  if [[ -z "${current_asset}" ]]; then
    echo "Missing required assets entry for '${rid}' in ${VERSION_FILE}" >&2
    exit 1
  fi
  if [[ "${current_asset}" != "${expected_asset}" ]]; then
    sync_rids+=("${rid}")
    sync_old_assets+=("${current_asset}")
    sync_new_assets+=("${expected_asset}")
  fi
done

if [[ "${#sync_rids[@]}" -gt 0 ]]; then
  echo "[version-sync] Synchronizing asset names in ${VERSION_FILE} for version ${VERSION}..."
  tmp_file="${VERSION_FILE}.sync.tmp"
  cp "${VERSION_FILE}" "${tmp_file}"

  for rid in "${RIDS[@]}"; do
    expected_asset="$(expected_asset_for_rid "${rid}")"
    if ! grep -Eq "^[[:space:]]*\"${rid}\"[[:space:]]*:[[:space:]]*\"" "${tmp_file}"; then
      echo "Failed to find assets entry '${rid}' while syncing ${VERSION_FILE}" >&2
      rm -f "${tmp_file}"
      exit 1
    fi

    next_file="${tmp_file}.next"
    sed -E "s#(^[[:space:]]*\"${rid}\"[[:space:]]*:[[:space:]]*\")[^\"]*(\"[[:space:]]*,?[[:space:]]*$)#\\1${expected_asset}\\2#" "${tmp_file}" > "${next_file}"
    mv "${next_file}" "${tmp_file}"
  done

  mv "${tmp_file}" "${VERSION_FILE}"

  for rid in "${RIDS[@]}"; do
    expected_asset="$(expected_asset_for_rid "${rid}")"
    actual_asset="$(sed -nE "s/^[[:space:]]*\"${rid}\"[[:space:]]*:[[:space:]]*\"([^\"]+)\".*/\\1/p" "${VERSION_FILE}" | head -n 1)"
    if [[ "${actual_asset}" != "${expected_asset}" ]]; then
      echo "Failed to sync assets entry '${rid}' in ${VERSION_FILE}: expected '${expected_asset}', found '${actual_asset}'." >&2
      exit 1
    fi
  done

  for i in "${!sync_rids[@]}"; do
    echo "[version-sync] ${sync_rids[$i]}: '${sync_old_assets[$i]}' -> '${sync_new_assets[$i]}'"
  done
else
  echo "[version-sync] Assets already aligned for version ${VERSION}."
fi

mkdir -p "${OUT_DIR}" "${DIST_DIR}"
rm -rf "${OUT_DIR:?}/"*
rm -rf "${DIST_DIR:?}/"*
mkdir -p "${PACK_DIR}"

for rid in "${RIDS[@]}"; do
  rid_os="${rid%-*}"
  arch="${rid##*-}"
  if ! os_label="$(map_os_label_from_rid_os "${rid_os}")"; then
    echo "Unsupported RID OS segment '${rid_os}' for RID '${rid}'." >&2
    exit 1
  fi
  publish_dir="${OUT_DIR}/${rid}"
  dist_os_dir="${DIST_DIR}/${os_label}"
  zip_name="$(expected_asset_for_rid "${rid}")"
  zip_base="${zip_name%.zip}"
  zip_path="${dist_os_dir}/${zip_name}"
  package_root="${PACK_DIR}/${zip_base}"

  echo "Publishing ${rid}..."
  dotnet publish "${PROJECT_PATH}" \
    -c Release \
    -r "${rid}" \
    --self-contained true \
    -p:Version="${VERSION}" \
    -p:InformationalVersion="${INFORMATIONAL_VERSION}" \
    -p:AssemblyVersion="${ASSEMBLY_FILE_VERSION}" \
    -p:FileVersion="${ASSEMBLY_FILE_VERSION}" \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=false \
    -o "${publish_dir}"

  if [[ -f "${publish_dir}/abHive.Web" ]]; then
    chmod +x "${publish_dir}/abHive.Web"
  fi

  if [[ "${rid_os}" != "win" ]]; then
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

  if [[ "${rid_os}" == "osx" ]]; then
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

  rm -rf "${publish_dir}/logs"
  rm -rf "${publish_dir}/schedule"
  rm -rf "${publish_dir}/Schedule"
  rm -f "${publish_dir}"/*.pdb
  find "${publish_dir}" -type f -name ".DS_Store" -delete

  echo "Packaging ${zip_name}..."
  mkdir -p "${dist_os_dir}"
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
  echo "sha256 path"
} > "${manifest_path}"

for rid in "${RIDS[@]}"; do
  expected_asset="$(sed -nE "s/^[[:space:]]*\"${rid}\"[[:space:]]*:[[:space:]]*\"([^\"]+)\".*/\\1/p" "${VERSION_FILE}" | head -n 1)"
  rid_os="${rid%-*}"
  if ! os_label="$(map_os_label_from_rid_os "${rid_os}")"; then
    echo "Unsupported RID OS segment '${rid_os}' for RID '${rid}'." >&2
    exit 1
  fi
  zip_name="$(expected_asset_for_rid "${rid}")"
  zip_path="${DIST_DIR}/${os_label}/${zip_name}"
  relative_path="${os_label}/${zip_name}"

  if [[ ! -f "${zip_path}" ]]; then
    echo "Missing expected artifact: ${zip_path}" >&2
    exit 1
  fi

  if [[ -n "${expected_asset}" && "${expected_asset}" != "${zip_name}" ]]; then
    echo "Asset name mismatch for ${rid}: expected '${expected_asset}' but built '${zip_name}'." >&2
    exit 1
  fi

  checksum="$(${checksum_cmd} "${zip_path}" | awk '{print $1}')"
  echo "${checksum} ${relative_path}" >> "${manifest_path}"
done

echo "Release build complete."
echo "Artifacts: ${DIST_DIR}"
echo "Manifest: ${manifest_path}"
rm -rf "${PACK_DIR}"

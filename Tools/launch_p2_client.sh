#!/usr/bin/env bash
set -euo pipefail

steam_root="${HOME}/.local/share/Steam"
proton="${steam_root}/compatibilitytools.d/GE-Proton10-34/proton"
compat_data="${steam_root}/steamapps/compatdata/1449850"
p2_dir="${1:-/run/media/rakeenhuq/New Volume/SteamLibrary/steamapps/common/Yu-Gi-Oh!  Master Duel/YgoMasterP2}"
log_file="${YGO_P2_LOG:-/tmp/ygomaster-p2-client.log}"

if [[ ! -x "${proton}" ]]; then
    proton="${HOME}/.steam/root/compatibilitytools.d/GE-Proton10-34/proton"
fi

if [[ ! -x "${proton}" ]]; then
    echo "missing GE-Proton10-34 proton executable: ${proton}" >&2
    exit 1
fi
if [[ ! -d "${p2_dir}" ]]; then
    echo "missing P2 client directory: ${p2_dir}" >&2
    exit 1
fi
if [[ ! -f "${p2_dir}/MonoRun.exe" ]]; then
    echo "missing P2 MonoRun.exe: ${p2_dir}/MonoRun.exe" >&2
    exit 1
fi

is_p2_running() {
    local args
    while IFS= read -r args; do
        case "${args}" in
            *"${p2_dir}/MonoRun.exe YgoMasterClient.exe"*)
                case "${args}" in
                    *"launch_p2_client.sh"*|*"grep -F"*)
                        ;;
                    *)
                        return 0
                        ;;
                esac
                ;;
        esac
    done < <(ps -eo args=)
    return 1
}

if is_p2_running; then
    echo "P2 client already running for ${p2_dir}"
    exit 0
fi

mkdir -p "${compat_data}"
cd "${p2_dir}"

setsid env \
    STEAM_COMPAT_CLIENT_INSTALL_PATH="${steam_root}" \
    STEAM_COMPAT_DATA_PATH="${compat_data}" \
    STEAM_COMPAT_APP_ID=1449850 \
    SteamAppId=1449850 \
    SteamGameId=1449850 \
    PROTON_LOG="${PROTON_LOG:-1}" \
    "${proton}" run "${p2_dir}/MonoRun.exe" YgoMasterClient.exe \
    </dev/null >"${log_file}" 2>&1 &

echo "started P2 client for ${p2_dir}; log: ${log_file}"

#!/usr/bin/env bash
set -euo pipefail

steam_root="${HOME}/.local/share/Steam"
proton="${steam_root}/compatibilitytools.d/GE-Proton10-34/proton"
compat_data="${steam_root}/steamapps/compatdata/1449850"
game_dir="/run/media/rakeenhuq/New Volume/SteamLibrary/steamapps/common/Yu-Gi-Oh!  Master Duel/YgoMaster"

if [[ ! -x "${proton}" ]]; then
    proton="${HOME}/.steam/root/compatibilitytools.d/GE-Proton10-34/proton"
fi

export STEAM_COMPAT_CLIENT_INSTALL_PATH="${steam_root}"
export STEAM_COMPAT_DATA_PATH="${compat_data}"

mkdir -p "${compat_data}"
cd "${game_dir}"

start_server_and_forwarders() {
    "${proton}" run "${game_dir}/MonoRun.exe" YgoMaster.exe &
    sleep 3

    systemctl --user stop \
        ygomaster-forward-4989.service \
        ygomaster-forward-4988.service >/dev/null 2>&1 || true

    lan_ip="$(ip -4 route get 1.1.1.1 2>/dev/null | awk '{for (i = 1; i <= NF; i++) if ($i == "src") {print $(i + 1); exit}}')"
    if [[ -n "${lan_ip}" ]] && command -v socat >/dev/null 2>&1; then
        systemd-run --user --unit=ygomaster-forward-4989 --collect \
            socat "TCP-LISTEN:4989,bind=${lan_ip},reuseaddr,fork" TCP:127.0.0.1:4989 >/dev/null
        systemd-run --user --unit=ygomaster-forward-4988 --collect \
            socat "TCP-LISTEN:4988,bind=${lan_ip},reuseaddr,fork" TCP:127.0.0.1:4988 >/dev/null
    fi
}

(
    sleep 5
    start_server_and_forwarders
) &

exec "${proton}" waitforexitandrun "${game_dir}/MonoRun.exe" YgoMasterClient.exe

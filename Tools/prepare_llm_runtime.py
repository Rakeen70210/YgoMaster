#!/usr/bin/env python3
import argparse
import json
import re
import shutil
import socket
import subprocess
import sys
from datetime import datetime
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urlparse, urlunparse
from urllib.request import Request, urlopen


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_BUILD_DIR = Path("/tmp/ygomaster-build/solution")
DEFAULT_BROKER_TIMEOUT_MS = 55000
RUNTIME_BINARIES = ("YgoMaster.exe", "YgoMasterClient.exe")
STATUS_SETTINGS_KEYS = (
    "PvpLogToFile",
    "LlmDecisionLogEnabled",
    "LlmBrokerEnabled",
    "LlmBrokerUrl",
    "LlmBrokerTimeoutMs",
    "LlmBrokerControlPlayer",
    "LlmSelfResourcesAuditEnabled",
    "LlmPlanningSearchAuditEnabled",
    "LlmSearchMaxStrategicDepth",
    "LlmSearchMaxNodes",
    "LlmSearchBeamWidth",
    "LlmSearchMaxWallMs",
    "LlmSearchMaxSerializedBytes",
)
# Known optional keys that may be absent from older deployed ClientSettings.json.
# update_settings_text may insert these (once) when a write explicitly targets them.
MIGRATABLE_OPTIONAL_SETTINGS = frozenset(
    {
        "LlmSelfResourcesAuditEnabled",
        "LlmPlanningSearchAuditEnabled",
        "LlmSearchMaxStrategicDepth",
        "LlmSearchMaxNodes",
        "LlmSearchBeamWidth",
        "LlmSearchMaxWallMs",
        "LlmSearchMaxSerializedBytes",
    }
)
# Plan defaults (YGOMASTER-LLM-005 Slice 2A). Broker enable never implies these.
LLM_SEARCH_LIMIT_DEFAULTS = {
    "LlmSearchMaxStrategicDepth": 4,
    "LlmSearchMaxNodes": 96,
    "LlmSearchBeamWidth": 12,
    "LlmSearchMaxWallMs": 500,
    "LlmSearchMaxSerializedBytes": 16384,
}
LLM_SETTING_INSERT_ANCHORS = (
    "LlmBrokerControlPlayer",
    "LlmBrokerTimeoutMs",
    "LlmBrokerUrl",
    "LlmBrokerEnabled",
    "LlmDecisionLogEnabled",
)
GAME_PROCESS_PATTERNS = (
    re.compile(r"masterduel\.exe(?:\s|$)", re.IGNORECASE),
    re.compile(r"MonoRun\.exe\s+YgoMaster(?:Client)?\.exe(?:\s|$)", re.IGNORECASE),
    re.compile(r"^\s*\d+\s+(?:[A-Za-z]:)?[^\s]*[\\/]YgoMaster(?:Client)?\.exe(?:\s|$)", re.IGNORECASE),
    re.compile(r"^\s*\d+\s+YgoMaster(?:Client)?\.exe(?:\s|$)", re.IGNORECASE),
    re.compile(r"^\s*\d+\s+(?:[^\s]*[\\/])?wine(?:64)?(?:\.exe)?\s+YgoMaster(?:Client)?\.exe(?:\s|$)", re.IGNORECASE),
    re.compile(r"^\s*\d+\s+.*(?:^|\s|[\\/])proton\s+run\s+(?:[A-Za-z]:)?[^\s]*[\\/]YgoMaster(?:Client)?\.exe(?:\s|$)", re.IGNORECASE),
)
IGNORED_PROCESS_BASENAMES = {
    "cat",
    "grep",
    "less",
    "nvim",
    "rg",
    "ripgrep",
    "sed",
    "vi",
    "vim",
}


def jsonc_literal(value):
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, int) and not isinstance(value, bool):
        return str(value)
    if isinstance(value, str):
        return json.dumps(value, separators=(",", ":"))
    raise TypeError("unsupported setting value type: %s" % type(value).__name__)


def setting_key_present(text, key):
    pattern = re.compile(r'(?m)^\s*"%s"\s*:' % re.escape(key))
    return pattern.search(text) is not None


def count_setting_keys(text, key):
    pattern = re.compile(r'(?m)^\s*"%s"\s*:' % re.escape(key))
    return len(pattern.findall(text))


def _setting_line_pattern(key):
    return re.compile(
        r'(?m)^(?P<indent>\s*)"%s"\s*:\s*'
        r'(?P<old>"(?:\\.|[^"\\])*"|true|false|null|-?\d+(?:\.\d+)?)'
        r'(?P<comma>\s*,?)(?P<comment>\s*(?://.*)?)$'
        % re.escape(key)
    )


def insert_optional_setting(text, key, value):
    """
    Insert a single known optional setting near other LLM keys.
    JSONC-preserving: keeps comments, indentation, and unrelated values.
    Idempotent when the key is already present.
    """
    if key not in MIGRATABLE_OPTIONAL_SETTINGS:
        raise ValueError("Refusing to migrate unknown setting: %s" % key)
    if setting_key_present(text, key):
        return text

    literal = jsonc_literal(value)
    for anchor in LLM_SETTING_INSERT_ANCHORS:
        match = _setting_line_pattern(anchor).search(text)
        if match is None:
            continue
        indent = match.group("indent")
        # Anchor must have a trailing comma so the new property is valid JSONC.
        fixed_anchor = (
            '%s"%s": %s,%s'
            % (
                indent,
                anchor,
                match.group("old"),
                match.group("comment"),
            )
        )
        new_line = '%s"%s": %s,' % (indent, key, literal)
        insertion = fixed_anchor + "\n" + new_line
        return text[: match.start()] + insertion + text[match.end() :]

    raise ValueError(
        "Cannot migrate missing setting %s: no LLM anchor keys found to insert near"
        % key
    )


def migrate_optional_settings_for_update(text, settings):
    """
    When an update targets a known optional key absent from older runtime files,
    insert it once before replacement. Broker enable paths that omit the key do nothing.
    """
    updated = text
    for key, value in settings.items():
        if key not in MIGRATABLE_OPTIONAL_SETTINGS:
            continue
        if setting_key_present(updated, key):
            continue
        updated = insert_optional_setting(updated, key, value)
    return updated


def update_settings_text(text, settings):
    updated = migrate_optional_settings_for_update(text, settings)
    missing = []
    for key, value in settings.items():
        pattern = _setting_line_pattern(key)
        literal = jsonc_literal(value)
        replaced = 0

        def replace(match):
            nonlocal replaced
            replaced += 1
            return (
                '%s"%s": %s%s%s'
                % (
                    match.group("indent"),
                    key,
                    literal,
                    match.group("comma"),
                    match.group("comment"),
                )
            )

        updated = pattern.sub(replace, updated, count=1)
        if replaced == 0:
            missing.append(key)

    if missing:
        raise ValueError("Missing setting(s): " + ", ".join(missing))
    return updated


def read_settings_values(text):
    values = {}
    for key in STATUS_SETTINGS_KEYS:
        pattern = re.compile(
            r'(?m)^\s*"%s"\s*:\s*'
            r'(?P<value>"(?:\\.|[^"\\])*"|true|false|null|-?\d+(?:\.\d+)?)'
            r'\s*,?\s*(?://.*)?$'
            % re.escape(key)
        )
        match = pattern.search(text)
        if match is None:
            values[key] = None
            continue
        values[key] = json.loads(match.group("value"))
    return values


def broker_settings(enable, control_player, broker_url, timeout_ms):
    if enable:
        if control_player not in (0, 1):
            raise ValueError("--control-player must be 0 or 1 when enabling")
        if timeout_ms <= 0:
            raise ValueError("--timeout-ms must be positive")
        effective_timeout_ms = int(timeout_ms)
    else:
        effective_timeout_ms = (
            int(timeout_ms) if timeout_ms > 0 else DEFAULT_BROKER_TIMEOUT_MS
        )
    # LlmSelfResourcesAuditEnabled is intentionally omitted: broker enable/disable must
    # never implicitly turn private self-resources audit on (YGOMASTER-LLM-005 Slice 1B).
    return {
        "LlmDecisionLogEnabled": bool(enable),
        "LlmBrokerEnabled": bool(enable),
        "LlmBrokerUrl": broker_url,
        "LlmBrokerTimeoutMs": effective_timeout_ms,
        "LlmBrokerControlPlayer": int(control_player if enable else -1),
        "PvpLogToFile": bool(enable),
    }


def process_basename(line):
    match = re.match(r"^\s*\d+\s+(?P<command>\S+)", line)
    if match is None:
        return ""
    command = match.group("command").replace("\\", "/")
    return command.rsplit("/", 1)[-1].lower()


def detect_running_game_processes(ps_text):
    matches = []
    for line in ps_text.splitlines():
        if "prepare_llm_runtime.py" in line:
            continue
        if process_basename(line) in IGNORED_PROCESS_BASENAMES:
            continue
        if " rg " in line or "ripgrep" in line:
            continue
        if any(pattern.search(line) for pattern in GAME_PROCESS_PATTERNS):
            matches.append(line.strip())
    return matches


def get_process_table():
    return subprocess.check_output(["ps", "-eo", "pid=,args="], text=True)


def ensure_game_not_running(ps_text=None):
    if ps_text is None:
        ps_text = get_process_table()
    matches = detect_running_game_processes(ps_text)
    if matches:
        raise RuntimeError(
            "Refusing to deploy binaries while game processes are running:\n"
            + "\n".join(matches)
        )


def make_backup_dir(runtime_dir):
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    return runtime_dir / ("backup-llm-runtime-%s" % stamp)


def sha256_file(path):
    import hashlib

    digest = hashlib.sha256()
    with path.open("rb") as file:
        for chunk in iter(lambda: file.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def file_status(path):
    status = {
        "path": str(path),
        "exists": path.is_file(),
    }
    if path.is_file():
        status["size"] = path.stat().st_size
        status["sha256"] = sha256_file(path)
    return status


def settings_file_status(path):
    status = file_status(path)
    status["values"] = {}
    if path.is_file():
        status["values"] = read_settings_values(path.read_text(encoding="utf-8"))
    return status


def parse_broker_endpoint(url):
    if not url:
        return None, None
    parsed = urlparse(url)
    if not parsed.hostname:
        return None, None
    port = parsed.port
    if port is None:
        port = 443 if parsed.scheme == "https" else 80
    return parsed.hostname, port


def broker_health_url(url):
    parsed = urlparse(url or "")
    if parsed.scheme not in ("http", "https") or not parsed.netloc:
        return None
    return urlunparse((parsed.scheme, parsed.netloc, "/health", "", "", ""))


def is_tcp_listening(host, port, timeout=0.2):
    if not host or not port:
        return False
    try:
        with socket.create_connection((host, port), timeout=timeout):
            return True
    except OSError:
        return False


def probe_broker_health(url, timeout=0.2):
    health_url = broker_health_url(url)
    result = {
        "url": health_url,
        "ok": False,
        "status_code": None,
        "service": None,
        "status": None,
        "provider_configured": None,
        "error": None,
    }
    if health_url is None:
        result["error"] = "unsupported broker URL"
        return result

    request = Request(
        health_url,
        headers={"Accept": "application/json"},
        method="GET",
    )
    try:
        with urlopen(request, timeout=timeout) as response:
            result["status_code"] = response.status
            payload = json.loads(response.read().decode("utf-8"))
    except HTTPError as exc:
        result["status_code"] = exc.code
        result["error"] = "HTTP %d" % exc.code
        return result
    except (OSError, TimeoutError, URLError, ValueError, json.JSONDecodeError) as exc:
        result["error"] = str(exc)
        return result

    if not isinstance(payload, dict):
        result["error"] = "health response was not a JSON object"
        return result

    result["service"] = payload.get("service")
    result["status"] = payload.get("status")
    result["provider_configured"] = payload.get("provider_configured")
    result["ok"] = (
        result["status_code"] == 200 and
        result["service"] == "ygomaster_llm_broker" and
        result["status"] == "ok"
    )
    if not result["ok"]:
        result["error"] = "unexpected broker health response"
    return result


def collect_status(runtime_dir, build_dir, ps_text=None, check_broker=True):
    runtime_settings_path = runtime_dir / "Data" / "ClientData" / "ClientSettings.json"
    source_settings_path = (
        runtime_dir / "YgoMaster" / "Data" / "ClientData" / "ClientSettings.json"
    )
    decision_log_path = runtime_dir / "Data" / "ClientData" / "LlmDecisionLog.jsonl"
    reasoning_log_path = runtime_dir / "Data" / "ClientData" / "LlmReasoningLog.jsonl"
    if ps_text is None:
        ps_text = get_process_table()

    runtime_settings = settings_file_status(runtime_settings_path)
    source_settings = settings_file_status(source_settings_path)
    binaries = {}
    for name in RUNTIME_BINARIES:
        runtime_binary = file_status(runtime_dir / name)
        build_binary = file_status(build_dir / name)
        binaries[name] = {
            "runtime": runtime_binary,
            "build": build_binary,
            "matches_build": (
                runtime_binary.get("sha256") is not None and
                runtime_binary.get("sha256") == build_binary.get("sha256")
            ),
        }

    broker_url = runtime_settings.get("values", {}).get("LlmBrokerUrl")
    broker_host, broker_port = parse_broker_endpoint(broker_url)
    broker_health = probe_broker_health(broker_url) if check_broker else None
    broker = {
        "url": broker_url,
        "host": broker_host,
        "port": broker_port,
        "listening": is_tcp_listening(broker_host, broker_port) if check_broker else None,
        "health": broker_health,
        "ready_for_llm_validation": (
            bool(broker_health and broker_health.get("ok") and broker_health.get("provider_configured"))
            if check_broker else None
        ),
    }

    return {
        "runtime_dir": str(runtime_dir),
        "build_dir": str(build_dir),
        "runtime_settings": runtime_settings,
        "source_settings": source_settings,
        "binaries": binaries,
        "broker": broker,
        "game_processes": detect_running_game_processes(ps_text),
        "decision_log": file_status(decision_log_path),
        "reasoning_log": file_status(reasoning_log_path),
    }


def write_text_with_backup(path, new_text, backup_dir, backup_relpath=None):
    backup_dir.mkdir(parents=True, exist_ok=True)
    backup = backup_dir / (backup_relpath if backup_relpath is not None else path.name)
    backup.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, backup)
    path.write_text(new_text, encoding="utf-8")
    return backup


def copy_file_with_backup(src, dst, backup_dir):
    backup_dir.mkdir(parents=True, exist_ok=True)
    backup = backup_dir / dst.name
    if dst.exists():
        shutil.copy2(dst, backup)
    shutil.copy2(src, dst)
    return backup if backup.exists() else None


def rollback_file_changes(changes):
    for path, backup, existed in reversed(list(changes)):
        if backup is not None and Path(backup).is_file():
            shutil.copy2(backup, path)
        elif not existed and path.exists():
            path.unlink()


def validate_deploy_sources(build_dir):
    for name in RUNTIME_BINARIES:
        src = build_dir / name
        if not src.is_file():
            raise FileNotFoundError(str(src))


def settings_paths(runtime_dir, include_source):
    paths = [runtime_dir / "Data" / "ClientData" / "ClientSettings.json"]
    if include_source:
        paths.append(runtime_dir / "YgoMaster" / "Data" / "ClientData" / "ClientSettings.json")
    return paths


def apply_settings(runtime_dir, include_source, settings, backup_dir, write):
    pending = []
    for path in settings_paths(runtime_dir, include_source):
        if not path.is_file():
            raise FileNotFoundError(str(path))
        original = path.read_text(encoding="utf-8")
        updated = update_settings_text(original, settings)
        pending.append((path, original, updated))

    changed = []
    try:
        for path, original, updated in pending:
            if original == updated:
                continue
            backup = None
            if write:
                backup = write_text_with_backup(
                    path,
                    updated,
                    backup_dir,
                    path.relative_to(runtime_dir),
                )
            changed.append((path, backup))
    except Exception:
        rollback_file_changes((path, backup, True) for path, backup in changed)
        raise
    return changed


def deploy_binaries(runtime_dir, build_dir, backup_dir, write, allow_running):
    if write and not allow_running:
        ensure_game_not_running()
    validate_deploy_sources(build_dir)

    copied = []
    try:
        for name in RUNTIME_BINARIES:
            src = build_dir / name
            dst = runtime_dir / name
            existed = dst.exists()
            backup = None
            if write:
                backup = copy_file_with_backup(src, dst, backup_dir)
            copied.append((src, dst, backup, existed))
    except Exception:
        rollback_file_changes((dst, backup, existed) for _, dst, backup, existed in copied)
        raise
    return [(src, dst, backup) for src, dst, backup, _ in copied]


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Prepare the local YgoMaster runtime for an LLM broker validation run."
    )
    parser.add_argument("--runtime-dir", type=Path, default=ROOT)
    parser.add_argument("--build-dir", type=Path, default=DEFAULT_BUILD_DIR)
    parser.add_argument("--status", action="store_true", help="print non-mutating runtime status as JSON")
    parser.add_argument("--settings", action="store_true", help="update runtime ClientSettings.json")
    parser.add_argument(
        "--include-source-settings",
        action="store_true",
        help="also update YgoMaster/Data/ClientData/ClientSettings.json",
    )
    parser.add_argument("--deploy-binaries", action="store_true")
    parser.add_argument("--disable", action="store_true", help="disable broker/log settings")
    parser.add_argument("--control-player", type=int, default=1)
    parser.add_argument("--broker-url", default="http://127.0.0.1:4991/decide")
    parser.add_argument("--timeout-ms", type=int, default=DEFAULT_BROKER_TIMEOUT_MS)
    parser.add_argument(
        "--self-resources-audit",
        choices=("on", "off"),
        default=None,
        help="explicitly enable/disable LlmSelfResourcesAuditEnabled (default-off; never implied by broker enable)",
    )
    parser.add_argument(
        "--planning-search-audit",
        choices=("on", "off"),
        default=None,
        help="explicitly enable/disable LlmPlanningSearchAuditEnabled (default-off; never implied by broker enable)",
    )
    parser.add_argument(
        "--search-max-strategic-depth",
        type=int,
        default=None,
        help="set LlmSearchMaxStrategicDepth (plan default 4; never implied by broker enable)",
    )
    parser.add_argument(
        "--search-max-nodes",
        type=int,
        default=None,
        help="set LlmSearchMaxNodes (plan default 96)",
    )
    parser.add_argument(
        "--search-beam-width",
        type=int,
        default=None,
        help="set LlmSearchBeamWidth (plan default 12)",
    )
    parser.add_argument(
        "--search-max-wall-ms",
        type=int,
        default=None,
        help="set LlmSearchMaxWallMs (plan default 500)",
    )
    parser.add_argument(
        "--search-max-serialized-bytes",
        type=int,
        default=None,
        help="set LlmSearchMaxSerializedBytes (plan default 16384)",
    )
    parser.add_argument(
        "--ensure-search-limit-defaults",
        action="store_true",
        help="insert missing LlmSearch* plan defaults without enabling planning audit",
    )
    parser.add_argument("--allow-running", action="store_true")
    parser.add_argument("--write", action="store_true", help="apply changes; otherwise dry-run only")
    args = parser.parse_args(argv)

    search_limit_args_set = any(
        v is not None
        for v in (
            args.search_max_strategic_depth,
            args.search_max_nodes,
            args.search_beam_width,
            args.search_max_wall_ms,
            args.search_max_serialized_bytes,
        )
    ) or args.ensure_search_limit_defaults

    if not args.status and not args.settings and not args.deploy_binaries:
        parser.error("choose --settings and/or --deploy-binaries")
    if args.status and (
        args.settings
        or args.deploy_binaries
        or args.include_source_settings
        or args.write
        or args.self_resources_audit is not None
        or args.planning_search_audit is not None
        or search_limit_args_set
    ):
        parser.error("--status cannot be combined with mutating options")
    if args.self_resources_audit is not None and not args.settings:
        parser.error("--self-resources-audit requires --settings")
    if args.planning_search_audit is not None and not args.settings:
        parser.error("--planning-search-audit requires --settings")
    if search_limit_args_set and not args.settings:
        parser.error("search limit options require --settings")

    runtime_dir = args.runtime_dir.resolve()
    build_dir = args.build_dir.resolve()

    try:
        if args.status:
            print(json.dumps(collect_status(runtime_dir, build_dir), indent=2, sort_keys=True))
            return 0

        backup_dir = make_backup_dir(runtime_dir)
        settings = broker_settings(
            not args.disable,
            args.control_player,
            args.broker_url,
            args.timeout_ms,
        )
        if args.self_resources_audit is not None:
            settings["LlmSelfResourcesAuditEnabled"] = args.self_resources_audit == "on"
        elif args.disable:
            # Clean disable path: turn private audit off without requiring a separate flag.
            settings["LlmSelfResourcesAuditEnabled"] = False
        if args.planning_search_audit is not None:
            settings["LlmPlanningSearchAuditEnabled"] = args.planning_search_audit == "on"
        elif args.disable:
            settings["LlmPlanningSearchAuditEnabled"] = False

        # Search limits: explicit only. Broker enable must not imply planning audit or limits.
        if args.ensure_search_limit_defaults:
            settings.update(LLM_SEARCH_LIMIT_DEFAULTS)
        if args.search_max_strategic_depth is not None:
            settings["LlmSearchMaxStrategicDepth"] = args.search_max_strategic_depth
        if args.search_max_nodes is not None:
            settings["LlmSearchMaxNodes"] = args.search_max_nodes
        if args.search_beam_width is not None:
            settings["LlmSearchBeamWidth"] = args.search_beam_width
        if args.search_max_wall_ms is not None:
            settings["LlmSearchMaxWallMs"] = args.search_max_wall_ms
        if args.search_max_serialized_bytes is not None:
            settings["LlmSearchMaxSerializedBytes"] = args.search_max_serialized_bytes

        if args.write and args.deploy_binaries:
            if not args.allow_running:
                ensure_game_not_running()
            validate_deploy_sources(build_dir)

        messages = []
        settings_changed = []

        if args.settings:
            changed = apply_settings(
                runtime_dir,
                args.include_source_settings,
                settings,
                backup_dir,
                args.write,
            )
            settings_changed = changed
            for path, backup in changed:
                if args.write:
                    messages.append("updated %s (backup: %s)" % (path, backup))
                else:
                    messages.append("would update %s" % path)

        if args.deploy_binaries:
            try:
                copied = deploy_binaries(
                    runtime_dir,
                    build_dir,
                    backup_dir,
                    args.write,
                    args.allow_running,
                )
            except Exception:
                if args.write:
                    rollback_file_changes(
                        (path, backup, True)
                        for path, backup in settings_changed
                        if backup is not None
                    )
                raise
            for src, dst, backup in copied:
                if args.write:
                    backup_text = "backup: %s" % backup if backup else "no previous file"
                    messages.append("copied %s -> %s (%s)" % (src, dst, backup_text))
                else:
                    messages.append("would copy %s -> %s" % (src, dst))

        for message in messages:
            print(message)
        if not args.write:
            print("dry-run only; rerun with --write to apply changes")
        return 0
    except Exception as exc:
        print("error: %s" % exc, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

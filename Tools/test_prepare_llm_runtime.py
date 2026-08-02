import unittest
import contextlib
import importlib.util
import io
import json
import tempfile
from pathlib import Path


MODULE_PATH = Path(__file__).with_name("prepare_llm_runtime.py")
spec = importlib.util.spec_from_file_location("prepare_llm_runtime", MODULE_PATH)
prep = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prep)


class PrepareLlmRuntimeTests(unittest.TestCase):
    def test_read_settings_values_handles_http_urls_and_comments(self):
        text = (
            "{\n"
            '    "LlmDecisionLogEnabled": true,// keep comment\n'
            '    "LlmBrokerEnabled": true,\n'
            '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",// URL keeps //\n'
            '    "LlmBrokerTimeoutMs": 2000,\n'
            '    "LlmBrokerControlPlayer": 1,\n'
            '    "PvpLogToFile": true,\n'
            "}\n"
        )

        values = prep.read_settings_values(text)

        self.assertEqual(values["LlmDecisionLogEnabled"], True)
        self.assertEqual(values["LlmBrokerEnabled"], True)
        self.assertEqual(values["LlmBrokerUrl"], "http://127.0.0.1:4991/decide")
        self.assertEqual(values["LlmBrokerTimeoutMs"], 2000)
        self.assertEqual(values["LlmBrokerControlPlayer"], 1)
        self.assertEqual(values["PvpLogToFile"], True)

    def test_collect_status_reports_settings_binaries_processes_and_logs(self):
        runtime_settings = (
            "{\n"
            '    "LlmDecisionLogEnabled": true,\n'
            '    "LlmBrokerEnabled": true,\n'
            '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",\n'
            '    "LlmBrokerTimeoutMs": 2000,\n'
            '    "LlmBrokerControlPlayer": 1,\n'
            '    "PvpLogToFile": true,\n'
            "}\n"
        )
        source_settings = runtime_settings.replace("true", "false").replace(
            '"LlmBrokerControlPlayer": 1',
            '"LlmBrokerControlPlayer": -1',
        )

        with tempfile.TemporaryDirectory() as tmp:
            runtime_dir = Path(tmp) / "runtime"
            build_dir = Path(tmp) / "build"
            runtime_settings_path = runtime_dir / "Data" / "ClientData" / "ClientSettings.json"
            source_settings_path = runtime_dir / "YgoMaster" / "Data" / "ClientData" / "ClientSettings.json"
            decision_log_path = runtime_dir / "Data" / "ClientData" / "LlmDecisionLog.jsonl"
            runtime_settings_path.parent.mkdir(parents=True)
            source_settings_path.parent.mkdir(parents=True)
            build_dir.mkdir()
            runtime_settings_path.write_text(runtime_settings, encoding="utf-8")
            source_settings_path.write_text(source_settings, encoding="utf-8")
            decision_log_path.write_text("{}\n", encoding="utf-8")
            (runtime_dir / "YgoMaster.exe").write_text("same", encoding="utf-8")
            (build_dir / "YgoMaster.exe").write_text("same", encoding="utf-8")
            (runtime_dir / "YgoMasterClient.exe").write_text("old", encoding="utf-8")
            (build_dir / "YgoMasterClient.exe").write_text("new", encoding="utf-8")

            status = prep.collect_status(
                runtime_dir,
                build_dir,
                ps_text="123 Z:\\path\\YgoMaster.exe\n",
                check_broker=False,
            )

            self.assertEqual(status["runtime_settings"]["values"]["LlmBrokerEnabled"], True)
            self.assertEqual(status["source_settings"]["values"]["LlmBrokerEnabled"], False)
            self.assertEqual(status["binaries"]["YgoMaster.exe"]["matches_build"], True)
            self.assertEqual(status["binaries"]["YgoMasterClient.exe"]["matches_build"], False)
            self.assertEqual(status["game_processes"], ["123 Z:\\path\\YgoMaster.exe"])
            self.assertEqual(status["decision_log"]["exists"], True)
            self.assertEqual(status["decision_log"]["size"], 3)
            self.assertEqual(status["reasoning_log"]["exists"], False)

    def test_collect_status_reports_broker_tcp_and_identity_health(self):
        runtime_settings = (
            "{\n"
            '    "LlmDecisionLogEnabled": true,\n'
            '    "LlmBrokerEnabled": true,\n'
            '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",\n'
            '    "LlmBrokerTimeoutMs": 2000,\n'
            '    "LlmBrokerControlPlayer": 1,\n'
            '    "PvpLogToFile": true,\n'
            "}\n"
        )
        original_tcp = prep.is_tcp_listening
        original_probe = prep.probe_broker_health
        try:
            prep.is_tcp_listening = lambda host, port: True
            prep.probe_broker_health = lambda url: {
                "url": "http://127.0.0.1:4991/health",
                "ok": True,
                "status_code": 200,
                "service": "ygomaster_llm_broker",
                "status": "ok",
                "error": None,
            }
            with tempfile.TemporaryDirectory() as tmp:
                runtime_dir = Path(tmp) / "runtime"
                build_dir = Path(tmp) / "build"
                settings_path = runtime_dir / "Data" / "ClientData" / "ClientSettings.json"
                source_settings_path = runtime_dir / "YgoMaster" / "Data" / "ClientData" / "ClientSettings.json"
                settings_path.parent.mkdir(parents=True)
                source_settings_path.parent.mkdir(parents=True)
                build_dir.mkdir()
                settings_path.write_text(runtime_settings, encoding="utf-8")
                source_settings_path.write_text(runtime_settings, encoding="utf-8")

                status = prep.collect_status(
                    runtime_dir,
                    build_dir,
                    ps_text="",
                    check_broker=True,
                )

                self.assertEqual(status["broker"]["host"], "127.0.0.1")
                self.assertEqual(status["broker"]["port"], 4991)
                self.assertEqual(status["broker"]["listening"], True)
                self.assertEqual(status["broker"]["health"]["ok"], True)
                self.assertEqual(
                    status["broker"]["health"]["service"],
                    "ygomaster_llm_broker",
                )
        finally:
            prep.is_tcp_listening = original_tcp
            prep.probe_broker_health = original_probe

    def test_collect_status_marks_broker_not_ready_when_provider_is_not_configured(self):
        runtime_settings = (
            "{\n"
            '    "LlmDecisionLogEnabled": true,\n'
            '    "LlmBrokerEnabled": true,\n'
            '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",\n'
            '    "LlmBrokerTimeoutMs": 2000,\n'
            '    "LlmBrokerControlPlayer": 1,\n'
            '    "PvpLogToFile": true,\n'
            "}\n"
        )
        original_tcp = prep.is_tcp_listening
        original_probe = prep.probe_broker_health
        try:
            prep.is_tcp_listening = lambda host, port: True
            prep.probe_broker_health = lambda url: {
                "url": "http://127.0.0.1:4991/health",
                "ok": True,
                "status_code": 200,
                "service": "ygomaster_llm_broker",
                "status": "ok",
                "provider_configured": False,
                "error": None,
            }
            with tempfile.TemporaryDirectory() as tmp:
                runtime_dir = Path(tmp) / "runtime"
                build_dir = Path(tmp) / "build"
                settings_path = runtime_dir / "Data" / "ClientData" / "ClientSettings.json"
                source_settings_path = runtime_dir / "YgoMaster" / "Data" / "ClientData" / "ClientSettings.json"
                settings_path.parent.mkdir(parents=True)
                source_settings_path.parent.mkdir(parents=True)
                build_dir.mkdir()
                settings_path.write_text(runtime_settings, encoding="utf-8")
                source_settings_path.write_text(runtime_settings, encoding="utf-8")

                status = prep.collect_status(
                    runtime_dir,
                    build_dir,
                    ps_text="",
                    check_broker=True,
                )

                self.assertEqual(status["broker"]["health"]["ok"], True)
                self.assertEqual(status["broker"]["ready_for_llm_validation"], False)
        finally:
            prep.is_tcp_listening = original_tcp
            prep.probe_broker_health = original_probe

    def test_probe_broker_health_rejects_non_object_json_without_raising(self):
        original_urlopen = prep.urlopen

        class FakeResponse:
            status = 200

            def __enter__(self):
                return self

            def __exit__(self, exc_type, exc, traceback):
                return False

            def read(self):
                return b'["not", "a", "health", "object"]'

        try:
            prep.urlopen = lambda request, timeout=None: FakeResponse()

            health = prep.probe_broker_health("http://127.0.0.1:4991/decide")

            self.assertEqual(health["ok"], False)
            self.assertEqual(health["status_code"], 200)
            self.assertEqual(health["error"], "health response was not a JSON object")
        finally:
            prep.urlopen = original_urlopen

    def test_status_mode_ignores_write_only_broker_validation_args(self):
        original_ps = prep.get_process_table
        try:
            prep.get_process_table = lambda: ""
            with tempfile.TemporaryDirectory() as tmp:
                runtime_dir = Path(tmp) / "runtime"
                build_dir = Path(tmp) / "build"
                runtime_dir.mkdir()
                build_dir.mkdir()
                stdout = io.StringIO()

                with contextlib.redirect_stdout(stdout):
                    result = prep.main(
                        [
                            "--status",
                            "--runtime-dir",
                            str(runtime_dir),
                            "--build-dir",
                            str(build_dir),
                            "--timeout-ms",
                            "0",
                            "--control-player",
                            "99",
                        ]
                    )

                status = json.loads(stdout.getvalue())
                self.assertEqual(result, 0)
                self.assertEqual(status["runtime_dir"], str(runtime_dir.resolve()))
        finally:
            prep.get_process_table = original_ps

    def test_update_settings_preserves_comments_and_trailing_commas(self):
        original = (
            "{\n"
            '    "LlmDecisionLogEnabled": false,\n'
            '    "LlmBrokerEnabled": false,// keep the comment\n'
            '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",\n'
            '    "LlmBrokerTimeoutMs": 2000,\n'
            '    "LlmBrokerControlPlayer": -1,\n'
            "}\n"
        )

        updated = prep.update_settings_text(
            original,
            {
                "LlmDecisionLogEnabled": True,
                "LlmBrokerEnabled": True,
                "LlmBrokerTimeoutMs": 3500,
                "LlmBrokerControlPlayer": 1,
            },
        )

        self.assertIn('    "LlmDecisionLogEnabled": true,', updated)
        self.assertIn('    "LlmBrokerEnabled": true,// keep the comment', updated)
        self.assertIn('    "LlmBrokerTimeoutMs": 3500,', updated)
        self.assertIn('    "LlmBrokerControlPlayer": 1,', updated)
        self.assertIn('    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",', updated)

    def test_update_settings_rejects_missing_keys(self):
        with self.assertRaisesRegex(ValueError, "Missing setting"):
            prep.update_settings_text("{\n}\n", {"LlmBrokerEnabled": True})

    def test_write_text_with_backup_keeps_original_copy(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "ClientSettings.json"
            backup_dir = Path(tmp) / "backup"
            path.write_text('{"LlmBrokerEnabled": false}\n', encoding="utf-8")

            backup = prep.write_text_with_backup(
                path,
                '{"LlmBrokerEnabled": true}\n',
                backup_dir,
            )

            self.assertEqual(path.read_text(encoding="utf-8"), '{"LlmBrokerEnabled": true}\n')
            self.assertEqual(backup.read_text(encoding="utf-8"), '{"LlmBrokerEnabled": false}\n')

    def test_apply_settings_backups_preserve_both_client_settings_files(self):
        original = (
            "{\n"
            '    "LlmDecisionLogEnabled": false,\n'
            '    "LlmBrokerEnabled": false,\n'
            '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",\n'
            '    "LlmBrokerTimeoutMs": 2000,\n'
            '    "LlmBrokerControlPlayer": -1,\n'
            '    "PvpLogToFile": false,\n'
            "}\n"
        )

        with tempfile.TemporaryDirectory() as tmp:
            runtime_dir = Path(tmp)
            runtime_path = runtime_dir / "Data" / "ClientData" / "ClientSettings.json"
            source_path = runtime_dir / "YgoMaster" / "Data" / "ClientData" / "ClientSettings.json"
            runtime_path.parent.mkdir(parents=True)
            source_path.parent.mkdir(parents=True)
            runtime_path.write_text(original, encoding="utf-8")
            source_path.write_text(original, encoding="utf-8")

            backup_dir = runtime_dir / "backup-llm-runtime-test"
            settings = prep.broker_settings(True, 1, "http://127.0.0.1:4991/decide", 2000)
            prep.apply_settings(runtime_dir, True, settings, backup_dir, True)

            runtime_backup = backup_dir / "Data" / "ClientData" / "ClientSettings.json"
            source_backup = backup_dir / "YgoMaster" / "Data" / "ClientData" / "ClientSettings.json"

            self.assertTrue(runtime_backup.is_file())
            self.assertTrue(source_backup.is_file())
            self.assertEqual(runtime_backup.read_text(encoding="utf-8"), original)
            self.assertEqual(source_backup.read_text(encoding="utf-8"), original)

    def test_apply_settings_validates_all_targets_before_writing_any_file(self):
        original = (
            "{\n"
            '    "LlmDecisionLogEnabled": false,\n'
            '    "LlmBrokerEnabled": false,\n'
            '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",\n'
            '    "LlmBrokerTimeoutMs": 2000,\n'
            '    "LlmBrokerControlPlayer": -1,\n'
            '    "PvpLogToFile": false,\n'
            "}\n"
        )
        source_missing_key = original.replace('    "PvpLogToFile": false,\n', "")

        with tempfile.TemporaryDirectory() as tmp:
            runtime_dir = Path(tmp)
            runtime_path = runtime_dir / "Data" / "ClientData" / "ClientSettings.json"
            source_path = runtime_dir / "YgoMaster" / "Data" / "ClientData" / "ClientSettings.json"
            runtime_path.parent.mkdir(parents=True)
            source_path.parent.mkdir(parents=True)
            runtime_path.write_text(original, encoding="utf-8")
            source_path.write_text(source_missing_key, encoding="utf-8")

            settings = prep.broker_settings(True, 1, "http://127.0.0.1:4991/decide", 2000)

            with self.assertRaisesRegex(ValueError, "Missing setting"):
                prep.apply_settings(
                    runtime_dir,
                    True,
                    settings,
                    runtime_dir / "backup-llm-runtime-test",
                    True,
                )

            self.assertEqual(runtime_path.read_text(encoding="utf-8"), original)
            self.assertFalse((runtime_dir / "backup-llm-runtime-test").exists())

    def test_apply_settings_rolls_back_prior_writes_when_later_write_fails(self):
        original = (
            "{\n"
            '    "LlmDecisionLogEnabled": false,\n'
            '    "LlmBrokerEnabled": false,\n'
            '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",\n'
            '    "LlmBrokerTimeoutMs": 2000,\n'
            '    "LlmBrokerControlPlayer": -1,\n'
            '    "PvpLogToFile": false,\n'
            "}\n"
        )
        original_writer = prep.write_text_with_backup
        calls = []
        try:
            with tempfile.TemporaryDirectory() as tmp:
                runtime_dir = Path(tmp)
                runtime_path = runtime_dir / "Data" / "ClientData" / "ClientSettings.json"
                source_path = runtime_dir / "YgoMaster" / "Data" / "ClientData" / "ClientSettings.json"
                runtime_path.parent.mkdir(parents=True)
                source_path.parent.mkdir(parents=True)
                runtime_path.write_text(original, encoding="utf-8")
                source_path.write_text(original, encoding="utf-8")

                def fail_second_write(path, new_text, backup_dir, backup_relpath=None):
                    calls.append(path)
                    if len(calls) == 2:
                        raise OSError("simulated write failure")
                    return original_writer(path, new_text, backup_dir, backup_relpath)

                prep.write_text_with_backup = fail_second_write
                settings = prep.broker_settings(True, 1, "http://127.0.0.1:4991/decide", 2000)

                with self.assertRaisesRegex(OSError, "simulated write failure"):
                    prep.apply_settings(
                        runtime_dir,
                        True,
                        settings,
                        runtime_dir / "backup-llm-runtime-test",
                        True,
                    )

                self.assertEqual(runtime_path.read_text(encoding="utf-8"), original)
                self.assertEqual(source_path.read_text(encoding="utf-8"), original)
        finally:
            prep.write_text_with_backup = original_writer

    def test_disable_settings_ignores_enable_only_validation_args(self):
        settings = prep.broker_settings(
            False,
            99,
            "http://127.0.0.1:4991/decide",
            0,
        )

        self.assertEqual(settings["LlmDecisionLogEnabled"], False)
        self.assertEqual(settings["LlmBrokerEnabled"], False)
        self.assertEqual(settings["LlmBrokerControlPlayer"], -1)
        self.assertEqual(settings["LlmBrokerTimeoutMs"], 55000)
        # Broker enable/disable must not implicitly flip private self-resources audit.
        self.assertNotIn("LlmSelfResourcesAuditEnabled", settings)

    def test_broker_settings_enable_does_not_implicitly_enable_self_resources_audit(self):
        settings = prep.broker_settings(True, 1, "http://127.0.0.1:4991/decide", 2000)
        self.assertEqual(settings["LlmBrokerEnabled"], True)
        self.assertNotIn("LlmSelfResourcesAuditEnabled", settings)

    def test_self_resources_audit_explicit_toggle_updates_setting(self):
        original = (
            "{\n"
            '    "LlmDecisionLogEnabled": false,\n'
            '    "LlmBrokerEnabled": false,\n'
            '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",\n'
            '    "LlmBrokerTimeoutMs": 2000,\n'
            '    "LlmBrokerControlPlayer": -1,\n'
            '    "LlmSelfResourcesAuditEnabled": false,\n'
            '    "PvpLogToFile": false,\n'
            "}\n"
        )
        updated = prep.update_settings_text(
            original,
            {"LlmSelfResourcesAuditEnabled": True},
        )
        self.assertIn('"LlmSelfResourcesAuditEnabled": true', updated)
        values = prep.read_settings_values(updated)
        self.assertEqual(values["LlmSelfResourcesAuditEnabled"], True)

        off = prep.update_settings_text(
            updated,
            {"LlmSelfResourcesAuditEnabled": False},
        )
        self.assertIn('"LlmSelfResourcesAuditEnabled": false', off)
        self.assertEqual(prep.read_settings_values(off)["LlmSelfResourcesAuditEnabled"], False)

    def test_read_settings_reports_self_resources_audit_default_false(self):
        text = (
            "{\n"
            '    "LlmDecisionLogEnabled": false,\n'
            '    "LlmBrokerEnabled": false,\n'
            '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",\n'
            '    "LlmBrokerTimeoutMs": 2000,\n'
            '    "LlmBrokerControlPlayer": -1,\n'
            '    "LlmSelfResourcesAuditEnabled": false,\n'
            '    "PvpLogToFile": false,\n'
            "}\n"
        )
        values = prep.read_settings_values(text)
        self.assertEqual(values["LlmSelfResourcesAuditEnabled"], False)

    def _old_runtime_settings_without_audit_key(self):
        # Realistic pre-Slice-1B deployed root ClientSettings.json (no audit key).
        return (
            "{\n"
            '    "MultiplayerPort": 0,// keep comment\n'
            '    "LlmDecisionLogEnabled": true,// keep comment\n'
            '    "LlmBrokerEnabled": true,\n'
            '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",// URL keeps //\n'
            '    "LlmBrokerTimeoutMs": 55000,\n'
            '    "LlmBrokerControlPlayer": 1,\n'
            '    "PvpLogToFile": true,\n'
            '    "EmoteDurationInSeconds": 4.0,\n'
            "}\n"
        )

    def test_migrate_self_resources_audit_on_old_runtime_inserts_true(self):
        original = self._old_runtime_settings_without_audit_key()
        self.assertIsNone(
            prep.read_settings_values(original)["LlmSelfResourcesAuditEnabled"]
        )

        updated = prep.update_settings_text(
            original,
            {"LlmSelfResourcesAuditEnabled": True},
        )
        values = prep.read_settings_values(updated)
        self.assertEqual(values["LlmSelfResourcesAuditEnabled"], True)
        self.assertEqual(prep.count_setting_keys(updated, "LlmSelfResourcesAuditEnabled"), 1)
        self.assertIn('    "LlmBrokerControlPlayer": 1,', updated)
        self.assertIn('    "LlmSelfResourcesAuditEnabled": true,', updated)
        # Preserve comments and unrelated keys.
        self.assertIn("// keep comment", updated)
        self.assertIn("// URL keeps //", updated)
        self.assertIn('"MultiplayerPort": 0', updated)
        self.assertIn('"EmoteDurationInSeconds": 4.0', updated)

    def test_migrate_self_resources_audit_off_writes_false_and_is_idempotent(self):
        original = self._old_runtime_settings_without_audit_key()
        once = prep.update_settings_text(
            original,
            {"LlmSelfResourcesAuditEnabled": False},
        )
        self.assertEqual(
            prep.read_settings_values(once)["LlmSelfResourcesAuditEnabled"], False
        )
        twice = prep.update_settings_text(
            once,
            {"LlmSelfResourcesAuditEnabled": False},
        )
        self.assertEqual(
            prep.read_settings_values(twice)["LlmSelfResourcesAuditEnabled"], False
        )
        self.assertEqual(prep.count_setting_keys(twice, "LlmSelfResourcesAuditEnabled"), 1)

        inserted_on = prep.update_settings_text(
            original,
            {"LlmSelfResourcesAuditEnabled": True},
        )
        flipped_off = prep.update_settings_text(
            inserted_on,
            {"LlmSelfResourcesAuditEnabled": False},
        )
        self.assertEqual(
            prep.read_settings_values(flipped_off)["LlmSelfResourcesAuditEnabled"], False
        )
        self.assertEqual(
            prep.count_setting_keys(flipped_off, "LlmSelfResourcesAuditEnabled"), 1
        )

    def test_broker_enable_without_audit_flag_does_not_insert_audit_key(self):
        original = self._old_runtime_settings_without_audit_key()
        settings = prep.broker_settings(True, 1, "http://127.0.0.1:4991/decide", 55000)
        self.assertNotIn("LlmSelfResourcesAuditEnabled", settings)
        updated = prep.update_settings_text(original, settings)
        self.assertIsNone(
            prep.read_settings_values(updated)["LlmSelfResourcesAuditEnabled"]
        )
        self.assertEqual(
            prep.count_setting_keys(updated, "LlmSelfResourcesAuditEnabled"), 0
        )
        self.assertIn('"LlmBrokerEnabled": true', updated)

    def test_apply_settings_audit_toggle_leaves_source_untouched_without_include_flag(self):
        old_runtime = self._old_runtime_settings_without_audit_key()
        source_with_key = (
            "{\n"
            '    "LlmDecisionLogEnabled": false,\n'
            '    "LlmBrokerEnabled": false,\n'
            '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",\n'
            '    "LlmBrokerTimeoutMs": 2000,\n'
            '    "LlmBrokerControlPlayer": -1,\n'
            '    "LlmSelfResourcesAuditEnabled": false,\n'
            '    "PvpLogToFile": false,\n'
            "}\n"
        )
        with tempfile.TemporaryDirectory() as tmp:
            runtime_dir = Path(tmp)
            runtime_path = runtime_dir / "Data" / "ClientData" / "ClientSettings.json"
            source_path = (
                runtime_dir / "YgoMaster" / "Data" / "ClientData" / "ClientSettings.json"
            )
            runtime_path.parent.mkdir(parents=True)
            source_path.parent.mkdir(parents=True)
            runtime_path.write_text(old_runtime, encoding="utf-8")
            source_path.write_text(source_with_key, encoding="utf-8")

            prep.apply_settings(
                runtime_dir,
                include_source=False,
                settings={"LlmSelfResourcesAuditEnabled": True},
                backup_dir=runtime_dir / "backup",
                write=True,
            )

            runtime_text = runtime_path.read_text(encoding="utf-8")
            source_text = source_path.read_text(encoding="utf-8")
            self.assertEqual(
                prep.read_settings_values(runtime_text)["LlmSelfResourcesAuditEnabled"],
                True,
            )
            self.assertEqual(source_text, source_with_key)
            self.assertEqual(
                prep.read_settings_values(source_text)["LlmSelfResourcesAuditEnabled"],
                False,
            )

    def test_status_rejects_self_resources_audit_as_mutating_option(self):
        stderr = io.StringIO()
        with self.assertRaises(SystemExit) as raised:
            with contextlib.redirect_stderr(stderr):
                prep.main(
                    [
                        "--status",
                        "--self-resources-audit",
                        "on",
                    ]
                )
        self.assertEqual(raised.exception.code, 2)
        self.assertIn(
            "--status cannot be combined with mutating options",
            stderr.getvalue(),
        )

    def test_planning_search_audit_migration_and_toggle(self):
        original = (
            "{\n"
            '    "LlmDecisionLogEnabled": true,\n'
            '    "LlmBrokerEnabled": true,\n'
            '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",\n'
            '    "LlmBrokerTimeoutMs": 55000,\n'
            '    "LlmBrokerControlPlayer": 1,\n'
            '    "LlmSelfResourcesAuditEnabled": false,\n'
            '    "PvpLogToFile": true,\n'
            "}\n"
        )
        self.assertIsNone(
            prep.read_settings_values(original).get("LlmPlanningSearchAuditEnabled")
        )
        on = prep.update_settings_text(
            original, {"LlmPlanningSearchAuditEnabled": True}
        )
        self.assertEqual(
            prep.read_settings_values(on)["LlmPlanningSearchAuditEnabled"], True
        )
        self.assertEqual(
            prep.count_setting_keys(on, "LlmPlanningSearchAuditEnabled"), 1
        )
        off = prep.update_settings_text(on, {"LlmPlanningSearchAuditEnabled": False})
        self.assertEqual(
            prep.read_settings_values(off)["LlmPlanningSearchAuditEnabled"], False
        )
        again = prep.update_settings_text(
            off, {"LlmPlanningSearchAuditEnabled": False}
        )
        self.assertEqual(
            prep.count_setting_keys(again, "LlmPlanningSearchAuditEnabled"), 1
        )
        # Broker enable must not insert planning audit.
        broker = prep.broker_settings(True, 1, "http://127.0.0.1:4991/decide", 55000)
        self.assertNotIn("LlmPlanningSearchAuditEnabled", broker)

    def test_status_rejects_planning_search_audit_as_mutating_option(self):
        stderr = io.StringIO()
        with self.assertRaises(SystemExit) as raised:
            with contextlib.redirect_stderr(stderr):
                prep.main(["--status", "--planning-search-audit", "on"])
        self.assertEqual(raised.exception.code, 2)
        self.assertIn(
            "--status cannot be combined with mutating options",
            stderr.getvalue(),
        )

    def test_campaign_cpu_native_trace_toggle_migrates_and_flips_runtime_setting(self):
        original = (
            "{\n"
            '    "LlmDecisionLogEnabled": false,\n'
            '    "LlmBrokerEnabled": false,\n'
            '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",\n'
            '    "LlmBrokerTimeoutMs": 2000,\n'
            '    "LlmBrokerControlPlayer": -1,\n'
            '    "PvpLogToFile": false,\n'
            "}\n"
        )

        enabled = prep.update_settings_text(
            original, {"CampaignCpuNativeTraceEnabled": True}
        )
        self.assertEqual(
            prep.read_settings_values(enabled)["CampaignCpuNativeTraceEnabled"], True
        )
        self.assertEqual(
            prep.count_setting_keys(enabled, "CampaignCpuNativeTraceEnabled"), 1
        )
        disabled = prep.update_settings_text(
            enabled, {"CampaignCpuNativeTraceEnabled": False}
        )
        self.assertEqual(
            prep.read_settings_values(disabled)["CampaignCpuNativeTraceEnabled"], False
        )
        self.assertEqual(
            prep.count_setting_keys(disabled, "CampaignCpuNativeTraceEnabled"), 1
        )

    def test_search_limit_defaults_migration_and_broker_does_not_imply(self):
        original = (
            "{\n"
            '    "LlmDecisionLogEnabled": true,\n'
            '    "LlmBrokerEnabled": true,\n'
            '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",\n'
            '    "LlmBrokerTimeoutMs": 55000,\n'
            '    "LlmBrokerControlPlayer": 1,\n'
            '    "LlmSelfResourcesAuditEnabled": false,\n'
            '    "LlmPlanningSearchAuditEnabled": false,\n'
            '    "PvpLogToFile": true,\n'
            "}\n"
        )
        self.assertIsNone(
            prep.read_settings_values(original).get("LlmSearchMaxNodes")
        )
        migrated = prep.update_settings_text(
            original, dict(prep.LLM_SEARCH_LIMIT_DEFAULTS)
        )
        values = prep.read_settings_values(migrated)
        self.assertEqual(values["LlmSearchMaxStrategicDepth"], 4)
        self.assertEqual(values["LlmSearchMaxNodes"], 96)
        self.assertEqual(values["LlmSearchBeamWidth"], 12)
        self.assertEqual(values["LlmSearchMaxWallMs"], 500)
        self.assertEqual(values["LlmSearchMaxSerializedBytes"], 16384)
        for key in prep.LLM_SEARCH_LIMIT_DEFAULTS:
            self.assertEqual(prep.count_setting_keys(migrated, key), 1)

        # Broker enable must not insert search limits or planning audit.
        broker = prep.broker_settings(True, 1, "http://127.0.0.1:4991/decide", 55000)
        self.assertNotIn("LlmPlanningSearchAuditEnabled", broker)
        for key in prep.LLM_SEARCH_LIMIT_DEFAULTS:
            self.assertNotIn(key, broker)

        overridden = prep.update_settings_text(
            migrated, {"LlmSearchMaxNodes": 48, "LlmSearchBeamWidth": 6}
        )
        ov = prep.read_settings_values(overridden)
        self.assertEqual(ov["LlmSearchMaxNodes"], 48)
        self.assertEqual(ov["LlmSearchBeamWidth"], 6)
        self.assertEqual(prep.count_setting_keys(overridden, "LlmSearchMaxNodes"), 1)

    def test_status_rejects_search_limit_mutating_option(self):
        stderr = io.StringIO()
        with self.assertRaises(SystemExit) as raised:
            with contextlib.redirect_stderr(stderr):
                prep.main(["--status", "--ensure-search-limit-defaults"])
        self.assertEqual(raised.exception.code, 2)
        self.assertIn(
            "--status cannot be combined with mutating options",
            stderr.getvalue(),
        )

    def test_deploy_dry_run_does_not_require_game_to_be_closed(self):
        original_guard = prep.ensure_game_not_running
        try:
            prep.ensure_game_not_running = lambda: (_ for _ in ()).throw(
                RuntimeError("game running")
            )
            with tempfile.TemporaryDirectory() as tmp:
                runtime_dir = Path(tmp) / "runtime"
                build_dir = Path(tmp) / "build"
                runtime_dir.mkdir()
                build_dir.mkdir()
                for name in prep.RUNTIME_BINARIES:
                    (build_dir / name).write_text("new", encoding="utf-8")

                copied = prep.deploy_binaries(
                    runtime_dir,
                    build_dir,
                    runtime_dir / "backup",
                    False,
                    False,
                )

                self.assertEqual(len(copied), 2)
                self.assertFalse((runtime_dir / "YgoMaster.exe").exists())
        finally:
            prep.ensure_game_not_running = original_guard

    def test_combined_write_checks_deploy_guard_before_settings_write(self):
        original_ps = prep.get_process_table
        try:
            prep.get_process_table = lambda: "456 Z:\\path\\MonoRun.exe YgoMaster.exe\n"
            with tempfile.TemporaryDirectory() as tmp:
                runtime_dir = Path(tmp) / "runtime"
                build_dir = Path(tmp) / "build"
                settings_path = runtime_dir / "Data" / "ClientData" / "ClientSettings.json"
                settings_path.parent.mkdir(parents=True)
                settings_path.write_text(
                    "{\n"
                    '    "LlmDecisionLogEnabled": false,\n'
                    '    "LlmBrokerEnabled": false,\n'
                    '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",\n'
                    '    "LlmBrokerTimeoutMs": 2000,\n'
                    '    "LlmBrokerControlPlayer": -1,\n'
                    '    "PvpLogToFile": false,\n'
                    "}\n",
                    encoding="utf-8",
                )
                build_dir.mkdir()
                for name in prep.RUNTIME_BINARIES:
                    (build_dir / name).write_text("new", encoding="utf-8")

                with contextlib.redirect_stderr(io.StringIO()):
                    result = prep.main(
                        [
                            "--runtime-dir",
                            str(runtime_dir),
                            "--build-dir",
                            str(build_dir),
                            "--settings",
                            "--deploy-binaries",
                            "--write",
                        ]
                    )

                self.assertEqual(result, 1)
                self.assertIn(
                    '"LlmBrokerEnabled": false',
                    settings_path.read_text(encoding="utf-8"),
                )
        finally:
            prep.get_process_table = original_ps

    def test_combined_write_rolls_back_settings_and_binary_when_deploy_fails(self):
        original_ps = prep.get_process_table
        original_copy = prep.copy_file_with_backup
        calls = []
        settings_text = (
            "{\n"
            '    "LlmDecisionLogEnabled": false,\n'
            '    "LlmBrokerEnabled": false,\n'
            '    "LlmBrokerUrl": "http://127.0.0.1:4991/decide",\n'
            '    "LlmBrokerTimeoutMs": 2000,\n'
            '    "LlmBrokerControlPlayer": -1,\n'
            '    "PvpLogToFile": false,\n'
            "}\n"
        )
        try:
            prep.get_process_table = lambda: ""
            with tempfile.TemporaryDirectory() as tmp:
                runtime_dir = Path(tmp) / "runtime"
                build_dir = Path(tmp) / "build"
                settings_path = runtime_dir / "Data" / "ClientData" / "ClientSettings.json"
                settings_path.parent.mkdir(parents=True)
                settings_path.write_text(settings_text, encoding="utf-8")
                runtime_dir.mkdir(exist_ok=True)
                build_dir.mkdir()
                (runtime_dir / "YgoMaster.exe").write_text("old-server", encoding="utf-8")
                (runtime_dir / "YgoMasterClient.exe").write_text("old-client", encoding="utf-8")
                (build_dir / "YgoMaster.exe").write_text("new-server", encoding="utf-8")
                (build_dir / "YgoMasterClient.exe").write_text("new-client", encoding="utf-8")

                def fail_second_copy(src, dst, backup_dir):
                    calls.append(dst.name)
                    if len(calls) == 2:
                        raise OSError("simulated copy failure")
                    return original_copy(src, dst, backup_dir)

                prep.copy_file_with_backup = fail_second_copy

                with contextlib.redirect_stderr(io.StringIO()):
                    result = prep.main(
                        [
                            "--runtime-dir",
                            str(runtime_dir),
                            "--build-dir",
                            str(build_dir),
                            "--settings",
                            "--deploy-binaries",
                            "--write",
                        ]
                    )

                self.assertEqual(result, 1)
                self.assertEqual(settings_path.read_text(encoding="utf-8"), settings_text)
                self.assertEqual((runtime_dir / "YgoMaster.exe").read_text(encoding="utf-8"), "old-server")
                self.assertEqual((runtime_dir / "YgoMasterClient.exe").read_text(encoding="utf-8"), "old-client")
        finally:
            prep.get_process_table = original_ps
            prep.copy_file_with_backup = original_copy

    def test_running_process_detection_matches_game_processes_only(self):
        ps_text = (
            "123 /usr/bin/bash -lc rg YgoMaster\n"
            "321 /usr/bin/rg /repo/YgoMasterClient.exe\n"
            "322 /usr/bin/vim /game/YgoMaster.exe\n"
            "323 /usr/bin/less Z:\\path\\YgoMaster.exe\n"
            "456 Z:\\path\\MonoRun.exe YgoMaster.exe\n"
            "654 Z:\\path\\MonoRun.exe YgoMasterClient.exe\n"
            "655 Z:\\path\\YgoMaster.exe\n"
            "656 Z:\\path\\YgoMasterClient.exe\n"
            "657 /usr/bin/wine YgoMaster.exe\n"
            "657 wine YgoMaster.exe\n"
            "658 wine64 YgoMasterClient.exe\n"
            "789 Z:\\path\\masterduel.exe\n"
        )

        matches = prep.detect_running_game_processes(ps_text)

        self.assertEqual(
            matches,
            [
                "456 Z:\\path\\MonoRun.exe YgoMaster.exe",
                "654 Z:\\path\\MonoRun.exe YgoMasterClient.exe",
                "655 Z:\\path\\YgoMaster.exe",
                "656 Z:\\path\\YgoMasterClient.exe",
                "657 /usr/bin/wine YgoMaster.exe",
                "657 wine YgoMaster.exe",
                "658 wine64 YgoMasterClient.exe",
                "789 Z:\\path\\masterduel.exe",
            ],
        )

    def test_status_rejects_mutating_flags(self):
        with contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit):
                prep.main(["--status", "--settings"])


if __name__ == "__main__":
    unittest.main()

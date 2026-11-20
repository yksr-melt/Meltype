# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 appipinopi
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("publisher", Path(__file__).with_name("publish-rust-release.py"))
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)


class PresentationTests(unittest.TestCase):
    def assets(self, directory):
        for name in publisher.PACKAGES + publisher.DOCUMENTS[:2]:
            (directory / name).write_bytes(b"fixture")
        (directory / "SHA256SUMS.txt").write_text("".join(
            f"{hashlib.sha256(b'fixture').hexdigest()}  {name}\n" for name in publisher.PACKAGES), encoding="utf-8")

    def test_draft_without_tag_is_found_on_later_pages(self):
        draft = {"id": 42, "tag_name": "flash-v0.1.0-preview.5", "draft": True}
        pages = [[{"tag_name": "flash-v0.1.0-preview.50", "draft": False}], [draft]]
        with patch.object(publisher, "gh", return_value=json.dumps(pages)) as command:
            self.assertEqual(publisher.find_release(draft["tag_name"]), draft)
        command.assert_called_once_with("api", f"repos/{publisher.REPO}/releases?per_page=100", "--paginate", "--slurp")

    def test_published_release_is_also_detected_to_prevent_overwrite(self):
        release = {"id": 43, "tag_name": "flash-v0.1.0-preview.4", "draft": False}
        with patch.object(publisher, "gh", return_value=json.dumps([[release]])):
            self.assertEqual(publisher.find_release(release["tag_name"]), release)

    def test_missing_release_is_distinct_from_an_api_error(self):
        with patch.object(publisher, "gh", return_value=json.dumps([[]])):
            self.assertIsNone(publisher.find_release("flash-v0.1.0-preview.5"))
        with patch.object(publisher, "gh", side_effect=RuntimeError("API unavailable")):
            with self.assertRaisesRegex(RuntimeError, "API unavailable"):
                publisher.find_release("flash-v0.1.0-preview.5")

    def test_only_all_platforms_licenses_and_sums_are_accepted(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            self.assets(directory)
            publisher.validate_assets(directory)
            for extra in ("speed-1.png", "BUILD-PROVENANCE.json", "README-iOS.md", "RELEASE-NOTES.md", "benchmark.zip", "Meltype-flash-Windows.zip"):
                with self.subTest(extra=extra):
                    (directory / extra).write_bytes(b"extra")
                    with self.assertRaises(ValueError):
                        publisher.validate_assets(directory)
                    (directory / extra).unlink()
            (directory / publisher.PACKAGES[6]).unlink()
            with self.assertRaises(ValueError):
                publisher.validate_assets(directory)

    def test_tampered_package_fails_before_publication(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            self.assets(directory)
            (directory / publisher.PACKAGES[0]).write_bytes(b"changed")
            with self.assertRaisesRegex(ValueError, "SHA256"):
                publisher.validate_assets(directory)

    def test_windows_newlines_do_not_split_table_rows(self):
        text = "# Title\r\r\n\r\n|OS|File|\r\r\n|---|---|\r\r\n|Windows|app.zip|\r\r\n"
        fixed = publisher.normalize_markdown(text)
        self.assertIn("|OS|File|\n|---|---|\n|Windows|app.zip|", fixed)
        self.assertNotIn("\r", fixed)

    def test_body_has_all_os_and_inline_images_without_benchmark_download_links(self):
        benchmark = "# 測定\n\n## 全入力の中央値\n\n|入力|値|\n|---|---|\n|sample|1|\n"
        title, body = publisher.compose_body("flash-v0.1.0-preview.4", "a" * 40, benchmark, "b" * 40, "fix: table\nfix: `quoted` <tag>")
        self.assertEqual(title, "Meltype-FLASH v0.1.0 Pre4")
        for os_name in ("Windows", "Linux", "macOS", "Android", "ChromeOS", "iOS"):
            self.assertIn(f"|{os_name}|成功|未実施|", body)
        for scope in ("CI専用試験", "隔離IBus", "エミュレーター", "実WASM・模擬IME API", "iOS Simulator"):
            self.assertIn(scope, body)
        self.assertIn("通常アプリ・物理端末でのIME入力確認とは別です", body)
        self.assertEqual(body.count(".png)"), 10)
        self.assertEqual(body.count(".svg)"), 10)
        self.assertIn("/" + "b" * 40 + "/flash-v0.1.0-preview.4/", body)
        self.assertNotIn("releases/download/flash-v0.1.0-preview.4/cpu.png", body)
        self.assertNotIn("[全測定", body)
        self.assertIn("&lt;tag&gt;", body)

    def test_stable_body_uses_release_title_and_retains_signing_limits(self):
        title, body = publisher.compose_body("flash-v1.0.0", "a" * 40, "測定結果", "b" * 40, "fix: release")
        self.assertEqual(title, "Meltype-FLASH v1.0.0")
        self.assertIn("共通Rustコアを使う正式版です", body)
        self.assertNotIn("開発プレビュー", body)
        self.assertIn("iOSのIPAは未署名です", body)
        self.assertIn("Androidはdebug署名、macOSはアドホック署名です", body)
        for os_name in ("Windows", "macOS", "Linux", "Android", "ChromeOS", "iOS"):
            self.assertIn(f"|{os_name}|成功|未実施|", body)
        self.assertEqual(body.count(".png)"), 10)
        self.assertEqual(body.count(".svg)"), 10)
        self.assertIn("/" + "b" * 40 + "/flash-v1.0.0/", body)

    def test_only_flash_stable_and_preview_tags_are_accepted(self):
        self.assertEqual(publisher.parse_tag("flash-v1.0.0"), ("1.0.0", None))
        self.assertEqual(publisher.parse_tag("flash-v0.1.0-preview.7"), ("0.1.0", "7"))
        for tag in ("v1.0.0", "rust-v1.0.0", "flash-v1.0.0-pre.1", "flash-v1.0.0-preview", "flash-v1.0.0-preview.1-extra"):
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                publisher.parse_tag(tag)

    def test_zero_padded_tag_uses_the_unpadded_release_notes_heading(self):
        self.assertEqual(publisher.notes_heading("flash-v0001.0001.0019"), "# Meltype-FLASH v1.1.19\n")
        self.assertEqual(publisher.notes_heading("flash-v1.0.0"), "# Meltype-FLASH v1.0.0\n")
        self.assertEqual(publisher.notes_heading("flash-v0000.0001.0000-preview.4"), "# Meltype-FLASH v0.1.0 Pre4\n")

    def publish_fixture(self, root, tag, version, release_override=None, skip_benchmark=False):
        assets = root / "assets"
        assets.mkdir()
        self.assets(assets)
        output = root / "body.md"
        lookups = []

        def find_release(release_tag):
            lookups.append(release_tag)
            if len(lookups) == 1:
                return None
            release = {"draft": True, "prerelease": "-preview." in tag, "html_url": "https://example.invalid/release",
                       "body": output.read_text(encoding="utf-8"), "assets": [
                           {"name": name, "digest": "sha256:" + hashlib.sha256((assets / name).read_bytes()).hexdigest()}
                           for name in publisher.PACKAGES + publisher.DOCUMENTS]}
            release.update(release_override or {})
            return release

        arguments = ["publish-rust-release.py", "--tag", tag, "--sha", "a" * 40, "--assets", str(assets),
                     "--output", str(output), "--publish"]
        arguments += ["--skip-benchmark"] if skip_benchmark else ["--benchmark", str(root / "benchmark")]
        source_outputs = ["a" * 40] + (["flash-v1.0.4"] if skip_benchmark else []) + ["fix: release"]
        with patch.object(sys, "argv", arguments), \
             patch.object(publisher.subprocess, "check_output", side_effect=source_outputs), \
             patch.object(publisher.tomllib, "loads", return_value={"workspace": {"package": {"version": version}}}), \
             patch.object(publisher, "validate_benchmark", return_value=({"old_commit": "c" * 40}, "測定結果")) as measurement, \
             patch.object(publisher, "validate_rendered"), \
             patch.object(publisher, "find_release", side_effect=find_release), \
             patch.object(publisher, "publish_images", return_value="b" * 40) as images, \
             patch.object(publisher, "gh") as command, patch("builtins.print"):
            publisher.main()
        if skip_benchmark:
            measurement.assert_not_called()
            images.assert_not_called()
            body = output.read_text(encoding="utf-8")
            self.assertIn("ベンチマークを実施していません", body)
            self.assertNotIn("raw.githubusercontent.com", body)
        self.assertEqual(lookups, [tag, tag])
        return command

    def test_explicit_skip_publishes_all_assets_without_measurements_or_images(self):
        with tempfile.TemporaryDirectory() as temporary:
            command = self.publish_fixture(Path(temporary), "flash-v1.0.5", "1.0.5", skip_benchmark=True)
            self.assertIn("--draft", command.call_args.args)
            for name in publisher.PACKAGES:
                self.assertTrue(any(argument.endswith(name) for argument in command.call_args.args))

    def test_skipped_benchmark_rendering_requires_both_tables_and_no_images(self):
        with patch.object(publisher, "gh", return_value='<table></table>' * 2):
            publisher.validate_rendered("body", expected_images=0)
        for html in ('<table></table>', '<table></table>' * 2 + '<img src="stale">'):
            with patch.object(publisher, "gh", return_value=html), self.assertRaises(ValueError):
                publisher.validate_rendered("body", expected_images=0)

    def test_publish_keeps_stable_and_preview_releases_as_drafts(self):
        for tag, version, flag in (("flash-v1.0.0", "1.0.0", "--prerelease=false"),
                                   ("flash-v0.1.0-preview.7", "0.1.0-pre.7", "--prerelease=true")):
            with self.subTest(tag=tag), tempfile.TemporaryDirectory() as temporary:
                command = self.publish_fixture(Path(temporary), tag, version)
                arguments = command.call_args.args
                self.assertEqual(arguments[:3], ("release", "create", tag))
                self.assertIn(flag, arguments)
                self.assertIn("--draft", arguments)
                command.assert_called_once()

    def test_publish_rejects_wrong_release_settings_after_upload(self):
        for override in ({"draft": False}, {"prerelease": True}):
            with self.subTest(override=override), tempfile.TemporaryDirectory() as temporary:
                with self.assertRaisesRegex(ValueError, "下書き・プレリリース設定"):
                    self.publish_fixture(Path(temporary), "flash-v1.0.0", "1.0.0", override)

    def test_github_rendering_requires_tables_and_ten_images(self):
        with patch.object(publisher, "gh", return_value='<table role="table"></table>' * 3 + '<img src="a">' * 10):
            publisher.validate_rendered("body")
        for html in ("<p>broken table</p>", "<table>" * 3, '<table role="table"></table>' * 3 + '<img src="a">' * 5):
            with patch.object(publisher, "gh", return_value=html), self.assertRaises(ValueError):
                publisher.validate_rendered("body")

    def test_benchmark_must_match_source_report_and_charts(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            (directory / "charts").mkdir()
            report = {"comparison_kind": "rust-versions", "new_commit": "a" * 40, "old_commit": "b" * 40,
                      "binaries_unchanged": True, "sources_unchanged": True, "accepted_performance_baseline": True,
                      "results": [{"case": "sample"}], "process_resources": [{}],
                      "versions": ["csharp", "old", "new"]}
            report_file = directory / "report.json"
            report_file.write_text(json.dumps(report), encoding="utf-8")
            summary_file = directory / "charts/summary.json"
            summary_file.write_text(json.dumps({"full_report_sha256": hashlib.sha256(report_file.read_bytes()).hexdigest()}), encoding="utf-8")
            (directory / "charts/README.md").write_text("## 全入力の中央値\n\n## CPU・RAMのプロセス中央値\n\n## 表示範囲外の測定値\n\n|項目|値|\n|---|---|\n|sample|999|\n\n## 全画像\n\n[raw](data.csv)", encoding="utf-8")
            for name in publisher.IMAGES:
                (directory / "charts" / name).write_bytes(b"\x89PNG\r\n\x1a\nfixture")
            for name in publisher.ZOOM_IMAGES:
                (directory / "charts" / name).write_text('<svg xmlns="http://www.w3.org/2000/svg"/>', encoding="utf-8")
            _, body = publisher.validate_benchmark(directory, "a" * 40)
            self.assertNotIn("data.csv", body)
            self.assertIn("表示範囲外の測定値", body)
            self.assertIn("|sample|999|", body)
            with self.assertRaises(ValueError):
                publisher.validate_benchmark(directory, "c" * 40)
            summary_file.write_text('{"full_report_sha256":"stale"}', encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "一致"):
                publisher.validate_benchmark(directory, "a" * 40)

    def test_media_commit_uses_only_image_branch_and_preserves_parent(self):
        calls = []

        def fake_api(path, payload=None, method=None):
            calls.append((path, payload, method))
            if "matching-refs" in path:
                return [{"ref": "refs/heads/release-media", "object": {"sha": "parent"}}]
            if path == "git/commits/parent":
                return {"tree": {"sha": "old-tree"}}
            return {"sha": "media-commit"}

        with tempfile.TemporaryDirectory() as temporary, patch.object(publisher, "api", side_effect=fake_api):
            charts = Path(temporary)
            for name in publisher.IMAGES + publisher.ZOOM_IMAGES:
                (charts / name).write_bytes(b"png")
            self.assertEqual(publisher.publish_images("flash-v0.1.0-preview.4", charts), "media-commit")
        tree = next(payload for path, payload, _ in calls if path == "git/trees")
        self.assertEqual(tree["base_tree"], "old-tree")
        self.assertEqual({entry["path"].split("/")[-1] for entry in tree["tree"]}, set(publisher.IMAGES + publisher.ZOOM_IMAGES))
        ref, payload, method = calls[-1]
        self.assertEqual((ref, method, payload["force"]), ("git/refs/heads/release-media", "PATCH", False))


if __name__ == "__main__":
    unittest.main()

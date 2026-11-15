# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 appipinopi
"""APT のカタログを作るときの、タグと deb の版の照合。"""
import importlib.util
from pathlib import Path
import unittest

PATH = Path(__file__).with_name("publish-linux-apt.py")
SPEC = importlib.util.spec_from_file_location("publish_linux_apt", PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class TagVersionTests(unittest.TestCase):
    def test_zero_padded_tag_matches_the_deb_version(self):
        # タグはゼロ埋め (flash-v0001.0001.0020)、deb の版は 1.1.20
        self.assertEqual(MODULE.tag_version("flash-v0001.0001.0020"), "1.1.20")
        self.assertEqual(MODULE.tag_version("flash-v1.1.20"), "1.1.20")
        self.assertEqual(MODULE.tag_version("flash-v0001.0001.0019"), "1.1.19")

    def test_only_final_release_tags_are_published(self):
        with self.assertRaises(ValueError):
            MODULE.build("flash-v0001.0001.0020-preview.1", "unused")
        with self.assertRaises(ValueError):
            MODULE.build("v1.1.20", "unused")


if __name__ == "__main__":
    unittest.main()

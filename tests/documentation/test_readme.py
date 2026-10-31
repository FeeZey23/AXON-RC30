"""Portable, dependency-free contracts for the bilingual AXON RC30 landing page.
Run: python -m unittest discover -s tests/documentation -v
These checks do not replace Windows build or installer acceptance.
"""
from pathlib import Path
import re
import unittest
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[2]
LABELS = [
    'AXON RC30 - English Description and Overview',
    'AXON RC30 - Step.by.Step.Guide',
    'AXON RC30 - Deutsche Beschreibung und Übersicht',
    'AXON RC30 - Schritt.für.Schritt.Anleitung',
]

class LandingPageTests(unittest.TestCase):
    def setUp(self):
        self.readme = (ROOT / 'README.md').read_text(encoding='utf-8-sig')
        self.sections = re.findall(r'<details>\s*<summary><strong>(.*?)</strong></summary>\s*\n(.*?)\n</details>', self.readme, re.S)

    def test_four_native_closed_dropdowns_in_requested_order(self):
        self.assertEqual(LABELS, [label for label, _ in self.sections])
        self.assertEqual(4, self.readme.count('<details>'))
        self.assertNotRegex(self.readme, r'<details\s+open')
        self.assertNotIn('<script', self.readme.lower())

    def test_short_branded_preview_precedes_language_selection(self):
        preview = self.readme.split('<details>', 1)[0]
        self.assertIn('# AXON RC30\n', preview)
        self.assertNotIn('ArenaDesktopCompanion', preview)
        self.assertIn('Separate Arena chats.', preview)
        self.assertIn('Getrennte Arena-Chats.', preview)
        self.assertIn('application-ui-smoke.png', preview)
        self.assertLess(len(preview), 2000)

    def test_both_guides_have_nine_real_steps_and_checklists(self):
        for language in ['en', 'de']:
            text = (ROOT / f'docs/STEP-BY-STEP.{language}.md').read_text()
            self.assertEqual(list(map(str, range(1, 10))), re.findall(r'^### (\d+)\.', text, re.M))
            self.assertGreaterEqual(text.count('- [ ]'), 6)
            for marker in ['Get-FileHash', 'WebView2', 'Connect', 'AXON%20RC30.sln', 'HTTP 403', 'HTTP 429', 'HTTP 503 / 507']:
                self.assertIn(marker, text)

    def test_embedded_guides_match_separate_pages(self):
        for index, language in [(1, 'en'), (3, 'de')]:
            text = (ROOT / f'docs/STEP-BY-STEP.{language}.md').read_text()
            body = text[text.index('## '):].strip().replace('](../', '](./')
            self.assertEqual(body, self.sections[index][1].strip())

    def test_local_markdown_links_exist(self):
        files = [ROOT / 'README.md', *list((ROOT / 'docs').glob('*.md')), ROOT / 'PROJECT.OVERVIEW.md']
        for doc in files:
            for value in re.findall(r'\]\(([^\s)]+)\)', doc.read_text(encoding='utf-8-sig')):
                if urlsplit(value).scheme or value.startswith('#'):
                    continue
                path = (doc.parent / unquote(value.split('#')[0])).resolve()
                self.assertTrue(path.is_relative_to(ROOT), str(path))
                self.assertTrue(path.exists(), str(path))

    def test_installer_hash_and_gate_caveats_match_both_languages(self):
        digest = "Get-Content -LiteralPath ($msi + '.sha256')"
        for language, manual, unsigned in [('en', 'Send the draft yourself', 'unsigned'), ('de', 'Den Entwurf selbst absenden', 'unsigniert')]:
            text = (ROOT / f'docs/STEP-BY-STEP.{language}.md').read_text()
            for marker in [digest, manual, unsigned, 'installer-manifest.json', 'Install-Local.ps1', 'SkipUiSmoke']:
                self.assertIn(marker, text)

    def test_named_solution_preserves_compatibility(self):
        new = (ROOT / 'AXON RC30.sln').read_text(encoding='utf-8-sig')
        old = (ROOT / 'ArenaDesktopCompanion.sln').read_text(encoding='utf-8-sig')
        self.assertEqual(old, new)
        self.assertIn('= "AXON RC30",', new)
        paths = re.findall(r'Project\([^\n]+? = "[^"]+", "([^"]+\.csproj)"', new)
        self.assertEqual(5, len(paths))
        for path in paths:
            self.assertTrue((ROOT / path.replace('\\', '/')).exists(), path)

    def test_guides_do_not_claim_new_msi_or_os_isolation(self):
        self.assertIn('keine Betriebssystem-Sandbox', self.readme)
        self.assertIn('historische Baselinewerte', self.readme)
        self.assertIn('or an operating-system sandbox', self.readme)
        self.assertIn('Der Nutzer sendet selbst', self.readme)

    def test_manual_ci_only(self):
        workflow = (ROOT / '.github/workflows/windows-validation.yml').read_text()
        self.assertIn('workflow_dispatch:', workflow)
        self.assertNotRegex(workflow, re.compile(r'^\s*(push|pull_request|release):', re.M))

if __name__ == '__main__':
    unittest.main()

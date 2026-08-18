import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
ENDPOINTS = ROOT / "src" / "Quest.Studio" / "QuestStudioEndpoints.cs"
EXPORT = ROOT / "src" / "Quest.Studio" / "QuestStudioDataExport.cs"
USAGE = ROOT / "src" / "Quest.Studio" / "QuestStudioUsageInsights.cs"


class QuestStudioDataControlTests(unittest.TestCase):
    def test_sensitive_data_and_usage_routes_are_authorized_and_no_store(self) -> None:
        source = ENDPOINTS.read_text(encoding="utf-8")
        routes = (
            '/api/v2/quest-studio/projects",',
            '/api/v2/quest-studio/projects/{projectId}",',
            '/api/v2/quest-studio/projects/{projectId}/duplicate',
            '/api/v2/quest-studio/projects/{projectId}/bump-patch',
            '/api/v2/quest-studio/projects/{projectId}/validate',
            '/api/v2/quest-studio/projects/{projectId}/certify',
            '/api/v2/quest-studio/projects/{projectId}/rehearse',
            '/api/v2/quest-studio/projects/{projectId}/publish',
            '/api/v2/quest-studio/projects/{projectId}/history',
            '/api/v2/quest-studio/projects/{projectId}/runtime-status',
            '/api/v2/quest-studio/projects/{projectId}/export',
            '/api/v2/quest-studio/projects/{projectId}/questpack',
            '/api/v2/quest-studio/usage',
            '/api/v2/quest-studio/usage/settings',
            '/api/v2/quest-studio/usage/export',
            '/api/v2/quest-studio/usage/reset',
            '/api/v1/workbench/quest-studio/project',
            '/api/v1/workbench/quest-studio/receipts',
            '/api/v1/workbench/quest-studio/history',
            '/api/v1/workbench/quest-studio/diff',
        )
        for route in routes:
            start = source.index(route)
            next_map = source.find("app.Map", start + len(route))
            block = source[start : next_map if next_map >= 0 else len(source)]
            self.assertIn("host.Authorize(request)", block, route)
            self.assertIn("NoStore(response)", block, route)
        self.assertIn('response.Headers.CacheControl = "no-store"', source)
        self.assertIn('response.Headers["X-Content-Type-Options"] = "nosniff"', source)

    def test_bundle_is_ephemeral_fixed_path_bounded_and_hash_manifested(self) -> None:
        source = EXPORT.read_text(encoding="utf-8")
        for expected in (
            "MaxEntries = 512",
            "128L * 1024 * 1024",
            '"project/draft.json"',
            '"project/history/{stem}.json"',
            '"compiled/experience.json"',
            '"compiled/diagnostics.json"',
            '"evidence/runtime-status.json"',
            '"packages/published/"',
            "SHA256.HashData",
            "byte_count",
            "sha256",
            "SafeEntry",
            "SafeQuestpackName",
        ):
            self.assertIn(expected, source)
        self.assertNotIn("File.WriteAllBytes", source)
        self.assertNotIn("CreateTemp", source)
        self.assertIn("Local Runtime evidence is excluded by default.", source)

    def test_usage_store_has_no_ingest_route_or_network_transport(self) -> None:
        endpoints = ENDPOINTS.read_text(encoding="utf-8")
        source = USAGE.read_text(encoding="utf-8")
        self.assertNotIn('/usage/record', endpoints)
        self.assertNotIn("HttpClient", source)
        self.assertNotIn("WebClient", source)
        self.assertNotIn("System.Net", source)
        self.assertIn('Path.Combine(host.StateDirectory, "quest-studio", "usage")', source)
        self.assertIn("RetentionWeeks = 13", source)
        self.assertIn("Templates.Contains", source)
        self.assertIn("Events.Contains", source)
        self.assertIn("Actions.Contains", source)
        self.assertIn("TryWrite", source)
        self.assertIn("reset_confirmation_required", source)
        self.assertIn("No authored text, targets, project/player/world identity", source)


if __name__ == "__main__":
    unittest.main()

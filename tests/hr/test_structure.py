#!/usr/bin/env python3
"""Repository-level guards; no SDK, browser, database or external Python packages needed."""
import re
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
INVENTORY = ROOT / "inventory"
CLIENT = INVENTORY / "src/Inventory.Client"


class HrStructureTests(unittest.TestCase):
    def test_one_paging_contract(self):
        definitions = []
        for source in (INVENTORY / "src/Inventory.Shared").rglob("*.cs"):
            if re.search(r"class\s+PagedResult\s*<", source.read_text()):
                definitions.append(source.name)
        self.assertEqual(definitions, ["PagedResult.cs"])

    def test_standard_reference_assemblies(self):
        props = ET.parse(INVENTORY / "Directory.Build.props").getroot()
        self.assertEqual(props.findtext(".//ProduceReferenceAssembly"), "true")
        self.assertFalse((INVENTORY / "Directory.Build.targets").exists())
        for project in INVENTORY.rglob("*.csproj"):
            tree = ET.parse(project).getroot()
            self.assertNotEqual(tree.findtext(".//ProduceReferenceAssembly"), "false", project)
            self.assertFalse(tree.findall(".//HintPath"), project)
            for reference in tree.findall(".//ProjectReference"):
                path = project.parent / reference.attrib["Include"].replace("\\", "/")
                self.assertTrue(path.is_file(), path)

    def test_no_standalone_hr_projects_or_host(self):
        solution = (INVENTORY / "Inventory.sln").read_text()
        paths = re.findall(r'Project\([^\n]+? = "[^"]+", "([^"]+\.csproj)"', solution)
        self.assertTrue(paths)
        for path in paths:
            self.assertTrue((INVENTORY / path.replace("\\", "/")).is_file(), path)
        self.assertNotIn("RadisHr.Client", solution)
        self.assertNotIn("RadisHr.Shared", solution)
        self.assertFalse((INVENTORY / "modules/RadisHr").exists())
        self.assertNotIn("<Compile Include=", (INVENTORY / "src/Inventory.Api/Inventory.Api.csproj").read_text())
        self.assertNotIn("radis-hr/index.html", (INVENTORY / "src/Inventory.Api/Program.cs").read_text())

    def test_pages_and_navigation_are_native(self):
        pages = [p for p in (CLIENT / "Pages/RadisHr").glob("*.razor") if p.name != "_Imports.razor"]
        self.assertEqual(len(pages), 13)
        routes = []
        for page in pages:
            page_routes = re.findall(r'@page "([^"]+)"', page.read_text())
            self.assertTrue(any(r == "/hr" or r.startswith("/hr/") for r in page_routes), page)
            self.assertTrue(all(r.startswith(("/hr", "/radis-hr")) for r in page_routes), page)
            routes.extend(page_routes)
        self.assertEqual(len(routes), len(set(routes)))
        navigation = (CLIENT / "Services/RadisHr/AppNav.cs").read_text()
        for route in re.findall(r'new\("[^"]+",\s*"[^"]+",\s*"([^"]+)"', navigation):
            self.assertIn(route, routes)
        menu = (CLIENT / "Layout/NavMenu.razor").read_text()
        self.assertIn("Services.RadisHr.AppNav.Items", menu)
        self.assertNotIn("OpenRadisHr", menu)
        self.assertIn("@layout Inventory.Client.Layout.MainLayout", (CLIENT / "Layout/RadisHrLayout.razor").read_text())
        self.assertIn("@layout Inventory.Client.Layout.RadisHrLayout", (CLIENT / "Pages/RadisHr/_Imports.razor").read_text())

    def test_hr_styles_do_not_leak_into_projects_or_shell(self):
        for css in (CLIENT / "wwwroot/css/radis-hr").glob("*.css"):
            text = re.sub(r"/\*.*?\*/", "", css.read_text(), flags=re.S)
            self.assertNotIn("@page", text, css)
            for selector in re.findall(r"(?:^|[{}])\s*([^{}]+)\{", text):
                if selector.lstrip().startswith("@"):
                    continue
                for part in selector.split(","):
                    self.assertTrue(part.strip().startswith(".radis-hr"), (css, part))
                    self.assertNotIn(".radis-hr .radis-hr", part, css)

    def test_hr_assets_are_in_inventory_publish(self):
        html = (CLIENT / "wwwroot/index.html").read_text()
        self.assertEqual(html.count("_framework/blazor.webassembly.js"), 1)
        self.assertIn('src="js/radis-interop.js"', html)
        for asset in re.findall(r'(?:src|href)="((?:css/radis-hr/|js/radis-)[^"]+)"', html):
            self.assertTrue((CLIENT / "wwwroot" / asset).is_file(), asset)
        for name in ["deploy-single.ps1", "deploy-single.sh", "RUN.ps1"]:
            script = (INVENTORY / name).read_text()
            self.assertNotIn("modules/RadisHr", script)
            self.assertNotIn("publish/radis-hr", script)
            self.assertNotIn("Remove-Item $dest -Recurse", script)
            self.assertNotIn("rm -rf src/Inventory.Api/wwwroot", script)

    def test_one_identity_and_no_default_hr_passwords(self):
        adapter = (CLIENT / "Services/RadisHr/RadisHrAuthState.cs").read_text()
        self.assertIn("IAuthState auth", adapter)
        self.assertNotIn("localStorage", re.sub(r"///[^\n]*", "", adapter))
        self.assertNotIn("LoginAsync", adapter)
        self.assertFalse((INVENTORY / "src/Inventory.Api/Controllers/RadisHr/AuthController.cs").exists())
        self.assertFalse((INVENTORY / "src/Inventory.Api/Services/RadisHr/TokenService.cs").exists())
        for controller in (INVENTORY / "src/Inventory.Api/Controllers/RadisHr").glob("*.cs"):
            self.assertIn('[Authorize(Policy = "RadisHrAccess")]', controller.read_text())
        self.assertNotIn("Radis@1405", (INVENTORY / "src/Inventory.Api/Data/RadisHr/DbSeeder.cs").read_text())


if __name__ == "__main__":
    unittest.main()

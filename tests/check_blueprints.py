"""Fails when a primary blueprint of this mod makes an item it doesn't define itself.

The game keeps one blueprint per result item (MyDefinitionManager.InitBlueprints). A
primary blueprint loaded from a mod replaces the vanilla primary one for that item, and
everything that looks blueprints up by result then gets the mod's: the build planner's
"add to production" (MyTerminalInventoryController.AddComponentsToProduction) and the
minimal price calculator. If no assembler can use the mod's blueprint, the build planner
can't queue that item at all (SE-1860: Construction components).

    python tests/check_blueprints.py
"""

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

DATA = Path(__file__).resolve().parent.parent / "Data"


def item_id(element: ET.Element) -> tuple[str, str]:
    return (
        element.findtext("TypeId") or element.get("TypeId"),
        element.findtext("SubtypeId") or element.get("SubtypeId"),
    )


def main() -> int:
    defined = set()
    blueprints = []
    for path in DATA.rglob("*.sbc"):
        root = ET.parse(path).getroot()
        for section in root:
            if section.tag == "Blueprints":
                blueprints += [(path, b) for b in section.findall("Blueprint")]
            else:
                defined |= {
                    item_id(d.find("Id")) for d in section if d.find("Id") is not None
                }

    failures = []
    for path, blueprint in blueprints:
        results = blueprint.findall("Result") or blueprint.findall("Results/Item")
        primary = (blueprint.findtext("IsPrimary") or "false").strip().lower() == "true"
        if primary and len(results) == 1 and item_id(results[0]) not in defined:
            name = item_id(blueprint.find("Id"))[1]
            failures.append(
                f"{path.relative_to(DATA.parent)}: primary blueprint {name} makes {item_id(results[0])}"
            )

    for failure in failures:
        print("FAIL", failure)
    print(f"{len(blueprints)} blueprint(s), {len(failures)} overriding a vanilla item")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())

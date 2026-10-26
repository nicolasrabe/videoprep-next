"""Print every failed test in the .trx files as a GitHub Actions error annotation.

Annotations are readable without logging in (Checks API), so a failure on macOS/Linux can be diagnosed from the
run page or from the API. Usage: python trx_annotations.py <folder with .trx files>
"""
import pathlib
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def esc(text: str) -> str:
    return text.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def main(folder: str) -> int:
    failed = 0
    for trx in sorted(pathlib.Path(folder).rglob("*.trx")):
        root = ET.parse(trx).getroot()
        for r in root.iterfind(".//t:UnitTestResult", NS):
            if r.get("outcome") != "Failed":
                continue
            failed += 1
            msg = r.findtext(".//t:ErrorInfo/t:Message", default="", namespaces=NS).strip()
            stack = r.findtext(".//t:ErrorInfo/t:StackTrace", default="", namespaces=NS).strip()
            body = (msg + "\n" + "\n".join(stack.splitlines()[:6]))[:3000]
            print(f"::error title=Test failed: {esc(r.get('testName', '?'))}::{esc(body)}")
    print(f"{failed} failed test(s) reported")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "TestResults"))

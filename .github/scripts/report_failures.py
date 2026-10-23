"""Prints failed tests from a .trx file as GitHub annotations, including the error message."""
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def escape(text: str) -> str:
    # Annotation messages must be on one line; GitHub decodes %0A back to new lines
    return text.replace("%", "%25").replace("\r", "").replace("\n", "%0A")


root = ET.parse(sys.argv[1]).getroot()
for result in root.iterfind(".//t:UnitTestResult[@outcome='Failed']", NS):
    name = result.get("testName", "unknown test")
    message = result.findtext(".//t:ErrorInfo/t:Message", default="", namespaces=NS).strip()
    stack = result.findtext(".//t:ErrorInfo/t:StackTrace", default="", namespaces=NS).strip()
    first_frames = "\n".join(line.strip() for line in stack.splitlines()[:4])
    print(f"::error title=Failed test {name}::{escape(message[:1500] + chr(10) + first_frames)}")

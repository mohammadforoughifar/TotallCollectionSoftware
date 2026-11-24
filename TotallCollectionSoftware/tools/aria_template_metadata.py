"""Synchronize company 1005 internal/minutes artwork + metadata from its approved outgoing MRT.
Only the watermark image and three value boxes change. Body, data dictionary and workflow stay intact.
Run from any directory: python tools/aria_template_metadata.py
"""
from pathlib import Path
import hashlib
import re
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape

ATTRIBUTES = ("ClientRectangle", "Font", "HorAlignment", "VertAlignment", "Brush", "TextBrush",
              "CanGrow", "CanShrink", "WordWrap", "ShrinkFontToFit", "ShrinkFontToFitMinimumSize", "TextOptions")


def mapping(xml):
    names = {e.tag for e in xml.find("./Pages/Page1/Components")}
    return {"NumberValue": "Text2", "MeetingDateValue" if "MeetingDateValue" in names else "DateValue": "Text1",
            "AttachmentValue": "Text3"}


def unaffected_hash(text):
    xml = ET.fromstring(text)
    xml.find(".//Watermark/ImageBytes").text = "ARTWORK"
    for name in mapping(xml):
        component = xml.find("./Pages/Page1/Components/" + name)
        for child in list(component):
            if child.tag in ATTRIBUTES:
                component.remove(child)
        # Retain bindings, refs, names and all non-layout attributes in the scope hash.
    return hashlib.sha256(ET.canonicalize(ET.tostring(xml, encoding="unicode"), strip_text=True).encode()).hexdigest()


def once(text, pattern, replacement):
    text, count = re.subn(pattern, replacement, text, flags=re.S)
    if count != 1:
        raise ValueError(f"Expected one XML component, got {count}")
    return text


def sync_aria_metadata(text, donor_text):
    """Use outgoing's native PNG and calibrated physical rectangles, preserving target bindings."""
    target = ET.fromstring(text)
    donor = ET.fromstring(donor_text)
    if target.findtext("ReportUnit") != "Inches" or donor.findtext("ReportUnit") != "Inches":
        raise ValueError("Unexpected report unit")
    before = unaffected_hash(text)
    margins = [float(v) for v in target.findtext("./Pages/Page1/Margins").split(",")]
    donor_margins = [float(v) for v in donor.findtext("./Pages/Page1/Margins").split(",")]
    image = donor.findtext(".//Watermark/ImageBytes")
    text = once(text, r"(<Watermark\b[^>]*>)(.*?)(</Watermark>)",
                lambda m: m[1] + once(m[2], r"<ImageBytes>.*?</ImageBytes>", lambda _: "<ImageBytes>" + image + "</ImageBytes>") + m[3])
    for name, source_name in mapping(target).items():
        source = donor.find("./Pages/Page1/Components/" + source_name)
        values = {key: source.findtext(key) for key in ATTRIBUTES}
        if any(v is None for v in values.values()):
            raise ValueError("Outgoing Aria must already contain the approved fixed-box metadata update")
        x, y, width, height = [float(v) for v in values["ClientRectangle"].split(",")]
        values["ClientRectangle"] = ",".join(f"{v:.6f}" for v in
            (x + donor_margins[0] - margins[0], y + donor_margins[2] - margins[2], width, height))
        def edit(match):
            body = match[2]
            newline = "\r\n" if "\r\n" in body else "\n"
            indent = re.search(r"(?:\r?\n)([ \t]+)<", body).group(1)
            for key, value in values.items():
                tag = f"<{key}>{escape(value)}</{key}>"
                if f"<{key}>" in body:
                    body = once(body, rf"<{key}>.*?</{key}>", lambda _, tag=tag: tag)
                else:
                    body = newline + indent + tag + body
            return match[1] + body + match[3]
        text = once(text, rf"(<{name}\b[^>]*>)(.*?)(</{name}>)", edit)
    if unaffected_hash(text) != before:
        raise ValueError("Unrelated template content changed")
    return text


def main():
    root = Path(__file__).resolve().parents[1]
    api = root / "inventory/src/Inventory.Api/Reports"
    for size in ("A4", "A5"):
        donor = (api / f"OutgoingLetters/Companies/1005/Outgoing-{size}.mrt").read_bytes().decode("utf-8")
        for target in (api / f"InnerLetters/Companies/1005/Inner-{size}.mrt",
                       api / f"MeetingMinutes/Companies/1005/Minutes-{size}.mrt",
                       root / f"Report/meetingminutes/1005/Report_{size}_MeetingMinutes.mrt"):
            updated = sync_aria_metadata(target.read_bytes().decode("utf-8"), donor)
            target.write_bytes(updated.encode("utf-8"))
            print("Updated", target.relative_to(root))


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Update only company 1005's outgoing A4/A5 watermarks and three metadata boxes.

Requires Pillow with LittleCMS. Pass the original supplied JPEGs; never resize/recompress them.
CMYK is converted using its embedded ICC profile into lossless, full-resolution sRGB PNG.
The original company dictionary, body, signatures and all other templates are preserved.
"""
from pathlib import Path
import argparse
import base64
import hashlib
import io
import json
import re
import xml.etree.ElementTree as ET
from PIL import Image, ImageCms, PngImagePlugin

GEOMETRY = {
    "A4": {"pixels": (2480, 3508), "top": 102.4, "height": 15.0, "font": 9.0,
           "fields": {"Text1": (201.0, 50.0), "Text2": (109.0, 48.0), "Text3": (19.0, 43.0)}},
    "A5": {"pixels": (1753, 2480), "top": 72.35, "height": 10.6, "font": 6.4,
           "fields": {"Text1": (141.5, 35.5), "Text2": (77.0, 34.0), "Text3": (13.5, 30.3)}},
}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def unaffected_xml_hash(xml):
    root = ET.fromstring(xml)
    root.find(".//Watermark/ImageBytes").text = "ARTWORK"
    components = root.find("./Pages/Page1/Components")
    for key in ("Text1", "Text2", "Text3"):
        components.find(key).clear()
    canonical = ET.canonicalize(ET.tostring(root, encoding="unicode"), strip_text=True)
    return sha(canonical.encode())


def convert_source(path, expected_pixels):
    with Image.open(path) as image:
        if image.size != expected_pixels:
            raise ValueError(f"Wrong image/size for {path.name}: {image.size}, expected {expected_pixels}")
        profile_data = image.info.get("icc_profile")
        if image.mode == "CMYK" and not profile_data:
            raise ValueError("CMYK input must have an ICC profile for a controlled colour conversion")
        if profile_data:
            profile = ImageCms.ImageCmsProfile(io.BytesIO(profile_data))
            profile_name = ImageCms.getProfileDescription(profile).strip()
            rgb = ImageCms.profileToProfile(image, profile, ImageCms.createProfile("sRGB"),
                                           outputMode="RGB", renderingIntent=ImageCms.Intent.RELATIVE_COLORIMETRIC)
        else:
            profile_name = "sRGB"
            rgb = image.convert("RGB")
        # Use a standard sRGB chunk, not a generated ICC profile with a timestamp.
        # No resampling or second lossy JPEG encoding is performed.
        rgb.info.clear()
        metadata = PngImagePlugin.PngInfo()
        metadata.add(b"sRGB", b"\x01")
        output = io.BytesIO()
        rgb.save(output, format="PNG", dpi=(300, 300), pnginfo=metadata, compress_level=9)
        return output.getvalue(), sha(rgb.tobytes()), image.mode, profile_name


def replace_one(text, pattern, replacement):
    result, count = re.subn(pattern, replacement, text, flags=re.S)
    if count != 1:
        raise ValueError(f"Expected one XML match, got {count}")
    return result


def update(inventory, inputs):
    records = []
    for size, source in inputs.items():
        geometry = GEOMETRY[size]
        template = inventory / "src/Inventory.Api/Reports/OutgoingLetters/Companies/1005" / f"Outgoing-{size}.mrt"
        original = template.read_bytes().decode("utf-8")
        before_hash = unaffected_xml_hash(original)
        root = ET.fromstring(original)
        if root.findtext("ReportUnit") != "Inches":
            raise ValueError("Unexpected MRT report unit")
        margins = [float(n) for n in root.findtext("./Pages/Page1/Margins").split(",")]
        png, pixels_hash, source_mode, profile_name = convert_source(source, geometry["pixels"])
        encoded = base64.b64encode(png).decode("ascii")
        updated = replace_one(original, r"(<Watermark\b[^>]*>)(.*?)(</Watermark>)",
                              lambda m: m[1] + replace_one(m[2], r"<ImageBytes>.*?</ImageBytes>",
                                                           lambda _: "<ImageBytes>" + encoded + "</ImageBytes>") + m[3])
        for name, (left, width) in geometry["fields"].items():
            ltr = name in ("Text1", "Text2")
            rectangle = (left / 72 - margins[0], geometry["top"] / 72 - margins[2],
                         width / 72, geometry["height"] / 72)
            tags = {
                "ClientRectangle": ",".join(f"{n:.6f}" for n in rectangle),
                "Font": f"Vazirmatn,{geometry['font']:g},Bold",
                "HorAlignment": "Right", "VertAlignment": "Center",
                "Brush": "White", "TextBrush": "Black", "CanGrow": "False",
                "CanShrink": "False", "WordWrap": "False", "ShrinkFontToFit": "True",
                "ShrinkFontToFitMinimumSize": "1",
                "TextOptions": f",,RightToLeft={'False' if ltr else 'True'},,WordWrap=False,A=0",
            }

            def component(match):
                body = match[2]
                newline = "\r\n" if "\r\n" in body else "\n"
                indent = re.search(r"(?:\r?\n)([ \t]+)<", body).group(1)
                for key, value in tags.items():
                    tag = f"<{key}>{value}</{key}>"
                    if f"<{key}>" in body:
                        body = replace_one(body, rf"<{key}>.*?</{key}>", lambda _, tag=tag: tag)
                    else:
                        body = newline + indent + tag + body
                return match[1] + body + match[3]

            updated = replace_one(updated, rf"(<{name}\b[^>]*>)(.*?)(</{name}>)", component)
        if unaffected_xml_hash(updated) != before_hash:
            raise ValueError("An unrelated MRT component changed")
        template.write_bytes(updated.encode("utf-8"))
        records.append({
            "size": size, "source_name": source.name, "source_sha256": sha(source.read_bytes()),
            "source_mode": source_mode, "source_icc": profile_name,
            "format": "PNG", "mode": "RGB", "pixels": geometry["pixels"], "dpi": 300,
            "embedded_sha256": sha(png), "pixel_sha256": pixels_hash,
            "unaffected_xml_sha256": before_hash,
            "top": geometry["top"], "height": geometry["height"],
            "fields": {name: {"left": left, "width": width} for name, (left, width) in geometry["fields"].items()},
        })
        print(f"Updated company 1005 {size}: {geometry['pixels']}, lossless RGB PNG ({len(png)} bytes); unrelated XML preserved.")
    manifest = inventory / "tests/AriaLetterhead/artwork-manifest.json"
    manifest.parent.mkdir(parents=True, exist_ok=True)
    manifest.write_text(json.dumps({"company": 1005, "colour_conversion": "ICC to sRGB, relative colorimetric", "artwork": records},
                                   ensure_ascii=False, indent=2) + "\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--a4", required=True, type=Path)
    parser.add_argument("--a5", required=True, type=Path)
    parser.add_argument("--inventory", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    update(args.inventory, {"A4": args.a4, "A5": args.a5})

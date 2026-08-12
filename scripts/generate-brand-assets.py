#!/usr/bin/env python3
"""Generate deterministic FolderGlimpse production branding assets.

The supplied raster masters remain the approved visual references. Each master
is normalized, resized with high-quality filtering, and sharpened at small sizes.
"""

from __future__ import annotations

import argparse
import io
import shutil
import struct
from pathlib import Path

from PIL import Image, ImageFilter


APP_SIZES = (256, 128, 64, 48, 32, 24, 20, 16)
TRAY_SIZES = (32, 24, 20, 16)


def alpha_bbox(image: Image.Image, threshold: int = 8) -> tuple[int, int, int, int]:
    alpha = image.getchannel("A").point(lambda value: 255 if value >= threshold else 0)
    box = alpha.getbbox()
    if box is None:
        raise ValueError("The source image has no visible pixels")
    return box


def normalize_master(source: Path, output_size: int, padding_ratio: float) -> Image.Image:
    image = Image.open(source).convert("RGBA")
    left, top, right, bottom = alpha_bbox(image)
    cropped = image.crop((left, top, right, bottom))
    usable = round(output_size * (1 - 2 * padding_ratio))
    scale = min(usable / cropped.width, usable / cropped.height)
    size = (max(1, round(cropped.width * scale)), max(1, round(cropped.height * scale)))
    cropped = cropped.resize(size, Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (output_size, output_size), (0, 0, 0, 0))
    canvas.alpha_composite(cropped, ((output_size - size[0]) // 2, (output_size - size[1]) // 2))
    return canvas


def resize_icon(master: Image.Image, size: int) -> Image.Image:
    icon = master.resize((size, size), Image.Resampling.LANCZOS)
    if size <= 64:
        radius = 0.55 if size >= 32 else 0.35
        percent = 115 if size >= 32 else 80
        icon = icon.filter(ImageFilter.UnsharpMask(radius=radius, percent=percent, threshold=2))
    return icon


def write_png(image: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, format="PNG", optimize=True)


def write_ico(images: list[Image.Image], path: Path) -> None:
    # PNG-backed ICO entries preserve full alpha and let us embed handcrafted
    # 20/24 px variants rather than asking Windows to shrink a larger frame.
    payloads: list[bytes] = []
    for image in images:
        stream = io.BytesIO()
        image.save(stream, format="PNG", optimize=True)
        payloads.append(stream.getvalue())

    directory_size = 6 + 16 * len(images)
    entries = []
    offset = directory_size
    for image, payload in zip(images, payloads):
        width = 0 if image.width == 256 else image.width
        height = 0 if image.height == 256 else image.height
        entries.append(struct.pack("<BBBBHHII", width, height, 0, 0, 1, 32, len(payload), offset))
        offset += len(payload)

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(struct.pack("<HHH", 0, 1, len(images)) + b"".join(entries) + b"".join(payloads))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--app-master", required=True, type=Path)
    parser.add_argument("--mark-master", required=True, type=Path)
    parser.add_argument("--tray-master", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    source_dir = args.output / "Source"
    source_dir.mkdir(parents=True, exist_ok=True)
    stored_app_master = source_dir / "FolderGlimpse-App-Master.png"
    stored_mark_master = source_dir / "FolderGlimpse-Mark-Master.png"
    stored_tray_master = source_dir / "FolderGlimpse-Tray-Master.png"
    if args.app_master.resolve() != stored_app_master.resolve():
        shutil.copyfile(args.app_master, stored_app_master)
    if args.mark_master.resolve() != stored_mark_master.resolve():
        shutil.copyfile(args.mark_master, stored_mark_master)
    if args.tray_master.resolve() != stored_tray_master.resolve():
        shutil.copyfile(args.tray_master, stored_tray_master)

    app_master = normalize_master(args.app_master, 1024, padding_ratio=.035)
    mark_master = normalize_master(args.mark_master, 512, padding_ratio=.06)
    tray_master = normalize_master(args.tray_master, 256, padding_ratio=.025)
    write_png(mark_master, args.output / "FolderGlimpse-Mark-512.png")
    write_png(resize_icon(mark_master, 256), args.output / "FolderGlimpse-Mark-256.png")

    app_icons = [resize_icon(app_master, size) for size in APP_SIZES]
    for size, image in zip(APP_SIZES, app_icons):
        write_png(image, args.output / f"FolderGlimpse-App-{size}.png")
    write_ico(app_icons, args.output / "FolderGlimpse-App.ico")

    tray_icons = [resize_icon(tray_master, size) for size in TRAY_SIZES]
    for size, image in zip(TRAY_SIZES, tray_icons):
        write_png(image, args.output / f"FolderGlimpse-Tray-{size}.png")
    write_ico(tray_icons, args.output / "FolderGlimpse-Tray.ico")


if __name__ == "__main__":
    main()

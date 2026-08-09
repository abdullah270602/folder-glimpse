"""Generate FolderGlimpse PNG/ICO exports from the checked-in vector design language.

Each target size is drawn independently at 4x and downsampled once. Small targets use
thicker geometry and fewer details instead of shrinking the 256 px application artwork.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "src" / "FolderGlimpse" / "Assets" / "Branding"
AA = 4

def sc(value): return int(round(value * AA))
def box(values): return tuple(sc(v) for v in values)

def vertical_gradient(size, top, bottom, radius):
    canvas = Image.new("RGBA", (sc(size), sc(size)), (0, 0, 0, 0))
    pixels = canvas.load()
    a = tuple(int(top[i:i+2], 16) for i in (1, 3, 5))
    b = tuple(int(bottom[i:i+2], 16) for i in (1, 3, 5))
    for y in range(canvas.height):
        t = y / max(1, canvas.height - 1)
        color = tuple(round(a[c] * (1-t) + b[c] * t) for c in range(3)) + (255,)
        for x in range(canvas.width): pixels[x, y] = color
    mask = Image.new("L", canvas.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, canvas.width-1, canvas.height-1), sc(radius), fill=255)
    canvas.putalpha(mask)
    return canvas

def rounded(draw, values, radius, fill, outline=None, width=1):
    draw.rounded_rectangle(box(values), sc(radius), fill=fill, outline=outline, width=sc(width))

def masked_gradient(image, mask, top, bottom):
    layer = Image.new("RGBA", image.size, (0,0,0,0)); draw = ImageDraw.Draw(layer)
    a = tuple(int(top[i:i+2], 16) for i in (1,3,5)); b = tuple(int(bottom[i:i+2], 16) for i in (1,3,5))
    for y in range(layer.height):
        t = y / max(1, layer.height-1)
        color = tuple(round(a[c]*(1-t)+b[c]*t) for c in range(3)) + (255,)
        draw.line((0,y,layer.width,y), fill=color)
    layer.putalpha(mask)
    image.alpha_composite(layer)

def rotated_panel(image, size, xy, wh, angle, fill, lines=True):
    width, height = sc(wh[0]), sc(wh[1])
    panel = Image.new("RGBA", (width,height), (0,0,0,0)); draw = ImageDraw.Draw(panel)
    draw.rounded_rectangle((1,1,width-2,height-2), sc(size*.045), fill=fill)
    if lines:
        line_h = max(sc(size*.025),3); left=round(width*.23)
        draw.rounded_rectangle((left,round(height*.29),round(width*.79),round(height*.29)+line_h), line_h//2, fill="#2563EB")
        draw.rounded_rectangle((left,round(height*.51),round(width*.69),round(height*.51)+line_h), line_h//2, fill="#94A3B8")
        draw.rounded_rectangle((left,round(height*.69),round(width*.60),round(height*.69)+line_h), line_h//2, fill="#CBD5E1")
    rotated = panel.rotate(angle, resample=Image.Resampling.BICUBIC, expand=True)
    position = (sc(xy[0])-(rotated.width-width)//2, sc(xy[1])-(rotated.height-height)//2)
    shadow = Image.new("RGBA", image.size, (0,0,0,0)); shadow.alpha_composite(rotated, position)
    alpha = shadow.getchannel("A").filter(ImageFilter.GaussianBlur(sc(size*.018)))
    shadow_fill = Image.new("RGBA", image.size, (2,6,23,90)); shadow_fill.putalpha(alpha.point(lambda p: round(p*.38)))
    image.alpha_composite(shadow_fill, (0,sc(size*.018)))
    image.alpha_composite(rotated, position)

def app_icon(size):
    tiny = size <= 32
    image = Image.new("RGBA", (sc(size), sc(size)), (0, 0, 0, 0))
    margin = size * .045
    bg = vertical_gradient(size - margin*2, "#17243B", "#08111F", size * .20)
    image.alpha_composite(bg, (sc(margin), sc(margin)))
    draw = ImageDraw.Draw(image)
    rear = [(size*.20,size*.30),(size*.43,size*.30),(size*.49,size*.36),(size*.76,size*.36),(size*.80,size*.41),(size*.80,size*.64),(size*.20,size*.64)]
    if tiny:
        draw.polygon([(sc(x),sc(y)) for x,y in rear], fill="#60A5FA")
    else:
        rear_mask=Image.new("L",image.size,0); ImageDraw.Draw(rear_mask).polygon([(sc(x),sc(y)) for x,y in rear],fill=255)
        masked_gradient(image,rear_mask,"#BFDBFE","#60A5FA")
    main = [(size*.17,size*.42),(size*.43,size*.42),(size*.49,size*.47),(size*.81,size*.47),(size*.81,size*.78),(size*.17,size*.78)]
    if tiny:
        draw.polygon([(sc(x),sc(y)) for x,y in main], fill="#2563EB")
        rounded(draw, (size*.17,size*.46,size*.81,size*.79), size*.055, "#2563EB")
    else:
        main_mask=Image.new("L",image.size,0); md=ImageDraw.Draw(main_mask)
        md.polygon([(sc(x),sc(y)) for x,y in main],fill=255); md.rounded_rectangle(box((size*.17,size*.46,size*.81,size*.79)),sc(size*.055),fill=255)
        masked_gradient(image,main_mask,"#3B82F6","#1D4ED8")
        ImageDraw.Draw(image).polygon([(sc(size*.18),sc(size*.47)),(sc(size*.79),sc(size*.47)),(sc(size*.75),sc(size*.55)),(sc(size*.18),sc(size*.61))],fill=(96,165,250,52))
    px = size*(.52 if tiny else .54); py = size*(.49 if tiny else .47)
    pw = size*(.34 if tiny else .31); ph = size*(.34 if tiny else .37)
    if not tiny:
        rotated_panel(image,size,(size*.50,size*.49),(size*.29,size*.35),8,"#60A5FA",lines=False)
        rotated_panel(image,size,(px,py),(pw,ph),6,"#F8FAFC",lines=True)
        return image.resize((size,size), Image.Resampling.LANCZOS)
    shadow = Image.new("RGBA", image.size, (0,0,0,0)); sd = ImageDraw.Draw(shadow)
    rounded(sd, (px+size*.014,py+size*.02,px+pw+size*.014,py+ph+size*.02), size*.045, (2,6,23,90))
    shadow = shadow.filter(ImageFilter.GaussianBlur(sc(size*.018 if not tiny else .008)))
    image.alpha_composite(shadow)
    draw = ImageDraw.Draw(image)
    rounded(draw, (px,py,px+pw,py+ph), size*.045, "#F8FAFC")
    line_h = max(size*.025, .8)
    rounded(draw, (px+pw*.23,py+ph*.30,px+pw*.78,py+ph*.30+line_h), line_h/2, "#2563EB")
    rounded(draw, (px+pw*.23,py+ph*.52,px+pw*.68,py+ph*.52+line_h), line_h/2, "#94A3B8")
    if size >= 24:
        rounded(draw, (px+pw*.23,py+ph*.70,px+pw*.59,py+ph*.70+line_h), line_h/2, "#CBD5E1")
    return image.resize((size,size), Image.Resampling.LANCZOS)

def tray_icon(size):
    image = Image.new("RGBA", (sc(size),sc(size)), (0,0,0,0)); draw = ImageDraw.Draw(image)
    stroke = max(1.7, size*.085)
    pts = [(size*.10,size*.31),(size*.10,size*.72),(size*.15,size*.78),(size*.69,size*.78),(size*.76,size*.72),(size*.76,size*.40),(size*.70,size*.34),(size*.43,size*.34),(size*.34,size*.23),(size*.16,size*.23),(size*.10,size*.31)]
    draw.line([(sc(x),sc(y)) for x,y in pts], fill="#2563EB", width=sc(stroke), joint="curve")
    dx1,dy1,dx2,dy2 = size*.48,size*.46,size*.91,size*.91
    rounded(draw, (dx1,dy1,dx2,dy2), size*.055, "#F8FAFC", "#334155", max(1.4,size*.065))
    line = max(1.3,size*.06)
    draw.line(box((size*.61,size*.62,size*.80,size*.62)), fill="#2563EB", width=sc(line))
    draw.line(box((size*.61,size*.74,size*.77,size*.74)), fill="#94A3B8", width=sc(line))
    return image.resize((size,size), Image.Resampling.LANCZOS)

def save_ico(path, frames):
    largest = max(frames, key=lambda im: im.width)
    others = [frame for frame in frames if frame is not largest]
    largest.save(path, format="ICO", sizes=[frame.size for frame in frames], append_images=others)

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    app_sizes = [16,20,24,32,48,64,128,256]
    tray_sizes = [16,20,24,32]
    app_frames = [app_icon(size) for size in app_sizes]
    tray_frames = [tray_icon(size) for size in tray_sizes]
    for size, frame in zip(app_sizes, app_frames): frame.save(OUT / f"FolderGlimpse-App-{size}.png")
    for size, frame in zip(tray_sizes, tray_frames): frame.save(OUT / f"FolderGlimpse-Tray-{size}.png")
    save_ico(OUT / "FolderGlimpse-App.ico", app_frames)
    save_ico(OUT / "FolderGlimpse-Tray.ico", tray_frames)
    print(f"Generated {len(app_frames)} app and {len(tray_frames)} tray frames in {OUT}")

if __name__ == "__main__": main()

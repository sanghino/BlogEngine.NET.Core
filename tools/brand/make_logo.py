"""Logo ExtraGeek-BlogEngine: marchio ">_" + scritta in Cascadia Mono (OFL) convertita in tracciati.

Uso: pip install fonttools, poi
    python tools/brand/make_logo.py src/BlogEngine.Web/wwwroot/Content/images/brand
Crea logo.svg (sfondi chiari), logo-dark.svg (sfondi scuri) e icon.svg. Le icone PNG e favicon.ico
si ottengono da icon.svg (16, 32 e 48 px nel file .ico).
"""
import os, sys
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen

OUT = sys.argv[1]
FONT = r"C:\Windows\Fonts\CascadiaMono.ttf"

def instance(weight):
    return instantiateVariableFont(TTFont(FONT), {"wght": weight})

def text_path(font, text, size, x, baseline, tracking=0.0):
    """Restituisce (d, larghezza) del testo come tracciato SVG."""
    gs, cmap, hmtx = font.getGlyphSet(), font.getBestCmap(), font["hmtx"]
    scale = size / font["head"].unitsPerEm
    pen = SVGPathPen(gs)
    cursor = x
    for ch in text:
        name = cmap[ord(ch)]
        tp = TransformPen(pen, (scale, 0, 0, -scale, cursor, baseline))
        gs[name].draw(tp)
        cursor += hmtx[name][0] * scale + tracking
    return pen.getCommands(), cursor - x - tracking

bold, regular = instance(700), instance(400)

GREEN = "#1cac7b"       # verde dell'amministrazione
GLOW = "#3ddc97"        # verde del prompt
INK = "#1d2129"

def mark(x=0, y=0, s=64, outline=False):
    """Riquadro con il prompt >_ (coordinate su base 64)."""
    k = s / 64
    outline_rect = f'\n    <rect x="1" y="1" width="62" height="62" rx="13" fill="none" stroke="{GLOW}" stroke-opacity="0.45" stroke-width="2"/>'
    return f'''<g transform="translate({x} {y}) scale({k})">
    <rect width="64" height="64" rx="14" fill="{INK}"/>{outline_rect if outline else ""}
    <path d="M15 20 L29 32 L15 44" fill="none" stroke="{GLOW}" stroke-width="6.5" stroke-linecap="round" stroke-linejoin="round"/>
    <rect x="33" y="40.5" width="17" height="6.5" rx="3.25" fill="{GLOW}"/>
  </g>'''

def wordmark(dark_bg):
    size, base, x0 = 30, 43, 80
    p1, w1 = text_path(bold, "ExtraGeek", size, x0, base)
    p2, w2 = text_path(regular, "-", size, x0 + w1, base)
    p3, w3 = text_path(regular, "BlogEngine", size, x0 + w1 + w2, base)
    width = x0 + w1 + w2 + w3 + 2
    c1 = "#ffffff" if dark_bg else INK
    c2 = "#8a93a3" if dark_bg else "#9aa1ac"
    c3 = GLOW if dark_bg else GREEN
    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width:.0f} 64" width="{width:.0f}" height="64" role="img" aria-label="ExtraGeek-BlogEngine">
  <title>ExtraGeek-BlogEngine</title>
  {mark(outline=dark_bg)}
  <path fill="{c1}" d="{p1}"/>
  <path fill="{c2}" d="{p2}"/>
  <path fill="{c3}" d="{p3}"/>
</svg>
'''
    return svg

os.makedirs(OUT, exist_ok=True)
open(os.path.join(OUT, "logo.svg"), "w", encoding="utf-8").write(wordmark(False))
open(os.path.join(OUT, "logo-dark.svg"), "w", encoding="utf-8").write(wordmark(True))
open(os.path.join(OUT, "icon.svg"), "w", encoding="utf-8").write(
    f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="64" height="64" role="img" aria-label="ExtraGeek-BlogEngine">\n  <title>ExtraGeek-BlogEngine</title>\n  {mark()}\n</svg>\n')
print("ok", os.listdir(OUT))

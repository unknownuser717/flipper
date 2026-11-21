# Génère les textures du flipper (répétables sans raccord) dans Assets/Textures.
# Usage : python generer_textures.py <dossier_sortie>
import sys, os
import numpy as np
from PIL import Image

N = 1024
sortie = sys.argv[1]
os.makedirs(sortie, exist_ok=True)
rng = np.random.default_rng(1717)

y, x = np.mgrid[0:N, 0:N].astype(np.float32) / N   # coordonnées 0..1 (y = ligne)


def bruit(fmin, fmax, nb, rng, etirement=(1, 1)):
    """Bruit répétable : somme de cosinus à fréquences entières (donc périodique sur la tuile)."""
    r = np.zeros((N, N), np.float32)
    for _ in range(nb):
        f = rng.uniform(fmin, fmax)
        ang = rng.uniform(0, 2 * np.pi)
        kx = int(round(np.cos(ang) * f * etirement[0]))
        ky = int(round(np.sin(ang) * f * etirement[1]))
        if kx == 0 and ky == 0:
            continue
        amp = 1.0 / max(1.0, np.hypot(kx, ky)) ** 0.8
        r += amp * np.cos(2 * np.pi * (kx * x + ky * y) + rng.uniform(0, 2 * np.pi))
    r -= r.min()
    return r / max(r.max(), 1e-6)


def normal_map(h, force):
    """Normal map (convention OpenGL / Unity, Y vers le haut) depuis une carte de hauteur répétable."""
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    nx, ny, nz = -dx * force, dy * force, np.ones_like(h)
    l = np.sqrt(nx ** 2 + ny ** 2 + nz ** 2)
    return np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5


def enregistrer(nom, rgb):
    Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8)).save(os.path.join(sortie, nom))
    print("écrit", nom)


# ------------------------------------------------------------------ Bois carbonisé (murs)
PLANCHES = 4
indice = np.floor(y * PLANCHES).astype(int)                 # numéro de planche
v_local = y * PLANCHES - indice                              # 0..1 dans la planche
decalage = rng.uniform(0, 1, PLANCHES)[indice]
deform = bruit(1, 6, 40, rng) * 0.35
veinage = (0.5 + 0.5 * np.sin(2 * np.pi * (v_local * 9 + deform * 4 + decalage * 5))) ** 2.5
fibres = bruit(30, 120, 120, rng, etirement=(0.15, 1))       # fibres fines dans le sens des planches
taches = bruit(2, 10, 60, rng)

# joints entre planches (horizontaux) + joints de bout (verticaux, un par planche, position aléatoire)
dist_joint = np.minimum(v_local, 1 - v_local) * (N / PLANCHES)          # en pixels
pos_bout = rng.uniform(0, 1, PLANCHES)[indice]
dist_bout = np.abs(((x - pos_bout + 0.5) % 1.0) - 0.5) * N
joint = np.clip(1 - np.minimum(dist_joint, dist_bout) / 4.0, 0, 1)    # 1 dans le joint

# fissures de braise : lignes fines là où un bruit passe par 0.5, plus visibles près des joints
fissure_b = bruit(3, 14, 70, rng)
fissures = np.clip(1 - np.abs(fissure_b - 0.5) / 0.006, 0, 1) * np.clip((taches - 0.62) * 8, 0, 1)
pres_joint = np.clip(1 - np.minimum(dist_joint, dist_bout) / 7.0, 0, 1) ** 1.5
lueur = np.clip((bruit(4, 24, 60, rng) - 0.45) * 3, 0, 1)
braise = np.clip(np.maximum(fissures, pres_joint * lueur), 0, 1)
braise *= 0.6 + 0.4 * bruit(8, 30, 50, rng)

charbon = np.array([0.055, 0.035, 0.028])
bois = np.array([0.20, 0.105, 0.06])
t = np.clip(veinage * 0.6 + fibres * 0.45 + taches * 0.25 - 0.3, 0, 1)[..., None]
albedo = charbon + (bois - charbon) * t
albedo *= (1 - 0.85 * joint)[..., None]
orange = np.array([1.0, 0.36, 0.06])
albedo = albedo * (1 - braise[..., None] * 0.7) + orange * braise[..., None] * 0.7
enregistrer("BoisBrule_Albedo.png", albedo)

emission = orange * (braise ** 1.3)[..., None] + np.array([1.0, 0.75, 0.3]) * (fissures ** 3)[..., None] * 0.6
enregistrer("BoisBrule_Emission.png", emission)

hauteur = veinage * 0.25 + fibres * 0.35 - joint * 1.2 - fissures * 0.6
enregistrer("BoisBrule_Normal.png", normal_map(hauteur, 6.0))

# ------------------------------------------------------------------ Métal brossé sombre (rails, pieds)
brossage = bruit(40, 260, 220, rng, etirement=(0.03, 1))     # stries horizontales
rayures = np.clip(1 - np.abs(bruit(4, 20, 40, rng) - 0.5) / 0.004, 0, 1) * 0.5
nuances = bruit(1, 5, 30, rng)
acier = np.array([0.30, 0.30, 0.32])
chauffe = np.array([0.36, 0.26, 0.20])                         # léger reflet bronze (chaleur)
base = acier + (chauffe - acier) * (nuances ** 2)[..., None] * 0.6
metal = base * (0.78 + 0.3 * brossage)[..., None] + rayures[..., None] * 0.25
enregistrer("MetalBrosse_Albedo.png", metal)
enregistrer("MetalBrosse_Normal.png", normal_map(brossage * 0.4 - rayures, 3.0))

# ------------------------------------------------------------------ Pastille de bonus (rollover)
T = 256
yy, xx = np.mgrid[0:T, 0:T].astype(np.float32)
cx = cy = (T - 1) / 2
r = np.hypot(xx - cx, yy - cy) / (T / 2)
a = np.arctan2(yy - cy, xx - cx)
# étoile à 5 branches
rayon_etoile = 0.30 + 0.25 * (0.5 + 0.5 * np.cos(5 * (a + np.pi / 2)))
etoile = np.clip((rayon_etoile - r) * 40, 0, 1)
anneau = np.clip(1 - np.abs(r - 0.86) / 0.06, 0, 1)
fond = np.clip((1 - r) * 30, 0, 1)
pastille = np.clip(0.12 * fond + 0.9 * etoile + 0.8 * anneau, 0, 1)
img = np.stack([pastille] * 3, -1)
Image.fromarray((img * 255).astype(np.uint8)).save(os.path.join(sortie, "Bonus_Pastille.png"))
print("écrit Bonus_Pastille.png")


# ------------------------------------------------------------------ Inserts (style "lentilles" de flipper)
# Formes découpées (canal alpha) : cadre sombre + verre bombé avec reflet. La couleur vient du matériau.
from PIL import ImageDraw, ImageFont, ImageFilter

def police(taille):
    for nom in ("arialbd.ttf", "segoeuib.ttf", "DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(nom, taille)
        except OSError:
            pass
    return ImageFont.load_default()

def verre(masque_int, w, h, cx, cy):
    """Dégradé de verre bombé (clair au centre) + reflet en haut à gauche, dans le masque intérieur."""
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    d = np.hypot((xx - cx) / (w * 0.5), (yy - cy) / (h * 0.5))
    base = np.clip(1.0 - 0.45 * d, 0.45, 1.0)
    reflet = np.exp(-(((xx - w * 0.33) / (w * 0.13)) ** 2 + ((yy - h * 0.25) / (h * 0.07)) ** 2)) * 0.6
    return base, reflet

def insert(nom, w, h, dessiner_forme, zone_pointe=None, texte=None):
    S = 4  # sur-échantillonnage pour des bords propres
    m = Image.new("L", (w * S, h * S), 0)
    dessiner_forme(ImageDraw.Draw(m), w * S, h * S)
    masque = np.asarray(m.resize((w, h), Image.LANCZOS)).astype(np.float32) / 255
    interieur = np.asarray(m.filter(ImageFilter.MinFilter(4 * S + 1)).resize((w, h), Image.LANCZOS)).astype(np.float32) / 255

    base, reflet = verre(interieur, w, h, w / 2, h * 0.55)
    rgb = np.stack([base] * 3, -1)
    if zone_pointe is not None:                      # pointe colorée (rouge chaud)
        pointe = zone_pointe(w, h)[..., None]
        rgb = rgb * (1 - pointe) + np.array([1.0, 0.25, 0.12]) * base[..., None] * pointe
    rgb = rgb * interieur[..., None] + np.array([0.18, 0.17, 0.16]) * (1 - interieur[..., None])  # cadre
    rgb = np.clip(rgb + (reflet * interieur)[..., None], 0, 1)

    img = Image.fromarray((rgb * 255).astype(np.uint8))
    if texte:
        d = ImageDraw.Draw(img)
        f = police(int(h * 0.17))
        bb = d.textbbox((0, 0), texte, font=f)
        d.text(((w - (bb[2] - bb[0])) / 2 - bb[0], (h - (bb[3] - bb[1])) / 2 - bb[1]), texte, font=f, fill=(30, 22, 18))
    img.putalpha(Image.fromarray((masque * 255).astype(np.uint8)))
    img.save(os.path.join(sortie, nom))
    print("écrit", nom)

def forme_fleche(d, W, H):
    r = W * 0.46
    d.ellipse([W / 2 - r, H - 2 * r - W * 0.04, W / 2 + r, H - W * 0.04], fill=255)        # bas arrondi
    d.rectangle([W / 2 - r, H * 0.36, W / 2 + r, H - r - W * 0.04], fill=255)             # corps
    d.polygon([(W / 2 - r, H * 0.36), (W / 2 + r, H * 0.36), (W / 2, H * 0.015)], fill=255)  # pointe

def zone_pointe_fleche(w, h):
    yy = np.mgrid[0:h, 0:w][0].astype(np.float32)
    return np.clip((h * 0.33 - yy) / (h * 0.04), 0, 1)

def forme_rond(d, W, H):
    d.ellipse([W * 0.03, H * 0.03, W * 0.97, H * 0.97], fill=255)

insert("Insert_Fleche.png", 256, 512, forme_fleche, zone_pointe=zone_pointe_fleche)
insert("Insert_Rond.png", 256, 256, forme_rond, texte="BONUS")

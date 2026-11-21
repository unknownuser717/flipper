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

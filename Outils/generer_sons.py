# Génère les sons du flipper (WAV 44,1 kHz, 16 bits, mono) par synthèse : libres de droits.
# Usage : python generer_sons.py <dossier_sortie>
import os
import sys
import wave

import numpy as np

SR = 44100
sortie = sys.argv[1]
os.makedirs(sortie, exist_ok=True)
rng = np.random.default_rng(42)


def temps(duree):
    return np.arange(int(SR * duree)) / SR


def decroissance(t, vitesse, attaque=0.002):
    """Enveloppe : montée très courte puis décroissance exponentielle."""
    return np.clip(t / attaque, 0, 1) * np.exp(-t * vitesse)


def ton(frequence_t, t):
    """Sinusoïde dont la fréquence varie dans le temps (phase intégrée)."""
    phase = 2 * np.pi * np.cumsum(frequence_t) / SR
    return np.sin(phase)


def passe_bas(x, coupure):
    """Filtre passe-bas d'ordre 1 (coupure fixe ou variable dans le temps)."""
    coupure = np.broadcast_to(coupure, x.shape)
    a = 1 - np.exp(-2 * np.pi * coupure / SR)
    y = np.zeros_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc += a[i] * (x[i] - acc)
        y[i] = acc
    return y


def bruit(n):
    return rng.uniform(-1, 1, n)


def enregistrer(nom, x, volume=0.9):
    x = x - np.mean(x)
    x = x / (np.max(np.abs(x)) + 1e-9) * volume
    fondu = min(len(x) // 10, int(SR * 0.01))
    x[-fondu:] *= np.linspace(1, 0, fondu)              # pas de "clic" à la fin
    donnees = (x * 32767).astype(np.int16)
    with wave.open(os.path.join(sortie, nom), "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(SR)
        f.writeframes(donnees.tobytes())
    print("écrit", nom)


def ding(t, f0, vitesse=6.0):
    """Cloche : partiels inharmoniques qui s'éteignent plus vite quand ils sont aigus."""
    s = np.zeros_like(t)
    for rapport, amp, dec in ((1.0, 1.0, 1.0), (2.76, 0.4, 1.7), (5.4, 0.2, 2.7), (8.9, 0.08, 4.0)):
        s += amp * np.sin(2 * np.pi * f0 * rapport * t) * np.exp(-t * vitesse * dec)
    return s * np.clip(t / 0.001, 0, 1)


# --- Ressort du lanceur : "boing" qui vibre + petit claquement métallique
t = temps(0.7)
f = 150 * (1 + 0.35 * np.exp(-t * 4) * np.sin(2 * np.pi * 9 * t)) + 60 * np.exp(-t * 6)
boing = (ton(f, t) + 0.3 * ton(2 * f, t)) * np.exp(-t * 3.5)
metal = sum(a * np.sin(2 * np.pi * fr * t) for fr, a in ((930, 0.3), (1370, 0.2), (2110, 0.12))) * np.exp(-t * 18)
clic = bruit(len(t)) * np.exp(-t * 400)
enregistrer("Ressort_Boing.wav", boing + metal + 0.5 * clic)

# --- Flipper : clac mécanique (bruit court + coup sourd)
t = temps(0.15)
coup = np.sin(2 * np.pi * 70 * t) * decroissance(t, 40)
claquement = np.diff(bruit(len(t) + 1)) * np.exp(-t * 120)
enregistrer("Flipper_Clac.wav", coup + 0.6 * claquement, 0.8)

# --- Bumper : "pop" percutant (coup qui descend + clic brillant + résonance)
t = temps(0.3)
f = 90 + 130 * np.exp(-t * 60)
pop = ton(f, t) * decroissance(t, 25)
eclat = np.diff(bruit(len(t) + 1)) * np.exp(-t * 300)
resonance = (np.sin(2 * np.pi * 1800 * t) + 0.6 * np.sin(2 * np.pi * 2620 * t)) * np.exp(-t * 25) * 0.25
enregistrer("Bumper_Pop.wav", pop + 0.5 * eclat + resonance)

# --- Arbre en feu : "fwoom" de flamme + crépitements
t = temps(0.55)
souffle = passe_bas(bruit(len(t)), 600 + 1500 * np.exp(-t * 6)) * decroissance(t, 6, 0.02) * 3
crepit = np.zeros_like(t)
for debut in np.sort(rng.uniform(0, 0.45, 26)):
    i = int(debut * SR)
    n = int(SR * 0.006)
    crepit[i:i + n] += bruit(n) * np.exp(-np.arange(n) / (SR * 0.0015)) * rng.uniform(0.4, 1) * np.exp(-debut * 3)
coup = np.sin(2 * np.pi * 80 * t) * decroissance(t, 30)
enregistrer("Arbre_Feu.wav", souffle + 0.9 * crepit + 0.6 * coup)

# --- Rail : whoosh qui monte (bruit filtré dont le filtre s'ouvre + ton qui monte)
t = temps(0.65)
enveloppe = np.sin(np.pi * np.clip(t / 0.65, 0, 1)) ** 1.5
whoosh = passe_bas(bruit(len(t)), 300 + 4000 * (t / 0.65) ** 1.5) * 4
monte = ton(250 + 700 * (t / 0.65) ** 2, t) * 0.25
enregistrer("Rail_Boost.wav", (whoosh + monte) * enveloppe)

# --- Insert : "ding" de cloche
t = temps(0.6)
enregistrer("Insert_Ding.wav", ding(t, 1320), 0.7)

# --- Bonus : arpège montant (do mi sol do)
t = temps(1.1)
jingle = np.zeros_like(t)
for k, f0 in enumerate((1047, 1319, 1568, 2093)):
    decal = int(SR * 0.09 * k)
    jingle[decal:] += ding(t[: len(t) - decal], f0, 4.5) * (0.8 + 0.1 * k)
enregistrer("Bonus_Jingle.wav", jingle)

# --- Bille perdue : "wah-wah" qui descend
t = temps(0.9)
f = 440 * 2 ** (-2 * t / 0.9) * (1 + 0.03 * np.sin(2 * np.pi * 6 * t))
wah = sum(np.sin(2 * np.pi * np.cumsum(f * h) / SR) / h for h in (1, 2, 3, 4))
enregistrer("Bille_Perdue.wav", wah * np.clip(t / 0.02, 0, 1) * np.exp(-t * 1.5), 0.8)

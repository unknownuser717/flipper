# Changes

## New
- **Assets/Script/Flipper.cs**: flipper controller (kinematic Rigidbody, rotates around the table normal).
  - Left: Left Shift / Left Arrow / A / Q / gamepad left shoulder or trigger
  - Right: Right Shift / Right Arrow / D / gamepad right shoulder or trigger
  - To use your arcade buttons, add bindings to the "Bouton" field in the Inspector
    (adding any binding replaces the defaults, so add the keyboard keys too if you want them).
- **Scene objects `flipper_gauche` / `flipper_droite`**: the pivot is the parent object, `batte` is the visual + collider.
  `rabatoire_bas_gauche/droite` were shortened so they stop at the pivot and act as the inlanes.
- **Assets/Script/Bille.cs**: identifies the ball by the new "Ball" tag (falls back to the name "la_bille").

## Fixed
- Script_ressort.cs: the plunger could be relaunched while still moving (`||`/`&&` precedence), and crashed with no keyboard.
- BumperEffect.cs / RetourDepart.cs: use the Ball tag; the drain now also resets the Rigidbody position.
- Ball Rigidbody: Interpolate on (smoother movement).
- PC_RPAsset: GPU Resident Drawer disabled (cause of the Burst `AllocateBinsPerBatch` error).
- Fixed Timestep 0.02 -> 0.01 (fast flippers hit the ball more reliably).

## Still to do on your side
- .fbx / .jpg / .png / .mp3 files in this zip are Git LFS placeholders, not the real files.
  Get the real ones by cloning the repo with Git LFS installed.

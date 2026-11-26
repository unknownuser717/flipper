using UnityEngine;
using UnityEngine.InputSystem;

// Deux vues de caméra : "joueur" (devant la machine, on voit la table et le backglass avec le score)
// et "dessus" (la vue d'origine, au-dessus de la table). Touche C (ou bouton nord de la manette) pour changer.
// Les deux vues sont enregistrées par le menu Flipper > Caméras (joueur / dessus).
[RequireComponent(typeof(Camera))]
public class CameraFlipper : MonoBehaviour
{
    [System.Serializable]
    public struct Vue
    {
        public Vector3 position;
        public Quaternion rotation;
        public float champ;   // field of view
    }

    public Vue vueJoueur;
    public Vue vueDessus;
    [Tooltip("Vue au lancement : true = joueur, false = dessus.")]
    public bool commencerEnVueJoueur = true;
    [Tooltip("Durée du passage d'une vue à l'autre (secondes).")]
    public float dureeTransition = 0.6f;

    /// <summary>La caméra est-elle en vue joueur (backglass visible) ?</summary>
    public static bool VueJoueur { get; private set; }

    Camera cam;
    float t;   // 0 = dessus, 1 = joueur

    void Awake()
    {
        cam = GetComponent<Camera>();
        VueJoueur = commencerEnVueJoueur;
        t = VueJoueur ? 1f : 0f;
        Appliquer(t);
    }

    void Update()
    {
        var k = Keyboard.current;
        var g = Gamepad.current;
        if ((k != null && k.cKey.wasPressedThisFrame) || (g != null && g.buttonNorth.wasPressedThisFrame))
            VueJoueur = !VueJoueur;

        float cible = VueJoueur ? 1f : 0f;
        if (Mathf.Approximately(t, cible)) return;
        t = Mathf.MoveTowards(t, cible, Time.unscaledDeltaTime / Mathf.Max(0.01f, dureeTransition));
        Appliquer(Mathf.SmoothStep(0f, 1f, t));
    }

    void Appliquer(float k)
    {
        transform.SetPositionAndRotation(Vector3.Lerp(vueDessus.position, vueJoueur.position, k),
                                         Quaternion.Slerp(vueDessus.rotation, vueJoueur.rotation, k));
        cam.fieldOfView = Mathf.Lerp(vueDessus.champ, vueJoueur.champ, k);
    }
}

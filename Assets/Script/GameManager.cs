using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

// Gère le score, les billes restantes et la remise en jeu de la bille.
// Rien à configurer : il se crée tout seul au lancement du jeu s'il n'est pas dans la scène.
// (Vous pouvez aussi l'ajouter sur un objet vide pour régler les valeurs dans l'Inspector.)
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Partie")]
    [Tooltip("Nombre de billes par partie.")]
    public int billesParPartie = 3;
    [Tooltip("Temps (secondes) avant que la bille revienne au lanceur.")]
    public float delaiRemiseEnJeu = 1f;

    [Header("Bille")]
    [Tooltip("Laisser vide : trouve la bille par son tag \"Ball\" ou son nom \"la_bille\".")]
    public Rigidbody bille;
    [Tooltip("Laisser vide : la bille revient là où elle est au début du jeu (dans le couloir du lanceur).")]
    public Transform pointDeDepart;
    [Tooltip("Si la bille tombe plus bas que (départ - cette valeur), elle est perdue (tombée de la table ou dans la caisse).")]
    public float hauteurPerte = 3f;
    [Tooltip("Si la bille s'éloigne plus que cette distance de son point de départ, elle est perdue (sortie de table).")]
    public float distanceMaxTable = 60f;

    [Header("Affichage")]
    public bool afficherScore = true;
    public int tailleTexte = 36;

    public int Score { get; private set; }
    public int BilleActuelle { get; private set; } = 1;
    public int Record { get; private set; }
    public bool PartieTerminee { get; private set; }
    // Numéro de frame où la partie a été relancée (le lanceur ignore cet appui-là)
    public int FrameRejouer { get; private set; } = -1;

    Vector3 positionDepart;
    Quaternion rotationDepart;
    bool enRemiseEnJeu;
    Renderer[] rendusBille;
    GUIStyle style, styleGrand;
    Pinball_input controles;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreerSiAbsent()
    {
        if (FindAnyObjectByType<GameManager>() == null)
            new GameObject("GameManager").AddComponent<GameManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Record = PlayerPrefs.GetInt("Record", 0);
        controles = new Pinball_input();
    }

    void OnEnable()  { controles?.Enable(); }
    void OnDisable() { controles?.Disable(); }

    void OnDestroy()
    {
        controles?.Dispose();
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (bille == null)
        {
            GameObject go = null;
            try { go = GameObject.FindWithTag("Ball"); } catch (UnityException) { }
            if (go == null) go = GameObject.Find("la_bille");
            if (go != null) bille = go.GetComponent<Rigidbody>();
        }
        if (bille == null)
        {
            Debug.LogError("GameManager : bille introuvable (tag \"Ball\" ou nom \"la_bille\").", this);
            enabled = false;
            return;
        }

        rendusBille = bille.GetComponentsInChildren<Renderer>();
        positionDepart = pointDeDepart != null ? pointDeDepart.position : bille.position;
        rotationDepart = bille.rotation;
    }

    void Update()
    {
        if (bille == null) return;

        // Filet de sécurité : la bille est sortie de la table
        if (!enRemiseEnJeu && !PartieTerminee && !bille.isKinematic &&
            (bille.position.y < positionDepart.y - hauteurPerte ||
             Vector3.Distance(bille.position, positionDepart) > distanceMaxTable))
            BillePerdue();

        // Partie terminée : Espace / Start / bouton sud de la manette pour rejouer
        if (PartieTerminee && AppuiRejouer())
            NouvellePartie();
    }

    // ---------------------------------------------------------------- API

    public void AjouterPoints(int points)
    {
        if (PartieTerminee || enRemiseEnJeu) return;
        Score += points;
        if (Score > Record)
        {
            Record = Score;
            PlayerPrefs.SetInt("Record", Record);
        }
    }

    public void BillePerdue()
    {
        if (enRemiseEnJeu || PartieTerminee) return;
        StartCoroutine(RemiseEnJeu());
    }

    public void NouvellePartie()
    {
        PlayerPrefs.Save();
        FrameRejouer = Time.frameCount;
        Score = 0;
        BilleActuelle = 1;
        PartieTerminee = false;
        PlacerBille();
    }

    // ---------------------------------------------------------------- interne

    IEnumerator RemiseEnJeu()
    {
        enRemiseEnJeu = true;
        Cacher(true);
        yield return new WaitForSeconds(delaiRemiseEnJeu);

        if (BilleActuelle >= billesParPartie)
        {
            PartieTerminee = true;
            PlayerPrefs.Save();
            PlacerBille();          // la bille attend dans le lanceur
        }
        else
        {
            BilleActuelle++;
            PlacerBille();
        }
        enRemiseEnJeu = false;
    }

    void PlacerBille()
    {
        bille.isKinematic = false;
        bille.linearVelocity = Vector3.zero;
        bille.angularVelocity = Vector3.zero;
        bille.position = positionDepart;
        bille.rotation = rotationDepart;
        bille.transform.SetPositionAndRotation(positionDepart, rotationDepart);

        var trail = bille.GetComponent<TrailRenderer>();
        if (trail != null) trail.Clear();

        Cacher(false);
    }

    void Cacher(bool cacher)
    {
        if (cacher)
        {
            bille.linearVelocity = Vector3.zero;
            bille.angularVelocity = Vector3.zero;
            bille.isKinematic = true;   // ne bouge plus pendant l'attente
        }
        foreach (var r in rendusBille)
            if (r != null) r.enabled = !cacher;
    }

    bool AppuiRejouer()
    {
        var k = Keyboard.current;
        var g = Gamepad.current;
        return (controles != null && controles.inputs.start.WasPressedThisFrame())
            || (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame))
            || (g != null && (g.startButton.wasPressedThisFrame || g.buttonSouth.wasPressedThisFrame));
    }

    void OnGUI()
    {
        if (!afficherScore) return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = tailleTexte, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.white;
            styleGrand = new GUIStyle(style) { fontSize = tailleTexte * 2, alignment = TextAnchor.MiddleCenter };
        }

        Ombre(new Rect(20, 15, 600, 60), $"SCORE  {Score:N0}", style);
        Ombre(new Rect(20, 15 + tailleTexte * 1.3f, 600, 60), $"BALLE  {Mathf.Min(BilleActuelle, billesParPartie)} / {billesParPartie}", style);
        Ombre(new Rect(20, 15 + tailleTexte * 2.6f, 600, 60), $"RECORD  {Record:N0}", style);

        if (PartieTerminee)
        {
            var r = new Rect(0, Screen.height / 2f - tailleTexte * 2, Screen.width, tailleTexte * 2.5f);
            Ombre(r, "GAME OVER", styleGrand);
            r.y += tailleTexte * 2.5f;
            var petit = new GUIStyle(styleGrand) { fontSize = tailleTexte };
            Ombre(r, "Appuyez sur ESPACE pour rejouer", petit);
        }
    }

    static void Ombre(Rect r, string texte, GUIStyle s)
    {
        var c = s.normal.textColor;
        s.normal.textColor = new Color(0, 0, 0, 0.7f);
        GUI.Label(new Rect(r.x + 3, r.y + 3, r.width, r.height), texte, s);
        s.normal.textColor = c;
        GUI.Label(r, texte, s);
    }
}

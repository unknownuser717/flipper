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
    [Tooltip("Vitesse maximale de la bille (m/s). Empêche un choc trop violent de l'envoyer hors de la machine.")]
    public float vitesseMaxBille = 25f;

    [Header("Affichage")]
    public bool afficherScore = true;
    [Tooltip("Taille du texte en fraction de la hauteur de l'écran (0,022 = 24 px en 1080p).")]
    [Range(0.012f, 0.05f)]
    public float tailleRelative = 0.022f;

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
    GUIStyle style, styleGrand, styleMessage, styleSousTitre;
    int taillePolice;
    string message;
    float finMessage;
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

    void FixedUpdate()
    {
        // Limite de vitesse : la bille ne peut pas être projetée hors de la machine
        if (bille == null || bille.isKinematic) return;
        if (bille.linearVelocity.sqrMagnitude > vitesseMaxBille * vitesseMaxBille)
            bille.linearVelocity = bille.linearVelocity.normalized * vitesseMaxBille;
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
        foreach (var groupe in FindObjectsByType<GroupeBonus>())
            groupe.Reinitialiser();
        PlacerBille();
    }

    // Message temporaire au centre en haut de l'écran (BOOST, BONUS...)
    public void AfficherMessage(string texte, float duree = 1.5f)
    {
        message = texte;
        finMessage = Time.time + duree;
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

        // Taille du texte proportionnelle à la hauteur de l'écran (pareil en 720p, 1080p, 4K...)
        int taille = Mathf.Max(12, Mathf.RoundToInt(Screen.height * tailleRelative));
        if (style == null || taillePolice != taille)
            CreerStyles(taille);

        // Bandeau compact sur une seule ligne, en haut à gauche
        string infos = $"SCORE <b>{Score:N0}</b>     BALLE <b>{Mathf.Min(BilleActuelle, billesParPartie)}/{billesParPartie}</b>     RECORD <b>{Record:N0}</b>";
        Vector2 dim = style.CalcSize(new GUIContent(infos));
        float marge = taille * 0.5f;
        var fond = new Rect(taille * 0.5f, taille * 0.4f, dim.x + marge * 2f, dim.y + marge * 0.6f);
        GUI.color = new Color(0f, 0f, 0f, 0.45f);
        GUI.DrawTexture(fond, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(fond.x + marge, fond.y + marge * 0.3f, dim.x, dim.y), infos, style);

        // Message temporaire (BOOST, BONUS...)
        if (!string.IsNullOrEmpty(message) && Time.time < finMessage)
            Ombre(new Rect(0, Screen.height * 0.14f, Screen.width, taille * 2.5f), message, styleMessage);

        if (PartieTerminee)
        {
            var r = new Rect(0, Screen.height * 0.5f - taille * 3f, Screen.width, taille * 3.5f);
            Ombre(r, "GAME OVER", styleGrand);
            r.y += taille * 3.2f;
            r.height = taille * 2f;
            Ombre(r, "Appuyez sur ESPACE pour rejouer", styleSousTitre);
        }
    }

    void CreerStyles(int taille)
    {
        taillePolice = taille;
        style = new GUIStyle(GUI.skin.label) { fontSize = taille, richText = true, wordWrap = false };
        style.normal.textColor = new Color(1f, 1f, 1f, 0.92f);
        styleMessage = new GUIStyle(style) { fontSize = Mathf.RoundToInt(taille * 1.6f), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        styleMessage.normal.textColor = new Color(1f, 0.75f, 0.2f);
        styleGrand = new GUIStyle(style) { fontSize = taille * 3, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        styleSousTitre = new GUIStyle(style) { alignment = TextAnchor.MiddleCenter };
    }

    static void Ombre(Rect r, string texte, GUIStyle s)
    {
        var c = s.normal.textColor;
        s.normal.textColor = new Color(0, 0, 0, 0.7f);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), texte, s);
        s.normal.textColor = c;
        GUI.Label(r, texte, s);
    }
}

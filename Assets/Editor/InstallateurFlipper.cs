using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Menu "Flipper > Installer" : met en place automatiquement dans la scène ouverte
// le GameManager, la zone du lanceur, la zone de drain et les composants des bumpers.
// On peut le relancer sans risque : il réutilise les objets déjà créés (même nom).
// Tout est annulable avec Ctrl+Z.
public static class InstallateurFlipper
{
    const string NomZoneLanceur = "Zone_Lanceur";
    const string NomZoneDrain = "Zone_Drain";
    const string NomDepartBille = "Depart_Bille";
    const string NomGameManager = "GameManager";
    const int PointsRabatoires = 10;

    [MenuItem("Flipper/Installer")]
    public static void Installer()
    {
        var rapport = new StringBuilder("Flipper > Installer :\n");
        var aVerifier = new StringBuilder();

        Undo.SetCurrentGroupName("Installer le flipper");
        int groupeUndo = Undo.GetCurrentGroup();

        // ---------------------------------------------------------------- 1. La bille
        GameObject bille = TrouverBille();
        if (bille == null)
        {
            EditorUtility.DisplayDialog("Flipper", "Bille introuvable : il faut un objet avec le tag \"Ball\" ou nommé \"la_bille\".", "OK");
            return;
        }
        if (!bille.CompareTag(Bille.Tag))
        {
            Undo.RecordObject(bille, "Tag bille");
            bille.tag = Bille.Tag;
            rapport.AppendLine("- Tag \"Ball\" mis sur la bille.");
        }
        Rigidbody rbBille = bille.GetComponent<Rigidbody>();
        if (rbBille == null)
        {
            rbBille = Undo.AddComponent<Rigidbody>(bille);
            rapport.AppendLine("- Rigidbody ajouté sur la bille.");
        }
        float diametre = DiametreBille(bille);

        // Orientation de la table : normale (haut de la table) et direction "vers le haut du plateau"
        Vector3 normale = NormaleTable(bille);
        Vector3 hautTable = Vector3.ProjectOnPlane(Vector3.forward, normale).normalized;
        if (hautTable.sqrMagnitude < 0.01f) hautTable = Vector3.forward;
        Quaternion rotationTable = Quaternion.LookRotation(hautTable, normale);

        // ---------------------------------------------------------------- 2. GameManager + point de départ
        GameManager gm = Object.FindAnyObjectByType<GameManager>();
        if (gm == null)
        {
            var go = new GameObject(NomGameManager);
            Undo.RegisterCreatedObjectUndo(go, "GameManager");
            gm = Undo.AddComponent<GameManager>(go);
            rapport.AppendLine("- GameManager créé.");
        }
        else rapport.AppendLine("- GameManager déjà présent : réutilisé.");

        bool departCree;
        GameObject depart = TrouverOuCreer(NomDepartBille, bille.transform.position, bille.transform.rotation, out departCree);
        if (departCree) rapport.AppendLine("- Depart_Bille créé à la position actuelle de la bille.");

        Undo.RecordObject(gm, "GameManager");
        gm.bille = rbBille;
        gm.pointDeDepart = depart.transform;
        gm.billesParPartie = 3;
        gm.delaiRemiseEnJeu = 1f;
        EditorUtility.SetDirty(gm);

        // ---------------------------------------------------------------- 3. Zone du lanceur
        bool zoneCreee;
        GameObject zoneGo = TrouverOuCreer(NomZoneLanceur, depart.transform.position, rotationTable, out zoneCreee);
        BoxCollider boiteLanceur = ObtenirOuAjouter<BoxCollider>(zoneGo);
        boiteLanceur.isTrigger = true;
        if (zoneCreee)
        {
            boiteLanceur.center = Vector3.zero;
            boiteLanceur.size = new Vector3(diametre * 1.8f, diametre * 2f, diametre * 4f);
            rapport.AppendLine("- Zone_Lanceur créée autour de la bille (axe bleu = direction du tir).");
        }
        ZoneLanceur zoneLanceur = ObtenirOuAjouter<ZoneLanceur>(zoneGo);

        // ---------------------------------------------------------------- 4. Ressort (lanceur)
        GameObject ressort = TrouverParNom("ressort");
        if (ressort != null)
        {
            Rigidbody rbRessort = ObtenirOuAjouter<Rigidbody>(ressort);
            rbRessort.isKinematic = true;
            rbRessort.useGravity = false;

            Script_ressort script = ObtenirOuAjouter<Script_ressort>(ressort);
            script.zoneLanceur = zoneLanceur;
            script.bille = rbBille;
            if (script.audioSource == null)
                script.audioSource = ressort.GetComponent<AudioSource>();
            EditorUtility.SetDirty(script);
            rapport.AppendLine("- Ressort relié à la Zone_Lanceur et à la bille.");
        }
        else aVerifier.AppendLine("- Objet \"ressort\" introuvable : ajoutez Script_ressort sur votre lanceur.");

        // ---------------------------------------------------------------- 5. Zone de drain
        Transform gauche = TrouverParNom("flipper_gauche")?.transform;
        Transform droite = TrouverParNom("flipper_droite")?.transform;
        Vector3 posDrain;
        float largeurDrain;
        if (gauche != null && droite != null)
        {
            Vector3 milieu = (gauche.position + droite.position) * 0.5f;
            largeurDrain = Vector3.Distance(gauche.position, droite.position) + 3f;
            posDrain = milieu - hautTable * 2.5f - normale * 0.5f;
        }
        else
        {
            largeurDrain = 6f;
            posDrain = depart.transform.position - hautTable * 3f;
            aVerifier.AppendLine("- flipper_gauche / flipper_droite introuvables : Zone_Drain placée au hasard, à déplacer.");
        }

        bool drainCree;
        GameObject drain = TrouverOuCreer(NomZoneDrain, posDrain, rotationTable, out drainCree);
        BoxCollider boiteDrain = ObtenirOuAjouter<BoxCollider>(drain);
        boiteDrain.isTrigger = true;
        if (drainCree)
        {
            boiteDrain.center = Vector3.zero;
            boiteDrain.size = new Vector3(largeurDrain, 2f, 2f);
            rapport.AppendLine("- Zone_Drain créée sous les flippers.");
        }
        ObtenirOuAjouter<RetourDepart>(drain);

        // ---------------------------------------------------------------- 6. Bumpers
        int nbBumpers = 0;
        foreach (GameObject bumper in TrouverBumpers())
        {
            int nbColliders = 0;
            foreach (MeshFilter mf in bumper.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                MeshCollider mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
                mc.sharedMesh = mf.sharedMesh;
                nbColliders++;
            }

            // Rigidbody cinématique sur la racine : le choc est reçu par BumperEffect
            // même si le collider est sur un objet enfant.
            Rigidbody rbBumper = ObtenirOuAjouter<Rigidbody>(bumper);
            rbBumper.isKinematic = true;
            rbBumper.useGravity = false;

            BumperEffect effet = ObtenirOuAjouter<BumperEffect>(bumper);
            effet.points = 100;

            // BumperEffect compte déjà les points : pas de ScoreOnHit en plus (sinon points en double)
            foreach (ScoreOnHit doublon in bumper.GetComponentsInChildren<ScoreOnHit>(true))
                Undo.DestroyObjectImmediate(doublon);

            nbBumpers++;
            rapport.AppendLine($"- Bumper \"{bumper.name}\" : BumperEffect (100 pts)" + (nbColliders > 0 ? $", {nbColliders} MeshCollider ajouté(s)." : "."));
        }
        if (nbBumpers == 0)
            aVerifier.AppendLine("- Aucun bumper trouvé (objets dont le nom commence par \"Bumper\").");

        // ---------------------------------------------------------------- 7. Points sur les rabatoires (cibles latérales)
        foreach (GameObject go in TousLesObjets())
        {
            if (!go.name.ToLower().StartsWith("rabatoire") || go.GetComponentInChildren<Collider>() == null)
                continue;
            if (go.GetComponent<ScoreOnHit>() == null)
            {
                ScoreOnHit s = Undo.AddComponent<ScoreOnHit>(go);
                s.points = PointsRabatoires;
                rapport.AppendLine($"- ScoreOnHit ({PointsRabatoires} pts) ajouté sur \"{go.name}\".");
            }
        }

        // ---------------------------------------------------------------- Fin
        Undo.CollapseUndoOperations(groupeUndo);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.objects = new Object[] { zoneGo, drain };

        rapport.AppendLine("\nÀ faire : vérifier la position et la taille de Zone_Lanceur (vert) et Zone_Drain (rouge), puis Ctrl+S.");
        if (aVerifier.Length > 0) rapport.Append("\nAttention :\n").Append(aVerifier);
        Debug.Log(rapport.ToString());

        EditorUtility.DisplayDialog("Flipper",
            "Installation terminée.\n\n" +
            "1. Vérifiez Zone_Lanceur (boîte verte, flèche jaune = direction du tir).\n" +
            "2. Vérifiez Zone_Drain (boîte rouge, sous les flippers).\n" +
            "3. Sauvegardez la scène (Ctrl+S).\n\n" +
            (aVerifier.Length > 0 ? "Attention :\n" + aVerifier + "\n" : "") +
            "Détails dans la Console.", "OK");
    }

    // Ajoute ScoreOnHit sur les objets sélectionnés (cibles, poteaux, rampes...).
    [MenuItem("Flipper/Ajouter des points à la sélection (ScoreOnHit)")]
    public static void AjouterPointsSelection()
    {
        foreach (GameObject go in Selection.gameObjects)
        {
            if (go.GetComponent<ScoreOnHit>() != null) continue;
            Undo.AddComponent<ScoreOnHit>(go);
            if (go.GetComponentInChildren<Collider>() == null)
                Debug.LogWarning($"\"{go.name}\" n'a pas de collider : ScoreOnHit ne détectera pas la bille.", go);
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    [MenuItem("Flipper/Ajouter des points à la sélection (ScoreOnHit)", true)]
    static bool ValiderAjouterPoints() => Selection.gameObjects.Length > 0;

    // ==================================================================== outils

    static IEnumerable<GameObject> TousLesObjets()
    {
        foreach (GameObject racine in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Transform t in racine.GetComponentsInChildren<Transform>(true))
                yield return t.gameObject;
    }

    static GameObject TrouverParNom(string nom)
    {
        foreach (GameObject go in TousLesObjets())
            if (go.name == nom) return go;
        return null;
    }

    static GameObject TrouverBille()
    {
        foreach (GameObject go in TousLesObjets())
            if (go.CompareTag(Bille.Tag)) return go;
        return TrouverParNom("la_bille");
    }

    // Objets dont le nom commence par "Bumper" (on garde le plus haut dans la hiérarchie)
    static List<GameObject> TrouverBumpers()
    {
        var liste = new List<GameObject>();
        foreach (GameObject go in TousLesObjets())
        {
            if (!go.name.ToLower().StartsWith("bumper")) continue;
            Transform parent = go.transform.parent;
            if (parent != null && parent.name.ToLower().StartsWith("bumper")) continue;
            liste.Add(go);
        }
        return liste;
    }

    static GameObject TrouverOuCreer(string nom, Vector3 position, Quaternion rotation, out bool cree)
    {
        GameObject go = TrouverParNom(nom);
        cree = go == null;
        if (cree)
        {
            go = new GameObject(nom);
            go.transform.SetPositionAndRotation(position, rotation);
            Undo.RegisterCreatedObjectUndo(go, nom);
        }
        return go;
    }

    static T ObtenirOuAjouter<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        if (c == null) return Undo.AddComponent<T>(go);
        Undo.RecordObject(c, typeof(T).Name);
        return c;
    }

    static float DiametreBille(GameObject bille)
    {
        var sphere = bille.GetComponent<SphereCollider>();
        Vector3 e = bille.transform.lossyScale;
        float echelle = Mathf.Max(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z));
        return sphere != null ? sphere.radius * 2f * echelle : echelle;
    }

    // Normale de la table : l'axe de rotation des flippers, sinon un rayon vers le bas sous la bille.
    static Vector3 NormaleTable(GameObject bille)
    {
        Vector3 somme = Vector3.zero;
        foreach (GameObject go in TousLesObjets())
            if (go.GetComponent<Flipper>() != null)
                somme += go.transform.up;
        if (somme.sqrMagnitude > 0.01f)
            return somme.normalized;

        Physics.SyncTransforms();
        foreach (RaycastHit hit in Physics.RaycastAll(bille.transform.position + Vector3.up, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
            if (hit.collider.gameObject != bille)
                return hit.normal;

        return Vector3.up;
    }
}

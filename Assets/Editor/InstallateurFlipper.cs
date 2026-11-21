using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Menu "Flipper > Installer" : met en place automatiquement dans la scène ouverte
//  - les collisions (plateau, murs, bumpers, poteaux, slingshot, flippers) + matériaux physiques,
//  - les flippers 3D branchés sur flipper_gauche / flipper_droite (script Flipper),
//  - la bille posée sur le plateau et le ressort placé derrière elle,
//  - le GameManager, la zone du lanceur, la zone de drain et les composants des bumpers.
// On peut le relancer sans risque : il réutilise les objets déjà créés (même nom).
// Tout est annulable avec Ctrl+Z.
public static class InstallateurFlipper
{
    const string NomZoneLanceur = "Zone_Lanceur";
    const string NomZoneDrain = "Zone_Drain";
    const string NomDepartBille = "Depart_Bille";
    const string NomGameManager = "GameManager";
    const string DossierPhysique = "Assets/Physique";
    const int PointsRabatoires = 10;
    const int PointsSlingshot = 10;

    // Type de collider à mettre sur un mesh
    enum Categorie { Aucune, PlateauStatique, PlateauConvexe, CaoutchoucConvexe }

    // ==================================================================== MENU : INSTALLER

    [MenuItem("Flipper/Installer", priority = 0)]
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

        // ---------------------------------------------------------------- 2. Collisions partout
        AjouterCollisions(rapport);
        Rigidbody rbBille = bille.GetComponent<Rigidbody>();
        float diametre = DiametreBille(bille);
        float rayon = diametre * 0.5f;

        // ---------------------------------------------------------------- 3. Orientation de la table
        Physics.SyncTransforms();
        bool surPlateau = RaycastSousBille(bille, out RaycastHit sol);
        Vector3 normale = surPlateau ? sol.normal : NormaleFlippers();
        Vector3 hautTable = Vector3.ProjectOnPlane(Vector3.forward, normale).normalized;
        if (hautTable.sqrMagnitude < 0.01f) hautTable = Vector3.forward;
        Quaternion rotationTable = Quaternion.LookRotation(hautTable, normale);

        // ---------------------------------------------------------------- 4. Bille posée sur le plateau
        Vector3 ancienneBille = bille.transform.position;
        if (surPlateau)
        {
            Vector3 posee = sol.point + normale * (rayon + 0.005f);
            if (Vector3.Distance(posee, ancienneBille) > 0.01f)
            {
                Undo.RecordObject(bille.transform, "Poser la bille");
                bille.transform.position = posee;
                rapport.AppendLine("- Bille posée à la surface du plateau.");
            }
        }
        else aVerifier.AppendLine("- Pas de plateau trouvé sous la bille : vérifiez qu'elle est bien au-dessus de la table.");

        // ---------------------------------------------------------------- 5. Flippers 3D branchés sur les scripts
        BrancherFlippers(normale, hautTable, rapport, aVerifier);

        // ---------------------------------------------------------------- 6. GameManager + point de départ
        GameManager gm = Object.FindAnyObjectByType<GameManager>();
        if (gm == null)
        {
            var go = new GameObject(NomGameManager);
            Undo.RegisterCreatedObjectUndo(go, "GameManager");
            gm = Undo.AddComponent<GameManager>(go);
            rapport.AppendLine("- GameManager créé.");
        }

        GameObject depart = TrouverOuCreer(NomDepartBille, bille.transform.position, bille.transform.rotation, out bool departCree);
        if (!departCree && Vector3.Distance(depart.transform.position, ancienneBille) < 1f)
        {
            Undo.RecordObject(depart.transform, "Depart_Bille");
            depart.transform.position = bille.transform.position;
        }

        Undo.RecordObject(gm, "GameManager");
        gm.bille = rbBille;
        gm.pointDeDepart = depart.transform;
        gm.billesParPartie = 3;
        gm.delaiRemiseEnJeu = 1f;
        if (gm.hauteurPerte > 3f) gm.hauteurPerte = 3f;   // bille tombée dans la caisse = perdue
        EditorUtility.SetDirty(gm);

        // ---------------------------------------------------------------- 7. Ressort placé derrière la bille
        GameObject ressort = TrouverParNom("ressort");
        if (ressort != null)
        {
            PlacerRessort(ressort, depart.transform.position, rayon, normale, hautTable, rapport);

            Rigidbody rbRessort = ObtenirOuAjouter<Rigidbody>(ressort);
            rbRessort.isKinematic = true;
            rbRessort.useGravity = false;
        }
        else aVerifier.AppendLine("- Objet \"ressort\" introuvable : ajoutez Script_ressort sur votre lanceur.");

        // ---------------------------------------------------------------- 8. Zone du lanceur
        GameObject zoneGo = TrouverOuCreer(NomZoneLanceur, depart.transform.position, rotationTable, out bool zoneCreee);
        BoxCollider boiteLanceur = ObtenirOuAjouter<BoxCollider>(zoneGo);
        boiteLanceur.isTrigger = true;
        if (zoneCreee || Vector3.Distance(zoneGo.transform.position, depart.transform.position) > 3f)
        {
            Undo.RecordObject(zoneGo.transform, "Zone_Lanceur");
            zoneGo.transform.SetPositionAndRotation(depart.transform.position, rotationTable);
            boiteLanceur.center = Vector3.zero;
            boiteLanceur.size = new Vector3(diametre * 1.8f, diametre * 2f, diametre * 4f);
            rapport.AppendLine("- Zone_Lanceur placée autour de la bille (flèche jaune = direction du tir).");
        }
        ZoneLanceur zoneLanceur = ObtenirOuAjouter<ZoneLanceur>(zoneGo);

        if (ressort != null)
        {
            Script_ressort script = ObtenirOuAjouter<Script_ressort>(ressort);
            script.zoneLanceur = zoneLanceur;
            script.bille = rbBille;
            if (script.audioSource == null)
                script.audioSource = ressort.GetComponent<AudioSource>();
            // Anciennes valeurs par défaut (trop faibles pour cette table) remplacées
            if (Mathf.Approximately(script.vitesseMin, 6f) && Mathf.Approximately(script.vitesseMax, 18f))
            {
                script.vitesseMin = 13f;
                script.vitesseMax = 20f;
            }
            EditorUtility.SetDirty(script);
        }

        // ---------------------------------------------------------------- 8 bis. Courbe en haut du couloir + vitre
        ConstruireGuideHaut(bille, rayon, normale, hautTable, rapport, aVerifier);
        ConstruireVitre(depart.transform.position - normale * rayon, diametre, normale, hautTable, rapport);

        // Flippers trop rapides : à 1800°/s la bille part à ~26 m/s et s'envole hors de la machine
        foreach (GameObject go in TousLesObjets())
        {
            Flipper f = go.GetComponent<Flipper>();
            if (f == null || f.vitesseMontee <= 1000f) continue;
            Undo.RecordObject(f, "Vitesse flipper");
            f.vitesseMontee = 900f;
            EditorUtility.SetDirty(f);
            rapport.AppendLine($"- {go.name} : vitesse de levée réduite à 900°/s.");
        }

        // ---------------------------------------------------------------- 9. Zone de drain
        Transform gauche = TrouverParNom("flipper_gauche")?.transform;
        Transform droite = TrouverParNom("flipper_droite")?.transform;
        Vector3 posDrain;
        float largeurDrain;
        if (gauche != null && droite != null)
        {
            Vector3 milieu = (gauche.position + droite.position) * 0.5f;
            largeurDrain = Vector3.Distance(gauche.position, droite.position) + 8f;
            posDrain = milieu - hautTable * 1.8f;
        }
        else
        {
            largeurDrain = 8f;
            posDrain = depart.transform.position - hautTable * 3f;
            aVerifier.AppendLine("- flipper_gauche / flipper_droite introuvables : Zone_Drain à placer à la main.");
        }

        GameObject drain = TrouverOuCreer(NomZoneDrain, posDrain, rotationTable, out bool drainCree);
        BoxCollider boiteDrain = ObtenirOuAjouter<BoxCollider>(drain);
        boiteDrain.isTrigger = true;
        if (drainCree || Vector3.Distance(drain.transform.position, posDrain) > 6f)
        {
            Undo.RecordObject(drain.transform, "Zone_Drain");
            drain.transform.SetPositionAndRotation(posDrain, rotationTable);
            boiteDrain.center = Vector3.zero;
            boiteDrain.size = new Vector3(largeurDrain, 2f, 1.6f);
            rapport.AppendLine("- Zone_Drain placée sous les flippers.");
        }
        ObtenirOuAjouter<RetourDepart>(drain);

        // ---------------------------------------------------------------- 10. Bumpers
        int nbBumpers = 0;
        foreach (GameObject bumper in TrouverRacines("bumper"))
        {
            // Rigidbody cinématique sur la racine : le choc est reçu par BumperEffect
            // même si les colliders sont sur des objets enfants.
            Rigidbody rbBumper = ObtenirOuAjouter<Rigidbody>(bumper);
            rbBumper.isKinematic = true;
            rbBumper.useGravity = false;

            BumperEffect effet = ObtenirOuAjouter<BumperEffect>(bumper);
            effet.points = 100;

            // BumperEffect compte déjà les points : pas de ScoreOnHit en plus (sinon points en double)
            foreach (ScoreOnHit doublon in bumper.GetComponentsInChildren<ScoreOnHit>(true))
                Undo.DestroyObjectImmediate(doublon);

            nbBumpers++;
        }
        if (nbBumpers > 0) rapport.AppendLine($"- {nbBumpers} bumper(s) : BumperEffect (100 pts).");
        else aVerifier.AppendLine("- Aucun bumper trouvé (objets dont le nom commence par \"Bumper\").");

        // ---------------------------------------------------------------- 11. Points sur les rabatoires et le slingshot
        foreach (GameObject go in TousLesObjets())
        {
            string nom = go.name.ToLower();
            int points;
            if (nom.StartsWith("rabatoire")) points = PointsRabatoires;
            else if (nom.Contains("rubber")) points = PointsSlingshot;
            else continue;

            if (go.GetComponent<Collider>() == null || go.GetComponent<ScoreOnHit>() != null)
                continue;
            ScoreOnHit s = Undo.AddComponent<ScoreOnHit>(go);
            s.points = points;
            rapport.AppendLine($"- ScoreOnHit ({points} pts) ajouté sur \"{go.name}\".");
        }

        // ---------------------------------------------------------------- Fin
        Undo.CollapseUndoOperations(groupeUndo);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.objects = new Object[] { zoneGo, drain };

        rapport.AppendLine("\nÀ faire : vérifier Zone_Lanceur (vert), Zone_Drain (rouge) et le ressort, puis Ctrl+S.");
        if (aVerifier.Length > 0) rapport.Append("\nAttention :\n").Append(aVerifier);
        Debug.Log(rapport.ToString());

        EditorUtility.DisplayDialog("Flipper",
            "Installation terminée.\n\n" +
            "1. Vérifiez Zone_Lanceur (boîte verte, flèche jaune = direction du tir) et le ressort derrière la bille.\n" +
            "2. Vérifiez Zone_Drain (boîte rouge, sous les flippers).\n" +
            "3. Sauvegardez la scène (Ctrl+S).\n\n" +
            (aVerifier.Length > 0 ? "Attention :\n" + aVerifier + "\n" : "") +
            "Détails dans la Console.", "OK");
    }

    // ==================================================================== MENU : COLLISIONS

    [MenuItem("Flipper/Ajouter les collisions", priority = 1)]
    public static void MenuAjouterCollisions()
    {
        var rapport = new StringBuilder("Flipper > Ajouter les collisions :\n");
        Undo.SetCurrentGroupName("Ajouter les collisions");
        int groupe = Undo.GetCurrentGroup();
        AjouterCollisions(rapport);
        Undo.CollapseUndoOperations(groupe);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log(rapport.ToString());
    }

    // Ajoute les colliders et les matériaux physiques sur tout ce que la bille doit toucher.
    static void AjouterCollisions(StringBuilder rapport)
    {
        PhysicsMaterial matBille = Materiau("Bille", 0.2f, 0.3f, PhysicsMaterialCombine.Average);
        PhysicsMaterial matPlateau = Materiau("Plateau", 0.1f, 0.3f, PhysicsMaterialCombine.Average);
        PhysicsMaterial matCaoutchouc = Materiau("Caoutchouc", 0.1f, 0.7f, PhysicsMaterialCombine.Maximum);

        // Les meshes FBX doivent être en Read/Write pour les MeshCollider (sinon problème dans un build)
        var chemins = new HashSet<string>();
        foreach (GameObject go in TousLesObjets())
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            Categorie cat = Classer(go);
            if (cat == Categorie.Aucune) continue;
            string chemin = AssetDatabase.GetAssetPath(mf.sharedMesh);
            if (AssetImporter.GetAtPath(chemin) is ModelImporter imp && !imp.isReadable)
                chemins.Add(chemin);
        }
        if (chemins.Count > 0)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string chemin in chemins)
                {
                    var imp = (ModelImporter)AssetImporter.GetAtPath(chemin);
                    imp.isReadable = true;
                    imp.SaveAndReimport();
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            rapport.AppendLine($"- Read/Write activé sur {chemins.Count} modèle(s) FBX.");
        }

        int nbAjoutes = 0;
        foreach (GameObject go in TousLesObjets())
        {
            var mf = go.GetComponent<MeshFilter>();
            Categorie cat = Classer(go);

            if (cat != Categorie.Aucune && mf != null && mf.sharedMesh != null)
            {
                PhysicsMaterial mat = cat == Categorie.CaoutchoucConvexe ? matCaoutchouc : matPlateau;
                Collider col = go.GetComponent<Collider>();

                if (col == null)
                {
                    // MeshCollider = forme exacte du modèle (la surface de jeu est exactement celle qu'on voit)
                    var mc = Undo.AddComponent<MeshCollider>(go);
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = cat != Categorie.PlateauStatique;
                    col = mc;
                    col.sharedMaterial = mat;
                    nbAjoutes++;
                }
                else
                {
                    Undo.RecordObject(col, "Collider");
                    if (col is MeshCollider mc)
                    {
                        if (mc.sharedMesh == null) mc.sharedMesh = mf.sharedMesh;
                        mc.convex = cat != Categorie.PlateauStatique;
                    }
                    if (col.sharedMaterial == null) col.sharedMaterial = mat;
                }
                continue;
            }

            // Objets qui ont déjà leur collider : on leur donne juste un matériau physique
            string nom = go.name.ToLower();
            Collider existant = go.GetComponent<Collider>();
            if (existant == null || existant.isTrigger || existant.sharedMaterial != null) continue;

            PhysicsMaterial m = null;
            if (Bille.Est(go)) m = matBille;
            else if (nom == "ressort" || nom.StartsWith("bordure") || nom.StartsWith("rabatoire") || nom == "batte" || nom.StartsWith("cran"))
                m = matPlateau;
            if (m != null)
            {
                Undo.RecordObject(existant, "Matériau physique");
                existant.sharedMaterial = m;
            }
        }
        rapport.AppendLine($"- {nbAjoutes} collider(s) ajouté(s) (plateau, murs, bumpers, poteaux, slingshot, flippers).");

        // Bille : détection continue pour ne pas traverser les murs à grande vitesse
        GameObject bille = TrouverBille();
        if (bille != null)
        {
            Rigidbody rb = ObtenirOuAjouter<Rigidbody>(bille);
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (bille.GetComponent<Collider>() == null)
                Undo.AddComponent<SphereCollider>(bille).sharedMaterial = matBille;
        }
    }

    // Décide quel collider mettre sur un objet qui a un mesh (d'après son nom et celui de ses parents).
    static Categorie Classer(GameObject go)
    {
        string nom = go.name.ToLower();
        for (Transform t = go.transform; t != null; t = t.parent)
        {
            string n = t.name.ToLower();
            if (n == "pinballtable")
                return Categorie.PlateauStatique;
            if (n.StartsWith("bumper"))
                return nom.Contains("screw") ? Categorie.Aucune : Categorie.CaoutchoucConvexe;
            if (n.StartsWith("metal posts"))
                return Categorie.CaoutchoucConvexe;
            if (n == "miscellaneous")
                return nom.Contains("rubber") ? Categorie.CaoutchoucConvexe : Categorie.PlateauConvexe;
            if (n.StartsWith("flippers"))     // modèles 3D des flippers
                return Categorie.PlateauConvexe;
        }
        return Categorie.Aucune;
    }

    static PhysicsMaterial Materiau(string nom, float friction, float rebond, PhysicsMaterialCombine combineRebond)
    {
        if (!AssetDatabase.IsValidFolder(DossierPhysique))
            AssetDatabase.CreateFolder("Assets", "Physique");

        string chemin = $"{DossierPhysique}/{nom}.physicMaterial";
        var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(chemin);
        if (mat != null) return mat;

        mat = new PhysicsMaterial(nom)
        {
            dynamicFriction = friction,
            staticFriction = friction,
            bounciness = rebond,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = combineRebond
        };
        AssetDatabase.CreateAsset(mat, chemin);
        return mat;
    }

    // ==================================================================== FLIPPERS

    // Met chaque modèle 3D "Flippers" sous son pivot flipper_gauche / flipper_droite (script Flipper),
    // le pivot étant placé à l'origine du modèle (l'axe de rotation du flipper).
    static void BrancherFlippers(Vector3 normale, Vector3 hautTable, StringBuilder rapport, StringBuilder aVerifier)
    {
        List<GameObject> modeles = TousLesObjets()
            .Where(go => go.name.ToLower().StartsWith("flippers")
                      && go.GetComponent<MeshFilter>() != null
                      && go.GetComponentInParent<Flipper>(true) == null)
            .ToList();

        if (modeles.Count == 0) return;   // déjà branchés (ou pas de modèles)
        if (modeles.Count != 2)
        {
            aVerifier.AppendLine($"- {modeles.Count} modèle(s) \"Flippers\" trouvé(s) au lieu de 2 : flippers non branchés automatiquement.");
            return;
        }

        // Gauche / droite vus par le joueur (qui regarde vers le haut de la table)
        Vector3 droiteJoueur = Vector3.Cross(normale, hautTable);
        modeles.Sort((a, b) => Vector3.Dot(a.transform.position, droiteJoueur).CompareTo(Vector3.Dot(b.transform.position, droiteJoueur)));

        Quaternion rotationPivot = Quaternion.LookRotation(hautTable, normale);
        BrancherUnFlipper("flipper_gauche", true, modeles[0], rotationPivot, rapport);
        BrancherUnFlipper("flipper_droite", false, modeles[1], rotationPivot, rapport);
    }

    static void BrancherUnFlipper(string nomPivot, bool estGauche, GameObject modele, Quaternion rotationPivot, StringBuilder rapport)
    {
        GameObject pivot = TrouverParNom(nomPivot);
        if (pivot == null)
        {
            pivot = new GameObject(nomPivot);
            Undo.RegisterCreatedObjectUndo(pivot, nomPivot);
        }

        // Les anciens enfants (ancienne "batte") sont désactivés, pas supprimés
        foreach (Transform enfant in pivot.transform)
        {
            if (!enfant.gameObject.activeSelf) continue;
            Undo.RecordObject(enfant.gameObject, "Désactiver ancienne batte");
            enfant.gameObject.SetActive(false);
            rapport.AppendLine($"- Ancien enfant \"{enfant.name}\" de {nomPivot} désactivé.");
        }

        Undo.RecordObject(pivot.transform, "Pivot flipper");
        pivot.transform.SetParent(null, true);
        pivot.transform.SetPositionAndRotation(modele.transform.position, rotationPivot);
        pivot.transform.localScale = Vector3.one;

        Undo.SetTransformParent(modele.transform, pivot.transform, "Brancher flipper");

        Rigidbody rb = ObtenirOuAjouter<Rigidbody>(pivot);
        rb.isKinematic = true;
        rb.useGravity = false;

        Flipper flipper = ObtenirOuAjouter<Flipper>(pivot);
        flipper.estGauche = estGauche;
        EditorUtility.SetDirty(flipper);

        rapport.AppendLine($"- Modèle \"{modele.name}\" branché sur {nomPivot} (pivot = origine du modèle).");
    }

    // ==================================================================== RESSORT

    // Couche le ressort dans le couloir, juste derrière la bille, s'il en est loin.
    static void PlacerRessort(GameObject ressort, Vector3 positionBille, float rayon, Vector3 normale, Vector3 hautTable, StringBuilder rapport)
    {
        var rendu = ressort.GetComponent<Renderer>();
        Bounds limites = rendu != null ? rendu.bounds : new Bounds(ressort.transform.position, Vector3.zero);
        if (limites.SqrDistance(positionBille) < 1f)
            return; // déjà contre la bille : on ne touche pas

        var mf = ressort.GetComponent<MeshFilter>();
        float demiLongueur = mf != null && mf.sharedMesh != null
            ? mf.sharedMesh.bounds.extents.y * Mathf.Abs(ressort.transform.lossyScale.y)
            : 0.5f;

        Undo.RecordObject(ressort.transform, "Placer le ressort");
        // Axe Y du ressort (sa longueur) dans l'axe du couloir
        ressort.transform.rotation = Quaternion.LookRotation(-normale, hautTable);
        ressort.transform.position = positionBille - hautTable * (rayon + demiLongueur + 0.02f);
        rapport.AppendLine("- Ressort couché dans le couloir, juste derrière la bille.");
    }

    // ==================================================================== GUIDE COURBE

    const string NomGuideHaut = "Guide_Haut_Droite";
    const string NomMurFond = "Mur_Fond";
    const float RayonGuide = 2.5f;
    const int SegmentsGuide = 8;

    // En haut de la table :
    //  - Mur_Fond : mur invisible au bout de la surface de jeu (bouche le trou entre le plateau et le mur du fond),
    //  - Guide_Haut_Droite : arc de cercle en haut du couloir du lanceur, qui renvoie la bille vers le plateau.
    // Les segments de l'arc sont des cubes : vous pouvez les remplacer par votre propre modèle.
    static void ConstruireGuideHaut(GameObject bille, float rayon, Vector3 normale, Vector3 hautTable, StringBuilder rapport, StringBuilder aVerifier)
    {
        Physics.SyncTransforms();
        Vector3 depart = bille.transform.position;
        Vector3 droite = Vector3.Cross(normale, hautTable);
        Vector3 surface = depart - normale * rayon;   // point de la surface de jeu sous la bille

        // Bout de la surface de jeu : on avance dans le couloir tant qu'il y a un sol sous la bille
        float distFond = -1f;
        for (float d = 0f; d < 60f; d += 0.1f)
        {
            Vector3 p = surface + hautTable * d;
            bool sol = RaycastHorsBille(p + normale * 0.5f, -normale, 1f, out RaycastHit _);
            bool mur = RaycastHorsBille(p + normale * rayon, hautTable, 0.1f + rayon, out RaycastHit _);
            if (!sol || mur) { distFond = d; break; }
        }
        if (!RaycastHorsBille(depart, droite, 5f, out RaycastHit murDroit) || distFond < 1f)
        {
            aVerifier.AppendLine("- Bout du plateau ou mur droit du couloir introuvable : courbe du haut non créée.");
            return;
        }
        float distDroite = Vector3.Dot(murDroit.point - depart, droite);

        PhysicsMaterial mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>($"{DossierPhysique}/Plateau.physicMaterial");

        if (TrouverParNom(NomMurFond) == null)
        {
            var mur = new GameObject(NomMurFond);
            Undo.RegisterCreatedObjectUndo(mur, NomMurFond);
            mur.transform.SetPositionAndRotation(
                surface + hautTable * (distFond + 0.25f) + normale * 0.5f - droite * 8f,
                Quaternion.LookRotation(hautTable, normale));
            var box = mur.AddComponent<BoxCollider>();
            box.size = new Vector3(30f, 3f, 0.5f);   // large : il dépasse des côtés de la caisse, sans effet
            if (mat != null) box.sharedMaterial = mat;
            rapport.AppendLine("- Mur_Fond créé (invisible) : bouche le trou en haut du plateau.");
        }

        if (TrouverParNom(NomGuideHaut) != null) return;   // déjà construit

        // Centre de l'arc : tangent au mur droit du couloir et au mur du fond
        Vector3 centre = surface + droite * (distDroite - RayonGuide) + hautTable * (distFond - RayonGuide);

        var parent = new GameObject(NomGuideHaut);
        Undo.RegisterCreatedObjectUndo(parent, NomGuideHaut);
        parent.transform.position = centre;

        const float epaisseur = 0.2f, hauteur = 1f;
        float pas = 90f / SegmentsGuide;
        float longueur = 2f * (RayonGuide + epaisseur * 0.5f) * Mathf.Sin(pas * 0.5f * Mathf.Deg2Rad) + 0.05f;

        for (int i = 0; i < SegmentsGuide; i++)
        {
            float angle = (i + 0.5f) * pas * Mathf.Deg2Rad;      // 0° = côté couloir, 90° = côté mur du fond
            Vector3 radial = Mathf.Cos(angle) * droite + Mathf.Sin(angle) * hautTable;
            Vector3 tangente = -Mathf.Sin(angle) * droite + Mathf.Cos(angle) * hautTable;

            GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seg.name = $"Segment_{i + 1}";
            Undo.RegisterCreatedObjectUndo(seg, seg.name);
            seg.transform.SetParent(parent.transform, false);
            seg.transform.SetPositionAndRotation(
                centre + radial * (RayonGuide + epaisseur * 0.5f) + normale * (hauteur * 0.5f),
                Quaternion.LookRotation(tangente, normale));
            seg.transform.localScale = new Vector3(epaisseur, hauteur, longueur);
            if (mat != null) seg.GetComponent<Collider>().sharedMaterial = mat;
        }
        rapport.AppendLine("- Guide_Haut_Droite créé : courbe en haut du couloir qui renvoie la bille sur le plateau.");
    }

    // Vitre invisible au-dessus du plateau (comme la vitre d'un vrai flipper) : la bille ne peut pas s'envoler.
    static void ConstruireVitre(Vector3 surface, float diametre, Vector3 normale, Vector3 hautTable, StringBuilder rapport)
    {
        const string nom = "Vitre";
        if (TrouverParNom(nom) != null) return;

        // Centre : milieu du plateau s'il existe, sinon autour du point de départ
        Vector3 milieu = surface;
        GameObject plateau = TrouverParNom("Playfield");
        if (plateau != null && plateau.GetComponent<Renderer>() != null)
        {
            Vector3 c = plateau.GetComponent<Renderer>().bounds.center;
            milieu = c - normale * Vector3.Dot(c - surface, normale);   // projeté sur le plan de jeu
        }

        const float epaisseur = 0.5f;
        float hauteur = diametre * 2f;   // le dessous de la vitre est à 2 diamètres de bille au-dessus du plateau

        var vitre = new GameObject(nom);
        Undo.RegisterCreatedObjectUndo(vitre, nom);
        vitre.transform.SetPositionAndRotation(
            milieu + normale * (hauteur + epaisseur * 0.5f),
            Quaternion.LookRotation(hautTable, normale));
        var box = vitre.AddComponent<BoxCollider>();
        box.size = new Vector3(30f, epaisseur, 40f);   // plus grande que la table : sans effet en dehors
        PhysicsMaterial mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>($"{DossierPhysique}/Plateau.physicMaterial");
        if (mat != null) box.sharedMaterial = mat;
        rapport.AppendLine("- Vitre créée (invisible) au-dessus du plateau : la bille ne peut plus s'envoler.");
    }

    static bool RaycastHorsBille(Vector3 origine, Vector3 direction, float distance, out RaycastHit resultat)
    {
        resultat = default;
        foreach (RaycastHit hit in Physics.RaycastAll(origine, direction, distance, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
        {
            if (Bille.Est(hit.collider.gameObject)) continue;
            if (hit.collider.attachedRigidbody != null && !hit.collider.attachedRigidbody.isKinematic) continue;
            resultat = hit;
            return true;
        }
        return false;
    }

    // ==================================================================== MENU : POINTS

    // Ajoute ScoreOnHit sur les objets sélectionnés (cibles, poteaux, rampes...).
    [MenuItem("Flipper/Ajouter des points à la sélection (ScoreOnHit)", priority = 20)]
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
        var liste = new List<GameObject>();
        foreach (GameObject racine in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Transform t in racine.GetComponentsInChildren<Transform>(true))
                liste.Add(t.gameObject);
        return liste;
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

    // Objets dont le nom commence par le préfixe (on garde le plus haut dans la hiérarchie)
    static List<GameObject> TrouverRacines(string prefixe)
    {
        var liste = new List<GameObject>();
        foreach (GameObject go in TousLesObjets())
        {
            if (!go.name.ToLower().StartsWith(prefixe)) continue;
            Transform parent = go.transform.parent;
            if (parent != null && parent.name.ToLower().StartsWith(prefixe)) continue;
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

    // Rayon vers le bas sous la bille : renvoie le plateau (le plus proche, hors bille et triggers).
    static bool RaycastSousBille(GameObject bille, out RaycastHit resultat)
    {
        resultat = default;
        var touches = Physics.RaycastAll(bille.transform.position + Vector3.up * 2f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in touches.OrderBy(h => h.distance))
        {
            if (hit.collider.attachedRigidbody != null && Bille.Est(hit.collider.attachedRigidbody.gameObject)) continue;
            if (Bille.Est(hit.collider.gameObject)) continue;
            if (hit.collider.attachedRigidbody != null && !hit.collider.attachedRigidbody.isKinematic) continue;
            if (hit.collider.GetComponentInParent<Script_ressort>() != null) continue;
            if (hit.collider.name == "Vitre") continue;   // la vitre est au-dessus de la bille, pas dessous
            resultat = hit;
            return true;
        }
        return false;
    }

    // Normale de secours : l'axe de rotation des flippers.
    static Vector3 NormaleFlippers()
    {
        Vector3 somme = Vector3.zero;
        foreach (GameObject go in TousLesObjets())
            if (go.GetComponent<Flipper>() != null)
                somme += go.transform.up;
        return somme.sqrMagnitude > 0.01f ? somme.normalized : Vector3.up;
    }
}

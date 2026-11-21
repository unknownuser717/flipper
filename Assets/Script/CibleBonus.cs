using UnityEngine;

// Pastille de bonus au sol (rollover) : la bille passe dessus -> points + la pastille s'allume.
// Si elle fait partie d'un GroupeBonus (objet parent), le groupe donne un gros bonus quand toutes sont allumées.
// Il faut un collider "Is Trigger" sur cet objet, et la pastille visible (Renderer) en enfant.
[RequireComponent(typeof(Collider))]
public class CibleBonus : MonoBehaviour
{
    [Tooltip("Points à chaque passage.")]
    public int points = 250;
    [Tooltip("Temps minimum entre deux passages comptés.")]
    public float delaiMinimum = 0.3f;

    [Header("Couleurs")]
    public Color couleurEteinte = new Color(0.25f, 0.08f, 0.03f);
    public Color couleurAllumee = new Color(1f, 0.55f, 0.1f);
    [Tooltip("Intensité de la lumière émise quand la pastille est allumée.")]
    public float intensiteAllumee = 3f;

    public bool Allumee { get; private set; }

    private Renderer rendu;
    private MaterialPropertyBlock bloc;
    private GroupeBonus groupe;
    private float dernierPassage = -10f;

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
        rendu = GetComponentInChildren<Renderer>();
        groupe = GetComponentInParent<GroupeBonus>();
        bloc = new MaterialPropertyBlock();
        Appliquer(false);
    }

    void OnTriggerEnter(Collider other)
    {
        bool estBille = Bille.Est(other.gameObject)
                     || (other.attachedRigidbody != null && Bille.Est(other.attachedRigidbody.gameObject));
        if (!estBille || Time.time - dernierPassage < delaiMinimum)
            return;
        dernierPassage = Time.time;

        if (GameManager.Instance != null)
            GameManager.Instance.AjouterPoints(points);

        if (!Allumee)
        {
            Allumer(true);
            if (groupe != null) groupe.CibleAllumee();
        }
    }

    public void Allumer(bool allumer)
    {
        Allumee = allumer;
        Appliquer(allumer);
    }

    // Utilisé par le groupe pour faire clignoter les pastilles (sans changer l'état)
    public void Appliquer(bool lumineux)
    {
        if (rendu == null) return;
        rendu.GetPropertyBlock(bloc);
        Color c = lumineux ? couleurAllumee : couleurEteinte;
        bloc.SetColor("_BaseColor", c);
        bloc.SetColor("_EmissionColor", lumineux ? c * intensiteAllumee : c * 0.3f);
        rendu.SetPropertyBlock(bloc);
    }
}

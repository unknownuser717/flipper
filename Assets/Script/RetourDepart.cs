using UnityEngine;

public class RetourDepart : MonoBehaviour
{
    public Transform pointDeDepart;

    void OnTriggerEnter(Collider other)
    {
        if (!Bille.Est(other.gameObject) || pointDeDepart == null)
            return;

        Rigidbody rb = other.attachedRigidbody;
        if (rb != null)
        {
            // Annule la vitesse et la rotation pour que la bille reparte proprement
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = pointDeDepart.position;
        }

        // Téléporte la bille au point de départ
        other.transform.position = pointDeDepart.position;
    }
}

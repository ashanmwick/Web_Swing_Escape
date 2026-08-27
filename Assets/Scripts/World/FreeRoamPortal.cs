using UnityEngine;

public class FreeRoamPortal : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            SceneLoader.Instance.LoadFreeRoam();
        }
    }
}
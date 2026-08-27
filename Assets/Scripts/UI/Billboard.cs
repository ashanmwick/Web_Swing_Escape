using UnityEngine;

public class Billboard : MonoBehaviour
{
    private Camera cam;

    void Start()
    {
        cam = Camera.main;
    }

    void LateUpdate()
    {
        if (cam == null) return;
        // Face the camera, but keep it upright (no tilt)
        Vector3 direction = transform.position - cam.transform.position;
        direction.y = 0; // remove this line if you want full free-facing billboards
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(direction);
    }
}
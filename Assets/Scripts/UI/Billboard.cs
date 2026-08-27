using UnityEngine;

/// <summary>
/// Rotates this object each frame so its front (+Z) faces the active camera.
/// Useful for world-space labels / health bars above characters.
/// </summary>
public class Billboard : MonoBehaviour
{
    [Tooltip("Keep the object upright (ignore the camera's pitch/roll).")]
    [SerializeField] bool lockVertical = true;

    Camera cam;

    void LateUpdate()
    {
        // Re-acquire if missing (scene load, camera spawned after this object, etc.).
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        // Vector pointing from the camera to this object => object's +Z faces the camera.
        Vector3 direction = transform.position - cam.transform.position;
        if (lockVertical) direction.y = 0f;

        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction);
    }
}

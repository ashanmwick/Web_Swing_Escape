using UnityEngine;
using WebSwingEscape.Progression;

/// <summary>
/// Put this on a treadmill's trigger collider. While the <c>Player</c>-tagged
/// object is inside the volume, it feeds Speed into <see cref="PlayerProgression"/>
/// every frame via <see cref="PlayerProgression.OnTreadmillTick"/>.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Treadmill : MonoBehaviour
{
    [Tooltip("Raw Speed granted per second while the player is on this treadmill (pre-Rebirth-multiplier).")]
    [SerializeField] float speedPerSecond = 50f;

    [Tooltip("Tag of the local player object.")]
    [SerializeField] string playerTag = "Player";

    [Tooltip("Target progression system. Auto-found in the scene if left empty.")]
    [SerializeField] PlayerProgression progression;

    bool _playerInside;

    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    void Awake()
    {
        if (progression == null) progression = FindFirstObjectByType<PlayerProgression>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(playerTag)) _playerInside = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(playerTag)) _playerInside = false;
    }

    void Update()
    {
        if (!_playerInside || speedPerSecond <= 0f) return;

        if (progression == null)
        {
            progression = FindFirstObjectByType<PlayerProgression>();
            if (progression == null) return;
        }

        progression.OnTreadmillTick(speedPerSecond * Time.deltaTime);
    }
}

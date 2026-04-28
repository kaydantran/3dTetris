using UnityEngine;

public class ContainerVisibility : MonoBehaviour
{
    [Header("Walls")]
    [SerializeField] private GameObject wallFront;
    [SerializeField] private GameObject wallBack;
    [SerializeField] private GameObject wallLeft;
    [SerializeField] private GameObject wallRight;

    [Header("Corners")]
    [SerializeField] private GameObject cornerFrontLeft;
    [SerializeField] private GameObject cornerFrontRight;
    [SerializeField] private GameObject cornerBackLeft;
    [SerializeField] private GameObject cornerBackRight;

    private TetrisGrid grid;
    private Camera cam;

    private void Awake()
    {
        grid = FindAnyObjectByType<TetrisGrid>();
        cam = Camera.main;
    }

    private void LateUpdate()
    {
        if (cam == null || grid == null) return;

        Vector3 toCamera = cam.transform.position - grid.Center;
        toCamera.y = 0f;
        if (toCamera.sqrMagnitude < 0.001f) return;
        toCamera.Normalize();

        bool xDominant = Mathf.Abs(toCamera.x) >= Mathf.Abs(toCamera.z);
        bool hideFront = !xDominant && toCamera.z < 0f;
        bool hideBack  = !xDominant && toCamera.z > 0f;
        bool hideLeft  =  xDominant && toCamera.x < 0f;
        bool hideRight =  xDominant && toCamera.x > 0f;

        Show(wallFront,  !hideFront);
        Show(wallBack,   !hideBack);
        Show(wallLeft,   !hideLeft);
        Show(wallRight,  !hideRight);

        Show(cornerFrontLeft,  !(hideFront || hideLeft));
        Show(cornerFrontRight, !(hideFront || hideRight));
        Show(cornerBackLeft,   !(hideBack  || hideLeft));
        Show(cornerBackRight,  !(hideBack  || hideRight));
    }

    private void Show(GameObject obj, bool visible)
    {
        if (obj != null && obj.activeSelf != visible)
            obj.SetActive(visible);
    }
}

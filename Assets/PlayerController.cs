using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] float turnSpeed = 5f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Keyboard.current.leftArrowKey.wasPressedThisFrame && transform.position.x > -3)
        {
            transform.Translate(Vector3.left, Space.World);
        }
        if (Keyboard.current.rightArrowKey.wasPressedThisFrame && transform.position.x < 3)
        {
            transform.Translate(Vector3.right, Space.World);
        }
        if (Keyboard.current.upArrowKey.wasPressedThisFrame && transform.position.z < 3)
        {
            transform.Translate(Vector3.forward, Space.World);
        }
        if (Keyboard.current.downArrowKey.wasPressedThisFrame && transform.position.z > -3)
        {
            transform.Translate(Vector3.back, Space.World);
        }
        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            transform.Rotate(90f, 0, 0, Space.World);
        }
        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            transform.Rotate(0, 0, 90f, Space.World); 
        }
    }
}

using UnityEngine;

namespace EightRealmHorizons
{
    public class MouseLook : MonoBehaviour
    {
        //This script allows the camera view to rotate by holding and dragging the left mouse button.
        public float sensitivity = 3f;

        float xRotation;
        float yRotation;

        void Update()
        {
            if (Input.GetMouseButton(0))
            {
                yRotation += Input.GetAxis("Mouse X") * sensitivity;
                xRotation -= Input.GetAxis("Mouse Y") * sensitivity;
                xRotation = Mathf.Clamp(xRotation, -90f, 90f);

                transform.rotation = Quaternion.Euler(xRotation, yRotation, 0f);
            }
        }
    }
}
using UnityEngine;

namespace FactoryLab
{
    public interface IFactoryInteractable
    {
        string Prompt { get; }
        void Interact();
    }

    public sealed class FactoryInteraction : MonoBehaviour, IFactoryInteractable
    {
        public string prompt;
        public System.Action action;
        public string Prompt => prompt;
        public void Interact() => action?.Invoke();
    }

    [RequireComponent(typeof(CharacterController))]
    public sealed class FactoryFirstPerson : MonoBehaviour
    {
        public Camera view;
        public float sensitivity = 2f;
        public bool Walking { get; private set; }
        public string Prompt { get; private set; }
        public System.Action pause, task;
        private CharacterController body;
        private float pitch;
        private IFactoryInteractable focused;
        public void Configure(Camera camera)
        {
            gameObject.layer = 2; // Ignore Raycast: the student must never occlude their own interaction ray.
            body = GetComponent<CharacterController>();
            body.height = 1.8f; body.radius = .23f; body.center = Vector3.up * .9f;
            body.stepOffset = .12f; body.skinWidth = .025f;
            view = camera; view.transform.SetParent(transform, false);
            view.transform.localPosition = Vector3.up * 1.65f;
            view.transform.localRotation = Quaternion.identity;
            view.orthographic = false; view.fieldOfView = 65;
            view.rect = new Rect(0, 0, 1, 1);
        }
        public void SetWalking(bool value)
        {
            Walking = value; focused = null; Prompt = "";
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
        }
        public void Teleport(Vector3 point)
        {
            body.enabled = false; transform.position = point; body.enabled = true;
        }
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { SetWalking(false); pause?.Invoke(); return; }
            if (!Walking) return;
            if (Input.GetKeyDown(KeyCode.Tab)) { SetWalking(false); task?.Invoke(); return; }
            transform.Rotate(0, Input.GetAxis("Mouse X") * sensitivity, 0);
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * sensitivity, -75, 75);
            view.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            var move = Vector3.ClampMagnitude(new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical")), 1);
            body.Move((transform.TransformDirection(move) * 2f + Vector3.down * 3f) * Time.deltaTime);
            focused = null; Prompt = "";
            if (Physics.Raycast(view.transform.position, view.transform.forward, out var hit, 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                focused = hit.collider.GetComponentInParent<FactoryInteraction>();
            if (focused == null) return;
            Prompt = "E  " + focused.Prompt;
            if (Input.GetKeyDown(KeyCode.E)) focused.Interact();
        }
        private void OnApplicationFocus(bool focusedWindow) { if (!focusedWindow) { SetWalking(false); pause?.Invoke(); } }
        private void OnDisable() => SetWalking(false);
    }
}

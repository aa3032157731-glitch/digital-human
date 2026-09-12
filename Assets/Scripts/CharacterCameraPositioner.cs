using UnityEngine;

public sealed class CharacterCameraPositioner : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Character root to move and rotate.")]
    public Transform characterRoot;

    [Tooltip("Camera used as the placement reference. If empty, the Main Camera is found automatically.")]
    public Camera referenceCamera;

    [Header("Auto Find")]
    public bool autoFindMainCamera = true;
    public string fallbackCameraName = "Main Camera";

    [Tooltip("Optional fallback used only when Character Root is not assigned.")]
    public bool autoFindCharacterByName;
    public string fallbackCharacterName = "Camelmingerup";

    [Header("Placement")]
    [Min(0.1f)]
    public float distanceFromCamera = 2.5f;

    [Tooltip("Additional horizontal yaw applied after facing the camera, useful when a model's authored forward axis is offset.")]
    public float yawOffsetDegrees;

    public void PositionCharacterInFrontOfCamera()
    {
        ResolveMissingReferences();

        if (characterRoot == null)
        {
            Debug.LogWarning("CharacterCameraPositioner: Character Root is not assigned.");
            return;
        }

        if (referenceCamera == null)
        {
            Debug.LogWarning("CharacterCameraPositioner: Reference Camera is not assigned and no Main Camera was found.");
            return;
        }

        var cameraTransform = referenceCamera.transform;
        var horizontalForward = cameraTransform.forward;
        horizontalForward.y = 0f;

        if (horizontalForward.sqrMagnitude <= 0.0001f)
        {
            Debug.LogWarning("CharacterCameraPositioner: Camera forward has no usable horizontal direction.");
            return;
        }

        horizontalForward.Normalize();

        var currentHeight = characterRoot.position.y;
        var targetPosition = cameraTransform.position + horizontalForward * distanceFromCamera;
        targetPosition.y = currentHeight;

        characterRoot.position = targetPosition;
        FaceCharacterToCamera(cameraTransform.position);
    }

    private void ResolveMissingReferences()
    {
        if (referenceCamera == null && autoFindMainCamera)
        {
            referenceCamera = Camera.main;

            if (referenceCamera == null && !string.IsNullOrWhiteSpace(fallbackCameraName))
            {
                var cameraObject = GameObject.Find(fallbackCameraName);
                referenceCamera = cameraObject != null ? cameraObject.GetComponent<Camera>() : null;
            }
        }

        if (characterRoot == null && autoFindCharacterByName && !string.IsNullOrWhiteSpace(fallbackCharacterName))
        {
            var characterObject = GameObject.Find(fallbackCharacterName);
            characterRoot = characterObject != null ? characterObject.transform : null;
        }
    }

    private void FaceCharacterToCamera(Vector3 cameraPosition)
    {
        var toCamera = cameraPosition - characterRoot.position;
        toCamera.y = 0f;

        if (toCamera.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        var lookRotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
        characterRoot.rotation = lookRotation * Quaternion.Euler(0f, yawOffsetDegrees, 0f);
    }
}

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class NpcFreeCameraController : MonoBehaviour
{
    [SerializeField, Min(0.1f), Tooltip("Normal movement speed in world units per second.")]
    private float moveSpeed = 8f;
    [SerializeField, Min(1f), Tooltip("Movement multiplier while either Shift key is held.")]
    private float fastMultiplier = 3f;
    [SerializeField, Min(0.01f), Tooltip("Mouse look sensitivity while the right mouse button is held.")]
    private float lookSensitivity = 0.12f;

    private NpcDialoguePlayerController dialogueController;
    private float yaw;
    private float pitch;
    private bool pointerLocked;

    private void OnEnable()
    {
        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = NormalizeAngle(angles.x);
    }

    private void Start()
    {
        dialogueController = FindFirstObjectByType<NpcDialoguePlayerController>();
    }

    private void Update()
    {
        if (DialogueBlocksCamera())
        {
            ReleasePointer();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null) return;

        UpdateLook(mouse);
        UpdateMovement(keyboard);
    }

    private void UpdateLook(Mouse mouse)
    {
        if (mouse == null || !mouse.rightButton.isPressed)
        {
            ReleasePointer();
            return;
        }

        if (!pointerLocked)
        {
            pointerLocked = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        Vector2 delta = mouse.delta.ReadValue();
        yaw += delta.x * lookSensitivity;
        pitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -89f, 89f);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void UpdateMovement(Keyboard keyboard)
    {
        float horizontal = Axis(keyboard.aKey, keyboard.dKey);
        float forward = Axis(keyboard.sKey, keyboard.wKey);
        float vertical = Axis(keyboard.leftCtrlKey, keyboard.spaceKey);
        if (Mathf.Approximately(horizontal, 0f)
            && Mathf.Approximately(forward, 0f)
            && Mathf.Approximately(vertical, 0f)) return;

        Vector3 planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (planarForward.sqrMagnitude < 0.001f) planarForward = Vector3.forward;
        else planarForward.Normalize();
        Vector3 planarRight = Vector3.Cross(Vector3.up, planarForward).normalized;
        Vector3 direction = planarRight * horizontal + planarForward * forward
            + Vector3.up * vertical;
        if (direction.sqrMagnitude > 1f) direction.Normalize();

        bool fast = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        float speed = moveSpeed * (fast ? fastMultiplier : 1f);
        transform.position += direction * speed * Time.unscaledDeltaTime;
    }

    private bool DialogueBlocksCamera()
    {
        if (dialogueController == null)
            dialogueController = FindFirstObjectByType<NpcDialoguePlayerController>();
        return dialogueController != null
            && dialogueController.State != NpcPlayerDialogueState.Closed;
    }

    private static float Axis(ButtonControl negative, ButtonControl positive)
    {
        return (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);
    }

    private void ReleasePointer()
    {
        if (!pointerLocked) return;
        pointerLocked = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnDisable() => ReleasePointer();
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) ReleasePointer();
    }

    private static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }
}

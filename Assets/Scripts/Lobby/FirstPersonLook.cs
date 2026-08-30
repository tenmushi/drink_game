using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 一人称の視点回転。プレイヤーの子にあるカメラ(Head)に貼る。
/// 親(プレイヤールート)の向きを正面として、左右±90度=合計180度に制限する。
/// </summary>
public class FirstPersonLook : MonoBehaviour
{
    [Header("感度")]
    [SerializeField] private float sensitivity = 0.1f;

    [Header("可動範囲(度)")]
    [SerializeField] private float yawLimit = 90f;    // 左右それぞれ90度 = 前面180度
    [SerializeField] private float pitchLimit = 60f;  // 上下

    private float yaw;
    private float pitch;

    private void Start()
    {
        LockCursor(true);
    }

    private void Update()
    {
        // Escでカーソル解放 / クリックで再ロック(エディタでの作業用)
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            LockCursor(false);
        }
        else if (Cursor.lockState != CursorLockMode.Locked
                 && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            LockCursor(true);
        }

        if (Cursor.lockState != CursorLockMode.Locked) return;
        if (Mouse.current == null) return;

        Vector2 delta = Mouse.current.delta.ReadValue();

        yaw += delta.x * sensitivity;
        pitch -= delta.y * sensitivity;

        yaw = Mathf.Clamp(yaw, -yawLimit, yawLimit);
        pitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);

        // localRotation を使うので、親(座席)の向きが自動的に「正面」になる
        transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}

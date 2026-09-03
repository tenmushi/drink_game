using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// 一人称の視点回転。プレイヤーの子にあるカメラ(Head)に貼る。
/// 親(座席)の向きを正面として、左右±90度=合計180度に制限する。
///
/// 操作: 画面をドラッグ(左ボタン) / 矢印キー
/// カーソルはロックしないので、UIのボタンはいつでも押せる。
/// UIの上で押し始めたドラッグは視点操作にならない。
/// </summary>
public class FirstPersonLook : MonoBehaviour
{
    [Header("感度")]
    [SerializeField] private float dragSensitivity = 0.12f;
    [SerializeField] private float keySpeed = 90f;   // 度/秒

    [Header("可動範囲(度)")]
    [SerializeField] private float yawLimit = 90f;    // 左右それぞれ90度 = 前面180度
    [SerializeField] private float pitchLimit = 60f;  // 上下

    private float yaw;
    private float pitch;
    private bool dragging;

    /// <summary>現在の左右角(度)。親の正面を0とする。</summary>
    public float Yaw { get { return yaw; } }

    /// <summary>現在の上下角(度)。</summary>
    public float Pitch { get { return pitch; } }

    private void OnEnable()
    {
        // 念のため: 以前の設定が残っていてもカーソルを解放する
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        dragging = false;
    }

    private void Update()
    {
        HandleDrag();
        HandleKeys();

        yaw = Mathf.Clamp(yaw, -yawLimit, yawLimit);
        pitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);

        // localRotation なので、親(座席)の向きが自動的に正面になる
        transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void HandleDrag()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            // ボタンの上で押し始めたなら視点操作にしない
            dragging = !IsPointerOverUI();
        }

        if (!mouse.leftButton.isPressed)
        {
            dragging = false;
            return;
        }

        if (!dragging) return;

        Vector2 delta = mouse.delta.ReadValue();
        yaw += delta.x * dragSensitivity;
        pitch -= delta.y * dragSensitivity;
    }

    private void HandleKeys()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float h = 0f;
        float v = 0f;
        if (kb.leftArrowKey.isPressed) h -= 1f;
        if (kb.rightArrowKey.isPressed) h += 1f;
        if (kb.upArrowKey.isPressed) v += 1f;
        if (kb.downArrowKey.isPressed) v -= 1f;

        if (h == 0f && v == 0f) return;

        yaw += h * keySpeed * Time.deltaTime;
        pitch -= v * keySpeed * Time.deltaTime;
    }

    private bool IsPointerOverUI()
    {
        var es = EventSystem.current;
        return es != null && es.IsPointerOverGameObject();
    }
}

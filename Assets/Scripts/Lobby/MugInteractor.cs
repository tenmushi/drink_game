using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Player プレハブに付ける。手の届くマグをクリックすると、持ち上げて口元で傾けて飲み、テーブルに戻す。
/// 飲み終わったところで DrunkMeter の酔い度が1杯分たまる。
///
/// 左ドラッグは視点操作(FirstPersonLook)なので、押してからほとんど動かさずに離したときだけクリックとみなす。
/// 自分のカメラが有効なとき(= 一人称視点のロビー)だけ動く。
///
/// 他のプレイヤーには「どのマグを飲んでいるか」だけを DrinkingMugId で送り、動き自体は各端末が同じ手順で再生する。
/// 他人の Head(カメラ)は回らないので、他人の動きは視線が同期されている目(eyesRoot)を基準にする。
/// 酔い度がたまるのは飲んだ本人だけ。
/// </summary>
public class MugInteractor : NetworkBehaviour
{
    private const int NoMug = -1;

    /// <summary>今飲んでいるマグの Id。飲んでいなければ -1。見た目だけの情報なので所有者が直接書く。</summary>
    public NetworkVariable<int> DrinkingMugId = new NetworkVariable<int>(
        NoMug,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    [Header("参照")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("他のプレイヤーの画面で動かすときの基準。NetworkPlayer の eyesRoot と同じ Eyes")]
    [SerializeField] private Transform eyesRoot;

    [Header("クリック")]
    [Tooltip("目からこの距離(m)までのマグを持てる。0.8 なら自分の席の前のマグだけ")]
    [SerializeField] private float reach = 0.8f;
    [Tooltip("押してから離すまでにマウスがこれ以上(px)動いたら、視点ドラッグとみなしてクリックにしない")]
    [SerializeField] private float clickMoveTolerance = 10f;

    [Header("持ち方 (目を原点、顔の正面を +Z とした位置。上下の視線には付いていかない)")]
    [Tooltip("手に持っているときの位置(右下)")]
    [SerializeField] private Vector3 holdOffset = new Vector3(0.15f, -0.18f, 0.38f);
    [Tooltip("口の位置。飲んでいる間、マグの縁をここに当てる。前に出しすぎると空のマグの中が見える")]
    [SerializeField] private Vector3 lipOffset = new Vector3(0f, -0.09f, 0.09f);
    [Tooltip("他のプレイヤーから見たときの口の位置。体(半径 0.225)にめり込まないよう前に出す")]
    [SerializeField] private Vector3 remoteLipOffset = new Vector3(0f, -0.12f, 0.26f);
    [Tooltip("マグの高さ(m)。原点(底)から縁までの距離")]
    [SerializeField] private float mugHeight = 0.117f;
    [Tooltip("口を付けたときの傾き(度)。小さいとマグの中が見える")]
    [SerializeField] private float sipStartTilt = 75f;
    [Tooltip("飲み干すときの傾き(度)。縁を口に当てたまま傾けるので、大きいほど底が持ち上がって画面に入ってくる")]
    [SerializeField] private float sipEndTilt = 105f;
    [Tooltip("取っ手を右手側に向けるための、マグ自身の向きの補正(度)")]
    [SerializeField] private float handleYaw = 180f;

    [Header("時間(秒)")]
    [SerializeField] private float liftDuration = 0.5f;
    [SerializeField] private float raiseDuration = 0.5f;
    [SerializeField] private float sipDuration = 1.0f;
    [SerializeField] private float lowerDuration = 0.4f;
    [SerializeField] private float putDownDuration = 0.5f;

    private DrinkableMug current;     // 今持っているマグ。持っていなければ null
    private bool interrupted;         // シーン切り替えなどで途中でやめたら true
    private Vector2 pressPosition;
    private bool pressedOnUI;

    private bool ViewActive
    {
        get { return playerCamera != null && playerCamera.enabled; }
    }

    /// <summary>自分は自分のカメラ、他人は目を基準に動かす。</summary>
    private Transform Head
    {
        get { return IsOwner ? playerCamera.transform : eyesRoot; }
    }

    /// <summary>動かし続けてよいか。自分はカメラが止まったら(ロビー以外)やめる。他人はマグが消えたらやめる(Tween 側で見る)。</summary>
    private bool CanAnimate
    {
        get { return IsOwner ? ViewActive : eyesRoot != null; }
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner || current != null || !ViewActive) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            pressPosition = mouse.position.ReadValue();
            pressedOnUI = IsPointerOverUI();
        }

        if (!mouse.leftButton.wasReleasedThisFrame || pressedOnUI) return;

        Vector2 releasePosition = mouse.position.ReadValue();
        if ((releasePosition - pressPosition).sqrMagnitude > clickMoveTolerance * clickMoveTolerance) return;

        Ray ray = playerCamera.ScreenPointToRay(releasePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, reach)) return;

        var mug = hit.collider.GetComponentInParent<DrinkableMug>();
        if (mug == null || mug.InUse) return;

        StartCoroutine(DrinkRoutine(mug));
    }

    public override void OnNetworkSpawn()
    {
        DrinkingMugId.OnValueChanged += HandleDrinkingMugChanged;
    }

    public override void OnNetworkDespawn()
    {
        DrinkingMugId.OnValueChanged -= HandleDrinkingMugChanged;
        ReleaseMug();
    }

    private void OnDisable()
    {
        ReleaseMug();
    }

    /// <summary>
    /// 他のプレイヤーが飲み始めたら、こちらの画面でも同じ動きを再生する。-1 になったら(飲み終わり・中断)マグを戻す。
    /// 同じマグを同時に取り合ったときは、先にこちらで動いているほうを優先する。
    /// </summary>
    private void HandleDrinkingMugChanged(int previous, int next)
    {
        if (IsOwner) return;

        ReleaseMug();
        var mug = DrinkableMug.FromId(next);
        if (mug == null || mug.InUse) return;
        StartCoroutine(DrinkRoutine(mug));
    }

    private IEnumerator DrinkRoutine(DrinkableMug mug)
    {
        current = mug;
        interrupted = false;
        mug.InUse = true;
        if (IsOwner) DrinkingMugId.Value = mug.Id;

        var home = new Pose(mug.HomePosition, mug.HomeRotation);
        Func<Pose> atHome = () => home;
        Func<Pose> inHand = () => HeadPose(holdOffset, 0f);
        Func<Pose> sipStart = () => MouthPose(sipStartTilt);
        Func<Pose> sipEnd = () => MouthPose(sipEndTilt);

        yield return Tween(mug, atHome, inHand, liftDuration);
        yield return Tween(mug, inHand, sipStart, raiseDuration);
        yield return Tween(mug, sipStart, sipEnd, sipDuration);

        if (IsOwner && !interrupted && DrunkMeter.Instance != null) DrunkMeter.Instance.Drink();

        yield return Tween(mug, sipEnd, inHand, lowerDuration);
        yield return Tween(mug, inHand, atHome, putDownDuration);

        StopDrinking();
    }

    /// <summary>
    /// マグを from から to へ滑らかに動かす。目の位置は毎フレーム変わるので、両端の姿勢も毎フレーム計算し直す。
    /// マグが消えた・カメラが止まったときは interrupted を立てて、以降の Tween も何もしない。
    /// </summary>
    private IEnumerator Tween(DrinkableMug mug, Func<Pose> from, Func<Pose> to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            if (interrupted || mug == null || !CanAnimate)
            {
                interrupted = true;
                yield break;
            }

            float k = Mathf.SmoothStep(0f, 1f, t / duration);
            Pose a = from();
            Pose b = to();
            mug.transform.SetPositionAndRotation(
                Vector3.Lerp(a.position, b.position, k),
                Quaternion.Slerp(a.rotation, b.rotation, k));
            yield return null;
        }

        if (interrupted || mug == null) yield break;
        Pose end = to();
        mug.transform.SetPositionAndRotation(end.position, end.rotation);
    }

    /// <summary>
    /// 目を原点にした姿勢を返す。顔の左右の向きにだけ付いていき、上下の視線は無視する
    /// (下を向いても口の位置は変わらないので)。tilt はマグの口を顔の側へ傾ける角度。
    /// </summary>
    private Pose HeadPose(Vector3 offset, float tilt)
    {
        Transform head = Head;
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
        Quaternion frame = Quaternion.LookRotation(forward, Vector3.up);

        Vector3 position = head.position + frame * offset;
        Quaternion rotation = frame * Quaternion.Euler(-tilt, 0f, 0f) * Quaternion.Euler(0f, handleYaw, 0f);
        return new Pose(position, rotation);
    }

    /// <summary>口元で飲んでいる姿勢。マグの縁が口に来るよう、傾けた分だけ底(原点)の位置をずらす。</summary>
    private Pose MouthPose(float tilt)
    {
        Pose pose = HeadPose(IsOwner ? lipOffset : remoteLipOffset, tilt);
        pose.position -= pose.rotation * (Vector3.up * mugHeight);
        return pose;
    }

    /// <summary>飲み終わり・中断のとき。マグを戻し、他のプレイヤーにも飲み終わったことを伝える。</summary>
    private void StopDrinking()
    {
        ReleaseMug();
        if (IsOwner && IsSpawned && DrinkingMugId.Value != NoMug) DrinkingMugId.Value = NoMug;
    }

    /// <summary>途中でも終わりでも、持っているマグをテーブルに戻す(この端末の中だけ)。</summary>
    private void ReleaseMug()
    {
        StopAllCoroutines();
        if (current != null) current.ReturnHome();
        current = null;
    }

    private bool IsPointerOverUI()
    {
        var es = EventSystem.current;
        return es != null && es.IsPointerOverGameObject();
    }
}

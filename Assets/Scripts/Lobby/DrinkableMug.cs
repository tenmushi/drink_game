using UnityEngine;

/// <summary>
/// ロビーのマグ(Mug プレハブ)に付ける。クリックされると MugInteractor が持ち上げて飲む動作をさせる。
/// 置いてあった位置を覚えておき、飲み終わったらそこへ戻す。
/// 当たり判定はプレハブの MeshCollider をそのまま使う。
/// </summary>
public class DrinkableMug : MonoBehaviour
{
    /// <summary>テーブルに置いてあったときの位置。</summary>
    public Vector3 HomePosition { get; private set; }

    /// <summary>テーブルに置いてあったときの向き。</summary>
    public Quaternion HomeRotation { get; private set; }

    /// <summary>誰かが手に持っている間は true。二重に持たれないようにする。</summary>
    public bool InUse { get; set; }

    private void Awake()
    {
        HomePosition = transform.position;
        HomeRotation = transform.rotation;
    }

    /// <summary>テーブルの元の位置に戻して、手を離す。</summary>
    public void ReturnHome()
    {
        transform.SetPositionAndRotation(HomePosition, HomeRotation);
        InUse = false;
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ロビーのマグ(Mug プレハブ)に付ける。クリックされると MugInteractor が持ち上げて飲む動作をさせる。
/// 置いてあった位置を覚えておき、飲み終わったらそこへ戻す。
/// 当たり判定はプレハブの MeshCollider をそのまま使う。
///
/// マグは NetworkObject ではないので、どのマグを飲んでいるかは Id(番号)で他のプレイヤーに伝える。
/// Id は置いてあった位置の順番で決めるので、同じシーンならどの端末でも同じマグが同じ番号になる。
/// </summary>
public class DrinkableMug : MonoBehaviour
{
    private static readonly List<DrinkableMug> all = new List<DrinkableMug>();
    private static bool sorted;

    /// <summary>テーブルに置いてあったときの位置。</summary>
    public Vector3 HomePosition { get; private set; }

    /// <summary>テーブルに置いてあったときの向き。</summary>
    public Quaternion HomeRotation { get; private set; }

    /// <summary>誰かが手に持っている間は true。二重に持たれないようにする。</summary>
    public bool InUse { get; set; }

    /// <summary>全端末で共通の番号。ネットワーク越しにマグを指すのに使う。</summary>
    public int Id
    {
        get
        {
            SortIfNeeded();
            return all.IndexOf(this);
        }
    }

    /// <summary>Id からマグを探す。今のシーンにそのマグがなければ null。</summary>
    public static DrinkableMug FromId(int id)
    {
        SortIfNeeded();
        return id >= 0 && id < all.Count ? all[id] : null;
    }

    private void Awake()
    {
        HomePosition = transform.position;
        HomeRotation = transform.rotation;
    }

    private void OnEnable()
    {
        all.Add(this);
        sorted = false;
    }

    private void OnDisable()
    {
        all.Remove(this);
        sorted = false;
    }

    /// <summary>テーブルの元の位置に戻して、手を離す。</summary>
    public void ReturnHome()
    {
        transform.SetPositionAndRotation(HomePosition, HomeRotation);
        InUse = false;
    }

    /// <summary>置いてあった位置(x→y→z)の順に並べる。持ち上げても HomePosition は変わらないので順番は崩れない。</summary>
    private static void SortIfNeeded()
    {
        if (sorted) return;
        all.Sort((a, b) =>
        {
            int c = a.HomePosition.x.CompareTo(b.HomePosition.x);
            if (c == 0) c = a.HomePosition.y.CompareTo(b.HomePosition.y);
            if (c == 0) c = a.HomePosition.z.CompareTo(b.HomePosition.z);
            return c;
        });
        sorted = true;
    }
}

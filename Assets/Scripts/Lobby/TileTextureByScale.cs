using UnityEngine;

/// <summary>
/// オブジェクトのスケールに合わせてテクスチャの繰り返し回数を自動調整する。
/// 壁や床を Cube / Plane で作ったとき、伸ばしても模様の大きさが変わらなくなる。
///
/// MaterialPropertyBlock を使うのでマテリアルは増えない(1枚を使い回せる)。
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class TileTextureByScale : MonoBehaviour
{
    public enum Plane { XY, XZ, ZY }

    [Tooltip("1ワールド単位あたり、テクスチャを何回繰り返すか。0.5 なら 2m で 1 枚")]
    [SerializeField] private float tilesPerUnit = 0.5f;

    [Tooltip("どの面を基準にするか。壁(板)なら XY、床なら XZ")]
    [SerializeField] private Plane basePlane = Plane.XY;

    [Tooltip("URP Lit は _BaseMap。旧 Standard は _MainTex")]
    [SerializeField] private string texturePropertyName = "_BaseMap";

    private MaterialPropertyBlock block;
    private Vector3 lastScale;

    private void OnEnable()
    {
        lastScale = Vector3.zero;
        Apply();
    }

    private void Update()
    {
        // 編集中にスケールをいじったら追従する
        if (transform.lossyScale != lastScale) Apply();
    }

    private void OnValidate()
    {
        lastScale = Vector3.zero;
        Apply();
    }

    private void Apply()
    {
        var renderer = GetComponent<Renderer>();
        if (renderer == null) return;

        Vector3 s = transform.lossyScale;
        lastScale = s;

        float u, v;
        switch (basePlane)
        {
            case Plane.XZ: u = s.x; v = s.z; break;
            case Plane.ZY: u = s.z; v = s.y; break;
            default:       u = s.x; v = s.y; break;
        }

        // Plane プリミティブは 1 スケール = 10m なので補正
        u = Mathf.Max(0.01f, u * tilesPerUnit);
        v = Mathf.Max(0.01f, v * tilesPerUnit);

        if (block == null) block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        // _ST は (tilingX, tilingY, offsetX, offsetY)
        block.SetVector(texturePropertyName + "_ST", new Vector4(u, v, 0f, 0f));
        renderer.SetPropertyBlock(block);
    }
}

using UnityEngine;

/// <summary>
/// 머티리얼 텍스처 UV를 계속 이동시켜
/// 구름이 흐르는 것처럼 보이게 한다.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class CloudUVScroller : MonoBehaviour
{
    [Header("Scroll")]
    [SerializeField] private Vector2 scrollSpeed = new Vector2(-0.01f, -0.002f);

    [Header("Options")]
    [SerializeField] private bool useUnscaledTime = false;

    private Material runtimeMaterial;
    private Vector2 offset;

    void Awake()
    {
        Renderer rd = GetComponent<Renderer>();

        // sharedMaterial 말고 material을 써야 런타임 인스턴스가 생김
        runtimeMaterial = rd.material;
    }

    void Update()
    {
        if (runtimeMaterial == null)
            return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        offset += scrollSpeed * dt;

        // Built-in / Standard / Unlit
        if (runtimeMaterial.HasProperty("_MainTex"))
            runtimeMaterial.SetTextureOffset("_MainTex", offset);

        // URP
        if (runtimeMaterial.HasProperty("_BaseMap"))
            runtimeMaterial.SetTextureOffset("_BaseMap", offset);
    }
}
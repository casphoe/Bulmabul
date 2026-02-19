using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class DiceClickRevealFX : MonoBehaviour
{
    [Header("Target UI (보통 이 오브젝트의 Image/RectTransform)")]
    public RectTransform targetRect;
    public Image targetImage;

    [Header("Crack Overlay Image (위에 덮는 Image)")]
    public Image crackOverlay; // 투명 PNG 넣을 Image
    public List<Sprite> crackFrames; // crack_01~03 스프라이트

    [Header("Particle (선택)")]
    public ParticleSystem burstParticle;

    [Header("Idle 흔들림(더블클릭 전)")]
    public float idleAngle = 30f;
    public float idleSpeed = 6f;

    [Header("DoubleClick 연출")]
    public Vector2 bigSize = new Vector2(800, 800);
    public float scaleUpTime = 0.16f;
    public float crackFrameInterval = 0.08f;
    public float afterTime = 0.15f;

    bool _idle;
    float _t;
    Vector2 _originSize;
    bool _originCaptured;

    void Reset()
    {
        targetRect = GetComponent<RectTransform>();
        targetImage = GetComponent<Image>();
    }

    void Awake()
    {
        if (targetRect == null) targetRect = GetComponent<RectTransform>();
        if (targetImage == null) targetImage = GetComponent<Image>();

        CaptureOriginIfNeeded();
        ResetForNewPull(); // 시작 시도 깨끗하게
    }

    void CaptureOriginIfNeeded()
    {
        if (_originCaptured) return;
        if (targetRect != null)
        {
            _originSize = targetRect.sizeDelta;
            _originCaptured = true;
        }
    }

    void Update()
    {
        if (!_idle || targetRect == null) return;

        _t += Time.unscaledDeltaTime * idleSpeed;
        float a = Mathf.Sin(_t) * idleAngle;
        targetRect.localRotation = Quaternion.Euler(0, 0, a);
    }

    /// <summary>
    /// 다음 뽑기(확인창 열릴 때)마다 호출해서 상태 초기화
    /// </summary>
    public void ResetForNewPull()
    {
        CaptureOriginIfNeeded();

        _idle = false;
        _t = 0f;

        if (targetRect != null)
        {
            targetRect.localRotation = Quaternion.identity;
            if (_originCaptured) targetRect.sizeDelta = _originSize; // 크기 원복
        }

        if (crackOverlay != null)
        {
            crackOverlay.sprite = null;
            crackOverlay.gameObject.SetActive(false);
        }

        if (burstParticle != null)
        {
            burstParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    public void BeginIdle()
    {
        CaptureOriginIfNeeded();

        // 혹시 이전 연출 잔여가 남아있을 수 있으니 회전/크랙 정리
        if (crackOverlay != null) crackOverlay.gameObject.SetActive(false);
        _idle = true;
        _t = 0;
    }

    public void EndIdle()
    {
        _idle = false;
        if (targetRect != null)
            targetRect.localRotation = Quaternion.identity;
    }

    public async Task PlayCrackAndBurstAsync()
    {
        EndIdle();

        if (targetRect == null) return;

        // 1) 800x800 확대
        Vector2 from = targetRect.sizeDelta;
        Vector2 to = bigSize;

        float t = 0f;
        while (t < scaleUpTime)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / scaleUpTime);

            k = 1f - Mathf.Pow(1f - k, 3f);

            targetRect.sizeDelta = Vector2.LerpUnclamped(from, to, k);
            await Task.Yield();
        }
        targetRect.sizeDelta = to;

        // 2) crack 오버레이 표시
        if (crackOverlay != null)
        {
            crackOverlay.gameObject.SetActive(true);
            crackOverlay.sprite = null;
            crackOverlay.color = Color.white;
        }

        // 3) crack 프레임 재생
        if (crackOverlay != null && crackFrames != null && crackFrames.Count > 0)
        {
            for (int i = 0; i < crackFrames.Count; i++)
            {
                crackOverlay.sprite = crackFrames[i];
                await WaitUnscaled(crackFrameInterval);
            }
        }

        // 4) 파티클
        if (burstParticle != null)
        {
            burstParticle.Play(true);
        }

        await WaitUnscaled(afterTime);

        // crackOverlay는 결과창으로 넘어갈 거라면 끄고,
        // 계속 보여주고 싶으면 끄지 말고 유지해도 됨.
        if (crackOverlay != null)
            crackOverlay.gameObject.SetActive(false);
    }

    static async Task WaitUnscaled(float sec)
    {
        float t = 0f;
        while (t < sec)
        {
            t += Time.unscaledDeltaTime;
            await Task.Yield();
        }
    }
}

using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 주사위 굴림 연출 UI.
/// 
/// 연출 흐름:
/// 1. 주사위가 위로 튀어오름
/// 2. 회전하면서 아래로 내려옴
/// 3. 내려오는 동안 주사위 이미지가 빠르게 변경됨
/// 4. 최종 dice1, dice2 결과 이미지로 멈춤
/// 
/// 주의:
/// - 실제 주사위 결과는 여기서 정하지 않는다.
/// - BulmabulGameState가 결정한 결과를 받아서 보여주기만 한다.
/// - 주사위 등급은 장착 주사위 Grade 기준으로 색상만 변경한다.
/// </summary>
public class DiceRollingUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Dice Images")]
    [SerializeField] private Image imgDiceLeft;
    [SerializeField] private Image imgDiceRight;

    [Header("Result Text")]
    [SerializeField] private TMP_Text txtDiceResult;

    [Header("Sprites")]
    [Tooltip("인덱스 0~5 = 주사위 1~6 공통 이미지")]
    [SerializeField] private Sprite[] diceSprites = new Sprite[6];

    [Header("Motion Settings")]
    [Tooltip("주사위가 위로 튀어오르는 높이")]
    [SerializeField] private float jumpHeight = 260f;

    [Tooltip("위로 올라가는 시간")]
    [SerializeField] private float jumpUpTime = 0.22f;

    [Tooltip("아래로 내려오는 시간")]
    [SerializeField] private float fallDownTime = 0.55f;

    [Tooltip("최종 위치에 도착 후 살짝 튕기는 시간")]
    [SerializeField] private float settleTime = 0.18f;

    [Tooltip("굴리는 중 주사위 이미지가 바뀌는 간격")]
    [SerializeField] private float rollingInterval = 0.05f;

    [Tooltip("결과가 빨리 와도 최소 이 시간만큼은 연출을 보여준다")]
    [SerializeField] private float minRollingTime = 1.1f;

    [Tooltip("결과 표시 후 패널이 닫히기까지 시간")]
    [SerializeField] private float resultShowTime = 0.9f;

    [Tooltip("왼쪽 주사위 회전 속도")]
    [SerializeField] private float leftRotateSpeed = 900f;

    [Tooltip("오른쪽 주사위 회전 속도")]
    [SerializeField] private float rightRotateSpeed = -900f;

    private Coroutine _rollingRoutine;

    private bool _isWaitingForResult;
    private float _rollingElapsed;

    private DiceGrade _currentDiceGrade = DiceGrade.Common;

    private RainbowImageEffect _leftRainbow;
    private RainbowImageEffect _rightRainbow;

    private RectTransform _leftRect;
    private RectTransform _rightRect;

    private Vector2 _leftOriginPos;
    private Vector2 _rightOriginPos;

    private Quaternion _leftOriginRot;
    private Quaternion _rightOriginRot;

    /// <summary>
    /// GameState가 Pawn 이동을 언제 시작해야 하는지 계산할 때 사용하는 전체 주사위 연출 시간.
    /// 최소 굴림 시간 + 내려오는 시간 + 멈춤 시간 + 결과 표시 시간을 포함한다.
    /// </summary>
    public float TotalPresentationSeconds
    {
        get
        {
            return minRollingTime + fallDownTime + settleTime + resultShowTime + 0.15f;
        }
    }

    private void Awake()
    {
        if (imgDiceLeft != null)
            _leftRect = imgDiceLeft.rectTransform;

        if (imgDiceRight != null)
            _rightRect = imgDiceRight.rectTransform;

        SaveOriginTransform();

        if (root != null)
            root.SetActive(false);

        ApplyDiceGradeColor();
    }

    /// <summary>
    /// 현재 위치/회전값 저장.
    /// 인스펙터에서 배치한 ImgDiceLeft / ImgDiceRight 위치가 최종 도착 위치가 된다.
    /// </summary>
    private void SaveOriginTransform()
    {
        if (_leftRect != null)
        {
            _leftOriginPos = _leftRect.anchoredPosition;
            _leftOriginRot = _leftRect.localRotation;
        }

        if (_rightRect != null)
        {
            _rightOriginPos = _rightRect.anchoredPosition;
            _rightOriginRot = _rightRect.localRotation;
        }
    }

    /// <summary>
    /// 장착 중인 주사위 정보를 받아서 굴림 연출 시작.
    /// </summary>
    public void ShowRolling(OwnedDice equippedDice)
    {
        SetCurrentDiceGrade(equippedDice);

        if (_rollingRoutine != null)
            StopCoroutine(_rollingRoutine);

        _rollingRoutine = StartCoroutine(CoShowRolling());
    }

    /// <summary>
    /// 장착 주사위 정보가 없으면 Common 기준으로 굴림.
    /// </summary>
    public void ShowRolling()
    {
        ShowRolling(null);
    }

    private IEnumerator CoShowRolling()
    {
        _isWaitingForResult = true;
        _rollingElapsed = 0f;

        if (root != null)
            root.SetActive(true);

        ResetDiceTransform();
        ApplyDiceGradeColor();

        if (txtDiceResult != null)
        {
            txtDiceResult.text = GetByLanguage(
                "주사위를 굴리는 중...",
                "Rolling the dice..."
            );
        }

        // 1단계: 위로 튀어오름
        yield return MoveDiceUp();

        // 2단계: 결과가 올 때까지 회전 + 랜덤 면 변경
        while (_isWaitingForResult)
        {
            SetRandomFaces();
            RotateDiceOnce(rollingInterval);

            _rollingElapsed += rollingInterval;
            yield return new WaitForSeconds(rollingInterval);
        }
    }

    /// <summary>
    /// 최종 결과 표시.
    /// </summary>
    public void StopWithResult(int leftValue, int rightValue, OwnedDice equippedDice, Action onComplete = null)
    {
        SetCurrentDiceGrade(equippedDice);

        if (_rollingRoutine != null)
            StopCoroutine(_rollingRoutine);

        _rollingRoutine = StartCoroutine(CoStopWithResult(leftValue, rightValue, onComplete));
    }

    public void StopWithResult(int leftValue, int rightValue, Action onComplete = null)
    {
        StopWithResult(leftValue, rightValue, null, onComplete);
    }

    private IEnumerator CoStopWithResult(int leftValue, int rightValue, Action onComplete)
    {
        if (root != null)
            root.SetActive(true);

        ApplyDiceGradeColor();

        // 결과가 너무 빨리 도착해도 최소 시간만큼은 랜덤으로 굴림 표시
        float remain = Mathf.Max(0f, minRollingTime - _rollingElapsed);

        while (remain > 0f)
        {
            SetRandomFaces();
            RotateDiceOnce(rollingInterval);

            float wait = Mathf.Min(rollingInterval, remain);
            remain -= wait;

            yield return new WaitForSeconds(wait);
        }

        // 3단계: 위에서 아래 최종 위치까지 내려오면서 굴러가는 느낌
        yield return MoveDiceDownWithRolling();

        _isWaitingForResult = false;

        // 4단계: 최종 결과 이미지로 고정
        SetFace(imgDiceLeft, leftValue);
        SetFace(imgDiceRight, rightValue);
        ApplyDiceGradeColor();

        // 5단계: 살짝 튕기면서 멈춤
        yield return SettleDice();

        int total = leftValue + rightValue;

        if (txtDiceResult != null)
        {
            txtDiceResult.text = GetByLanguage(
                $"{leftValue} + {rightValue} = {total}",
                $"{leftValue} + {rightValue} = {total}"
            );
        }

        yield return new WaitForSeconds(resultShowTime);

        if (root != null)
            root.SetActive(false);

        ResetDiceTransform();

        _rollingRoutine = null;
        onComplete?.Invoke();
    }

    /// <summary>
    /// 주사위가 위로 올라가는 연출.
    /// </summary>
    private IEnumerator MoveDiceUp()
    {
        Vector2 leftStart = _leftOriginPos;
        Vector2 rightStart = _rightOriginPos;

        Vector2 leftEnd = _leftOriginPos + Vector2.up * jumpHeight;
        Vector2 rightEnd = _rightOriginPos + Vector2.up * jumpHeight;

        float elapsed = 0f;

        while (elapsed < jumpUpTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / jumpUpTime);

            // EaseOut 느낌
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            if (_leftRect != null)
                _leftRect.anchoredPosition = Vector2.Lerp(leftStart, leftEnd, eased);

            if (_rightRect != null)
                _rightRect.anchoredPosition = Vector2.Lerp(rightStart, rightEnd, eased);

            SetRandomFaces();
            RotateDiceOnce(Time.deltaTime);

            _rollingElapsed += Time.deltaTime;

            yield return null;
        }
    }

    /// <summary>
    /// 주사위가 아래로 내려오면서 굴러가는 연출.
    /// </summary>
    private IEnumerator MoveDiceDownWithRolling()
    {
        Vector2 leftStart = _leftOriginPos + Vector2.up * jumpHeight;
        Vector2 rightStart = _rightOriginPos + Vector2.up * jumpHeight;

        Vector2 leftEnd = _leftOriginPos;
        Vector2 rightEnd = _rightOriginPos;

        float elapsed = 0f;
        float faceTimer = 0f;

        while (elapsed < fallDownTime)
        {
            elapsed += Time.deltaTime;
            faceTimer += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / fallDownTime);

            // EaseIn 느낌. 처음에는 천천히, 아래로 갈수록 빨라짐
            float eased = t * t;

            if (_leftRect != null)
                _leftRect.anchoredPosition = Vector2.Lerp(leftStart, leftEnd, eased);

            if (_rightRect != null)
                _rightRect.anchoredPosition = Vector2.Lerp(rightStart, rightEnd, eased);

            if (faceTimer >= rollingInterval)
            {
                faceTimer = 0f;
                SetRandomFaces();
            }

            RotateDiceOnce(Time.deltaTime);

            yield return null;
        }

        if (_leftRect != null)
            _leftRect.anchoredPosition = _leftOriginPos;

        if (_rightRect != null)
            _rightRect.anchoredPosition = _rightOriginPos;
    }

    /// <summary>
    /// 최종 위치에서 살짝 튕기면서 멈추는 연출.
    /// </summary>
    private IEnumerator SettleDice()
    {
        Vector2 leftStart = _leftOriginPos + Vector2.down * 24f;
        Vector2 rightStart = _rightOriginPos + Vector2.down * 24f;

        float elapsed = 0f;

        while (elapsed < settleTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / settleTime);

            // 아래에서 원위치로 살짝 올라오며 멈춤
            float eased = 1f - Mathf.Pow(1f - t, 2f);

            if (_leftRect != null)
                _leftRect.anchoredPosition = Vector2.Lerp(leftStart, _leftOriginPos, eased);

            if (_rightRect != null)
                _rightRect.anchoredPosition = Vector2.Lerp(rightStart, _rightOriginPos, eased);

            yield return null;
        }

        if (_leftRect != null)
        {
            _leftRect.anchoredPosition = _leftOriginPos;
            _leftRect.localRotation = _leftOriginRot;
        }

        if (_rightRect != null)
        {
            _rightRect.anchoredPosition = _rightOriginPos;
            _rightRect.localRotation = _rightOriginRot;
        }
    }

    /// <summary>
    /// 한 프레임 또는 짧은 시간만큼 주사위를 회전시킨다.
    /// </summary>
    private void RotateDiceOnce(float deltaTime)
    {
        if (_leftRect != null)
            _leftRect.Rotate(0f, 0f, leftRotateSpeed * deltaTime);

        if (_rightRect != null)
            _rightRect.Rotate(0f, 0f, rightRotateSpeed * deltaTime);
    }

    /// <summary>
    /// 주사위 위치/회전 초기화.
    /// </summary>
    private void ResetDiceTransform()
    {
        if (_leftRect != null)
        {
            _leftRect.anchoredPosition = _leftOriginPos;
            _leftRect.localRotation = _leftOriginRot;
        }

        if (_rightRect != null)
        {
            _rightRect.anchoredPosition = _rightOriginPos;
            _rightRect.localRotation = _rightOriginRot;
        }
    }

    /// <summary>
    /// 강제로 닫기.
    /// </summary>
    public void HideImmediately()
    {
        _isWaitingForResult = false;

        if (_rollingRoutine != null)
        {
            StopCoroutine(_rollingRoutine);
            _rollingRoutine = null;
        }

        ResetDiceTransform();

        if (root != null)
            root.SetActive(false);
    }

    private void SetCurrentDiceGrade(OwnedDice equippedDice)
    {
        if (equippedDice != null)
            _currentDiceGrade = equippedDice.Grade;
        else
            _currentDiceGrade = DiceGrade.Common;
    }

    /// <summary>
    /// 등급에 따른 색상 적용.
    /// 스프라이트는 동일하고, 색상만 주사위 등급에 따라 바뀐다.
    /// </summary>
    private void ApplyDiceGradeColor()
    {
        DiceGradeColorUtil.Apply(imgDiceLeft, _currentDiceGrade, ref _leftRainbow);
        DiceGradeColorUtil.Apply(imgDiceRight, _currentDiceGrade, ref _rightRainbow);
    }

    private void SetRandomFaces()
    {
        int left = UnityEngine.Random.Range(1, 7);
        int right = UnityEngine.Random.Range(1, 7);

        SetFace(imgDiceLeft, left);
        SetFace(imgDiceRight, right);

        ApplyDiceGradeColor();
    }

    private void SetFace(Image img, int value)
    {
        if (img == null)
            return;

        int index = Mathf.Clamp(value - 1, 0, 5);

        if (diceSprites == null || diceSprites.Length < 6 || diceSprites[index] == null)
            return;

        img.sprite = diceSprites[index];
    }

    private string GetByLanguage(string kor, string eng)
    {
        if (LaguageManager.Instance == null)
            return kor;

        return LaguageManager.Instance.currentLang == Lauaguage.Eng ? eng : kor;
    }
}
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Pawn 위에 떠오르는 알림 텍스트를 표시하는 전용 스크립트.
/// 한국어/영어 언어 설정을 자동으로 반영한다.
/// </summary>
public class PawnFloatingText : MonoBehaviour
{
    public enum PawnFloatingTextType
    {
        Normal,
        MoneyPlus,
        MoneyMinus,
        Danger,
        Card,
        BuyOrTakeOver
    }

    [Header("Anchor")]
    [Tooltip("Pawn 기준 알림 텍스트 위치. 플레이어 이름보다 위에 두려면 Y를 높이면 됨.")]
    public Vector3 localOffset = new Vector3(0f, 3.0f, 0f);

    [Header("Text")]
    public TMP_FontAsset font;
    public float fontSize = 2.6f;
    public float textWidth = 8f;
    public float textHeight = 1.5f;

    [Header("Animation")]
    public float duration = 1.15f;
    public float riseDistance = 1.2f;
    public float stackGap = 0.35f;
    public bool lookAtCamera = true;

    [Header("Sorting")]
    public int sortingOrder = 300;

    private Transform anchor;
    private int stackCount;

    private void Awake()
    {
        CreateAnchorIfNeeded();
    }

    private void CreateAnchorIfNeeded()
    {
        if (anchor != null)
            return;

        GameObject anchorObj = new GameObject("FloatingTextAnchor");
        anchorObj.transform.SetParent(transform);
        anchorObj.transform.localPosition = localOffset;
        anchorObj.transform.localRotation = Quaternion.identity;
        anchorObj.transform.localScale = Vector3.one;

        anchor = anchorObj.transform;
    }

    private string GetByLanguage(string kor, string eng)
    {
        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        return lang == Lauaguage.Kor ? kor : eng;
    }

    public void ShowMoveText(int moveCount)
    {
        ShowText(
            $"이동 {moveCount}칸",
            $"Move {moveCount} spaces",
            PawnFloatingTextType.Normal
        );
    }

    public void ShowMoneyText(int amount)
    {
        if (amount > 0)
        {
            ShowText(
                $"+{amount:N0}",
                $"+{amount:N0}",
                PawnFloatingTextType.MoneyPlus
            );
        }
        else if (amount < 0)
        {
            ShowText(
                $"{amount:N0}",
                $"{amount:N0}",
                PawnFloatingTextType.MoneyMinus
            );
        }
    }

    public void ShowJailText()
    {
        ShowText(
            "감옥에 갇힘",
            "Sent to Jail",
            PawnFloatingTextType.Danger
        );
    }

    public void ShowJailEscapeText()
    {
        ShowText(
            "감옥 탈출",
            "Escaped Jail",
            PawnFloatingTextType.Card
        );
    }

    public void ShowAngelCardText()
    {
        ShowText(
            "천사 카드 획득",
            "Angel Card Acquired",
            PawnFloatingTextType.Card
        );
    }

    public void ShowAngelCardUsedText()
    {
        ShowText(
            "천사 카드 사용",
            "Angel Card Used",
            PawnFloatingTextType.Card
        );
    }

    public void ShowJailEscapeCardText()
    {
        ShowText(
            "감옥 탈출 카드 획득",
            "Jail Escape Card Acquired",
            PawnFloatingTextType.Card
        );
    }

    public void ShowTravelCardText()
    {
        ShowText(
            "여행 카드 획득",
            "Travel Card Acquired",
            PawnFloatingTextType.Card
        );
    }

    public void ShowBuyText(string landNameKor, string landNameEng)
    {
        string landName = GetByLanguage(landNameKor, landNameEng);

        ShowText(
            $"{landName} 구매",
            $"Bought {landName}",
            PawnFloatingTextType.BuyOrTakeOver
        );
    }

    public void ShowTakeOverText(string landNameKor, string landNameEng)
    {
        string landName = GetByLanguage(landNameKor, landNameEng);

        ShowText(
            $"{landName} 인수",
            $"Took over {landName}",
            PawnFloatingTextType.BuyOrTakeOver
        );
    }

    public void ShowInfoText(string korMessage, string engMessage, PawnFloatingTextType type = PawnFloatingTextType.Normal)
    {
        ShowText(korMessage, engMessage, type);
    }

    /// <summary>
    /// 한국어/영어를 같이 넘기면 현재 언어에 맞게 출력된다.
    /// </summary>
    public void ShowText(string korMessage, string engMessage, PawnFloatingTextType type = PawnFloatingTextType.Normal)
    {
        string message = GetByLanguage(korMessage, engMessage);
        ShowText(message, type);
    }

    /// <summary>
    /// 이미 언어 처리가 끝난 단일 문자열을 그대로 출력한다.
    /// 돈 표시처럼 언어가 필요 없는 경우 사용.
    /// </summary>
    public void ShowText(string message, PawnFloatingTextType type = PawnFloatingTextType.Normal)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        CreateAnchorIfNeeded();

        GameObject textObj = new GameObject("FloatingText");
        textObj.transform.SetParent(anchor);

        int currentStack = stackCount;
        stackCount++;

        textObj.transform.localPosition = new Vector3(0f, currentStack * stackGap, 0f);
        textObj.transform.localRotation = Quaternion.identity;
        textObj.transform.localScale = Vector3.one;

        TextMeshPro tmp = textObj.AddComponent<TextMeshPro>();

        if (font != null)
            tmp.font = font;

        tmp.text = message;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.rectTransform.sizeDelta = new Vector2(textWidth, textHeight);

        Color baseColor = GetColor(type);
        tmp.color = baseColor;

        MeshRenderer renderer = tmp.GetComponent<MeshRenderer>();

        if (renderer != null)
            renderer.sortingOrder = sortingOrder;

        StartCoroutine(CoFloatingText(tmp, textObj.transform, baseColor));
    }

    private IEnumerator CoFloatingText(TextMeshPro tmp, Transform textTransform, Color baseColor)
    {
        Vector3 startLocalPos = textTransform.localPosition;
        Vector3 endLocalPos = startLocalPos + new Vector3(0f, riseDistance, 0f);

        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;

            float t = Mathf.Clamp01(time / duration);

            if (textTransform != null)
            {
                textTransform.localPosition = Vector3.Lerp(startLocalPos, endLocalPos, t);

                if (lookAtCamera && Camera.main != null)
                    textTransform.forward = Camera.main.transform.forward;
            }

            if (tmp != null)
            {
                Color c = baseColor;
                c.a = 1f - t;
                tmp.color = c;
            }

            yield return null;
        }

        stackCount = Mathf.Max(0, stackCount - 1);

        if (textTransform != null)
            Destroy(textTransform.gameObject);
    }

    private Color GetColor(PawnFloatingTextType type)
    {
        switch (type)
        {
            case PawnFloatingTextType.MoneyPlus:
                return new Color(0.25f, 1f, 0.35f, 1f);

            case PawnFloatingTextType.MoneyMinus:
                return new Color(1f, 0.25f, 0.25f, 1f);

            case PawnFloatingTextType.Danger:
                return new Color(1f, 0.2f, 0.1f, 1f);

            case PawnFloatingTextType.Card:
                return new Color(0.45f, 0.8f, 1f, 1f);

            case PawnFloatingTextType.BuyOrTakeOver:
                return new Color(1f, 0.85f, 0.25f, 1f);

            default:
                return Color.white;
        }
    }
}
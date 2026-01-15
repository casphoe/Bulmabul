using Gpm.Ui;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ChatMessageItem : InfiniteScrollItem
{
    [Header("UI")]
    public Text txtNick;
    public Text txtMsg;
    public Text txtTime;

    [Header("Bubble Flip (말풍선 이미지만 반전)")]
    [SerializeField] private RectTransform bubbleBg;      // 꼬리 포함 말풍선 배경(Image)
    [SerializeField] private bool flipBubbleForOther = true; // 남(상대)일 때 말풍선 반대로?

    [Header("Typewriter")]
    [SerializeField] private float charsPerSecond = 40f; // 타이핑 속도
    [SerializeField] private bool typeOnlyWhenAnimateFlag = true;

    [Header("Bubble Style")]
    [SerializeField] private Image bubbleBgImage; // 말풍선 배경(Image). 없으면 bubbleBg에서 자동 탐색

    // 팀전 색 / 개인전 색
    [SerializeField] private Color soloBgColor = Color.white;
    [SerializeField] private Color soloTextColor = Color.black;

    [SerializeField] private Color redTeamBgColor = Color.red;
    [SerializeField] private Color blueTeamBgColor = Color.blue;
    [SerializeField] private Color teamTextColor = Color.white;

    private Coroutine _co;
    private string _currentId;

    //지금은 비활성이라 코루틴을 못 돌리는 경우를 대비해 예약
    private bool _pendingType;
    private string _pendingFull;
    private string _pendingId;

    // 이미 타이핑 끝난 메시지는 다시 리빌드/재사용되어도 즉시 출력되게
    private static readonly HashSet<string> _typedDone = new HashSet<string>();

    // scale 부호가 누적되지 않도록 "원래 스케일" 저장
    private Vector3 _bgBaseScale = Vector3.one;
    private Vector3 _nickBaseScale = Vector3.one;
    private Vector3 _msgBaseScale = Vector3.one;
    private Vector3 _timeBaseScale = Vector3.one;
    private bool _baseScaleCached;

    private void Awake()
    {
        bubbleBg = bubbleBgImage.GetComponent<RectTransform>();
    }

    public override void UpdateData(InfiniteScrollData scrollData)
    {
        base.UpdateData(scrollData);

        var d = (ChatMessageData)scrollData;

        if (txtNick != null) txtNick.text = d.nickname ?? "";
        if (txtTime != null) txtTime.text = d.timeText ?? "";

        // 0) 팀/개인전 스킨 적용
        ApplyBubbleStyle(d);

        // 1) 말풍선 이미지 반전 처리
        ApplyBubbleFlip(d.isMine);

        // 풀링 재사용 대비: 이전 코루틴 정리
        StopTyping();

        // 메시지가 바뀌었는지(재사용 아이템)
        _currentId = d.messageId ?? "";

        string full = d.message ?? "";

        // 애니메이션 조건:
        // 1) d.animate가 true (최신 메시지)
        // 2) 아직 타이핑 완료된 적이 없음
        bool shouldType =
            (!typeOnlyWhenAnimateFlag || d.animate) &&
            !_typedDone.Contains(_currentId) &&
            !string.IsNullOrEmpty(full);

        // 타이핑 안 할 거면 즉시 출력
        if (!shouldType)
        {
            _pendingType = false;
            if (txtMsg != null) txtMsg.text = full;
            return;
        }

        // 타이핑 해야 하는데, 지금 오브젝트가 inactive면 StartCoroutine 하면 터진다.
        // 그래서 “예약(pending)”해두고 OnEnable에서 시작한다.
        _pendingType = true;
        _pendingFull = full;
        _pendingId = _currentId;

        // 일단 텍스트 비워둠(원하면 full로 넣어도 됨)
        if (txtMsg != null) txtMsg.text = "";

        // 지금 활성 상태면 바로 시작
        TryStartPendingTyping();
    }

    #region 팀전 / 개인전 색 적용

    private void ApplyBubbleStyle(ChatMessageData d)
    {
        // 개인전(혹은 팀 정보 없음) → 흰 배경 + 검은 글씨
        if (!d.isTeamMode || d.team == TeamSide.None)
        {
            if (bubbleBgImage != null) bubbleBgImage.color = soloBgColor;
            SetAllTextColor(soloTextColor);
            return;
        }

        // 팀전 → 팀별 배경 + 흰 글씨
        if (bubbleBgImage != null)
        {
            if (d.team == TeamSide.Red) bubbleBgImage.color = redTeamBgColor;
            else if (d.team == TeamSide.Blue) bubbleBgImage.color = blueTeamBgColor;
            else bubbleBgImage.color = soloBgColor;
        }

        SetAllTextColor(teamTextColor);
    }

    private void SetAllTextColor(Color c)
    {
        if (txtNick != null) txtNick.color = c;
        if (txtMsg != null) txtMsg.color = c;
        if (txtTime != null) txtTime.color = c;
    }

    #endregion

    #region 말풍선 배경 뒤집는 기능
    /// <summary>
    /// 말풍선 배경만 뒤집고 텍스트는 다시 정상 방향으로 되돌림.
    /// - d.isMine == true : 내 말풍선은 기본(예: 오른쪽 꼬리)
    /// - d.isMine == false: 상대 말풍선은 반전(예: 왼쪽 꼬리)
    /// </summary>
    private void ApplyBubbleFlip(bool isMine)
    {
        if (bubbleBg == null) return;

        CacheBaseScalesOnce();

        // flip 조건:
        // - 상대일 때만 뒤집고 싶으면: flip = !isMine
        // - 반대로 하고 싶으면 여기만 바꾸면 됨
        bool flip = flipBubbleForOther && !isMine;

        //  배경은 X만 뒤집기
        var bg = _bgBaseScale;
        bg.x = Mathf.Abs(bg.x) * (flip ? -1f : 1f);
        bubbleBg.localScale = bg;

        //  텍스트들은 다시 정상 방향(항상 +X)
        if (txtNick != null)
        {
            var s = _nickBaseScale;
            s.x = Mathf.Abs(s.x);
            txtNick.rectTransform.localScale = s;
        }
        if (txtMsg != null)
        {
            var s = _msgBaseScale;
            s.x = Mathf.Abs(s.x);
            txtMsg.rectTransform.localScale = s;
        }
        if (txtTime != null)
        {
            var s = _timeBaseScale;
            s.x = Mathf.Abs(s.x);
            txtTime.rectTransform.localScale = s;
        }
    }

    private void CacheBaseScalesOnce()
    {
        if (_baseScaleCached) return;

        _bgBaseScale = bubbleBg != null ? bubbleBg.localScale : Vector3.one;
        _nickBaseScale = txtNick != null ? txtNick.rectTransform.localScale : Vector3.one;
        _msgBaseScale = txtMsg != null ? txtMsg.rectTransform.localScale : Vector3.one;
        _timeBaseScale = txtTime != null ? txtTime.rectTransform.localScale : Vector3.one;

        _baseScaleCached = true;
    }
    #endregion

    private void OnEnable()
    {
        // 풀링으로 인해 Enable 되는 타이밍에 “예약된 타이핑”이 있으면 시작
        TryStartPendingTyping();
    }

    private void TryStartPendingTyping()
    {
        // 예약 없음
        if (!_pendingType) return;

        // 비활성/비활성화 상태면 시작하면 안 됨
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy) return;

        // 이미 다른 코루틴 돌고 있으면 방어
        if (_co != null) return;

        // 안전하게 시작
        _co = StartCoroutine(CoType(_pendingFull, _pendingId));

        // 예약 해제(시작했으니)
        _pendingType = false;
        _pendingFull = null;
        _pendingId = null;
    }

    private IEnumerator CoType(string full, string id)
    {
        if (txtMsg == null) yield break;

        float delay = (charsPerSecond <= 0f) ? 0f : (1f / charsPerSecond);

        for (int i = 1; i <= full.Length; i++)
        {
            // 재사용 중이면 중단(다른 데이터가 들어옴)
            if (id != _currentId) yield break;

            // 중간에 비활성되면 안전 종료
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy) yield break;

            txtMsg.text = full.Substring(0, i);

            if (delay > 0f) yield return new WaitForSeconds(delay);
            else yield return null;
        }

        _typedDone.Add(id);
        _co = null;
    }

    private void StopTyping()
    {
        if (_co != null)
        {
            StopCoroutine(_co);
            _co = null;
        }
    }

    private void OnDisable()
    {
        StopTyping();
        // 예약은 유지해도 되고 지워도 됨 (나는 유지 추천: 다시 켜질 때 이어서 타이핑)
        // _pendingType = false;
    }
}

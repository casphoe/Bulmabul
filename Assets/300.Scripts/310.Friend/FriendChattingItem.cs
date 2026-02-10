using Gpm.Ui;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class FriendChattingItem : InfiniteScrollItem
{
    public RectTransform bubbleRoot;  // 버블 이미지 루트만!
    public Text txtMessage;
    public Text txtClock;
    public Text txtNick;

    [Header("Typing")]
    public bool typingEnabled = true;
    public float secPerChar = 0.02f;

    Coroutine _typingCo;
    string _boundMsgId;

    bool _pendingTyping;
    string _pendingFullText;
    string _pendingMsgId;

    public override void UpdateData(InfiniteScrollData scrollData)
    {
        base.UpdateData(scrollData);

        var d = (FriendChatMessageData)scrollData;

        // 셀 재사용될 때 이전 타이핑 중지
        StopTyping();


        _boundMsgId = d.msgId;

        float sx = d.isMine ? 1f : -1f;

        if (bubbleRoot)
        {
            var s = bubbleRoot.localScale;
            s.x = Mathf.Abs(s.x) * sx;
            bubbleRoot.localScale = s;
        }

        if (txtNick) txtNick.text = d.fromNick ?? "";

        if (txtClock)
        {
            // ts가 Unix seconds 기준이라면 보기 좋게 HH:mm로
            // (UTC로 저장했으면 ToLocalTime()이 한국시간으로 바꿔줌)
            if (d.ts > 0)
            {
                var dt = System.DateTimeOffset.FromUnixTimeSeconds(d.ts).ToLocalTime();
                txtClock.text = dt.ToString("yyyy-MM-dd HH:mm");
            }
            else
            {
                txtClock.text = "";
            }
        }

        if (txtMessage == null) return;

        string full = d.text ?? "";

        // 타이핑 없이 즉시 표시
        if (!typingEnabled || !d.useTyping)
        {
            _pendingTyping = false;
            txtMessage.text = full;
            return;
        }

        // 타이핑 시작
        txtMessage.text = "";
        if (isActiveAndEnabled && gameObject.activeInHierarchy)
        {
            _typingCo = StartCoroutine(CoType(txtMessage, full, _boundMsgId));
            _pendingTyping = false;
        }
        else
        {
            // 비활성 상태면 예약만 해두고, OnEnable에서 시작
            _pendingTyping = true;
            _pendingFullText = full;
            _pendingMsgId = _boundMsgId;
        }
    }

    private void OnEnable()
    {
        if (_pendingTyping && typingEnabled && isActiveAndEnabled && gameObject.activeInHierarchy)
        {
            StopTyping();
            txtMessage.text = "";
            _typingCo = StartCoroutine(CoType(txtMessage, _pendingFullText, _pendingMsgId));
            _pendingTyping = false;
        }
    }

    void StopTyping()
    {
        if (_typingCo != null)
        {
            StopCoroutine(_typingCo);
            _typingCo = null;
        }
    }

    IEnumerator CoType(Text target, string full, string msgIdAtStart)
    {
        // 한 글자씩 출력
        for (int i = 0; i < full.Length; i++)
        {
            // 셀 재사용되어 다른 msg로 바뀌었으면 중단
            if (_boundMsgId != msgIdAtStart) yield break;

            if (!isActiveAndEnabled || !gameObject.activeInHierarchy) yield break;

            target.text += full[i];
            yield return new WaitForSecondsRealtime(secPerChar);
        }

        _typingCo = null;
    }

    void OnDisable()
    {
        StopTyping();
        _boundMsgId = null;
    }
}

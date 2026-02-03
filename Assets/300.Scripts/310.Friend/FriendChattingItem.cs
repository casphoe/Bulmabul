using Gpm.Ui;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class FriendChattingItem : InfiniteScrollItem
{
    public RectTransform bubbleRoot;  // 버블 이미지 루트만!
    public Text txtMeesage;
    public Text txtClock;
    public Text txtck;

    [Header("Typing")]
    public bool typingEnabled = true;
    public float secPerChar = 0.02f;

    Coroutine _typingCo;
    string _boundMsgId;

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

        if (txtMeesage == null) return;

        string full = d.text ?? "";

        // 타이핑 안 쓰는 경우(히스토리/옵션 OFF) 즉시 표시
        if (!typingEnabled || !d.useTyping)
        {
            txtMeesage.text = full;
            return;
        }

        // 타이핑 시작
        txtMeesage.text = "";
        _typingCo = StartCoroutine(CoType(txtMeesage, full, _boundMsgId));
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

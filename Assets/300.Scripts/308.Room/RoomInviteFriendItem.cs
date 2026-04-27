using System;
using Gpm.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 친구 초대 목록에 들어갈 데이터
/// - GPM InfiniteScroll은 InfiniteScrollData를 기반으로 아이템을 생성/재사용함
/// </summary>
[Serializable]
public class RoomInviteFriendData : InfiniteScrollData
{
    public FriendRow friend;

    // 초대 버튼 클릭 콜백
    public Action<FriendRow, RoomInviteFriendItem> onClickInvite;
}

/// <summary>
/// 친구 초대 목록 아이템
/// - GPM InfiniteScrollItem을 상속받아 UpdateData에서 UI를 갱신함
/// </summary>
public class RoomInviteFriendItem : InfiniteScrollItem
{
    [Header("UI")]
    [SerializeField] private RawImage profileImage;
    [SerializeField] private TMP_Text txtNick;
    [SerializeField] private TMP_Text txtLevel;
    [SerializeField] private Button btnInvite;

    [Header("Default")]
    [SerializeField] private Texture defaultProfileTexture;

    private RoomInviteFriendData _data;
    private FriendRow _row;
    private string _boundUrl = "";

    /// <summary>
    /// GPM InfiniteScroll이 데이터를 넣어줄 때마다 호출됨
    /// </summary>
    public override void UpdateData(InfiniteScrollData scrollData)
    {
        base.UpdateData(scrollData);

        _data = scrollData as RoomInviteFriendData;

        if (_data == null || _data.friend == null)
        {
            ClearUI();
            return;
        }

        _row = _data.friend;

        // 현재 언어 가져오기
        var lang = LaguageManager.Instance != null
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        // 닉네임 표시
        // 한국어: 닉네임 : 홍길동
        // 영어: Nickname : Hong
        if (txtNick != null)
        {
            txtNick.text = lang == Lauaguage.Kor
                ? $"닉네임 : {_row.nick}"
                : $"Nickname : {_row.nick}";
        }

        // 레벨 표시
        // 한국어: 레벨 : 1
        // 영어: Level : 1
        if (txtLevel != null)
        {
            txtLevel.text = lang == Lauaguage.Kor
                ? $"레벨 : {_row.accountLevel}"
                : $"Level : {_row.accountLevel}";
        }

        // 초대 버튼 세팅
        if (btnInvite != null)
        {
            btnInvite.interactable = true;
            btnInvite.onClick.RemoveAllListeners();

            btnInvite.onClick.AddListener(() =>
            {
                _data.onClickInvite?.Invoke(_row, this);
            });           
        }

        // 프로필 이미지 적용
        ApplyProfile(_row.photoUrl);
    }

    /// <summary>
    /// 초대 성공 후 버튼 비활성화
    /// </summary>
    public void SetInvited()
    {
        if (btnInvite == null) return;

        btnInvite.interactable = false;      
    }

    /// <summary>
    /// 데이터가 없을 때 UI 초기화
    /// </summary>
    private void ClearUI()
    {
        _row = null;
        _boundUrl = "";

        if (txtNick != null) txtNick.text = "";
        if (txtLevel != null) txtLevel.text = "";

        if (profileImage != null)
            profileImage.texture = defaultProfileTexture;

        if (btnInvite != null)
        {
            btnInvite.interactable = false;
            btnInvite.onClick.RemoveAllListeners();
        }
    }

    /// <summary>
    /// 프로필 이미지 적용
    /// - URL이 없으면 기본 이미지
    /// - 캐시에 있으면 즉시 적용
    /// - 없으면 비동기 다운로드
    /// </summary>
    private void ApplyProfile(string url)
    {
        if (profileImage == null) return;

        url = (url ?? "").Trim();

        // InfiniteScroll은 아이템을 재사용하므로
        // 다른 친구 데이터로 바뀌면 먼저 기본 이미지로 리셋
        if (_boundUrl != url)
        {
            _boundUrl = url;
            profileImage.texture = defaultProfileTexture;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            profileImage.texture = defaultProfileTexture;
            return;
        }

        if (FriendProfileImageCache.TryGetCached(url, out var cached) && cached != null)
        {
            profileImage.texture = cached;
            return;
        }

        _ = ApplyProfileAsync(url);
    }

    private async System.Threading.Tasks.Task ApplyProfileAsync(string url)
    {
        var tex = await FriendProfileImageCache.GetAsync(url);

        if (this == null) return;
        if (!gameObject.activeInHierarchy) return;

        // 아이템 재사용 보호
        if (_boundUrl != url) return;

        if (profileImage != null)
            profileImage.texture = tex != null ? tex : defaultProfileTexture;
    }
}
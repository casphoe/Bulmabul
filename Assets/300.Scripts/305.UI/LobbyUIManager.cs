using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;
using Fusion;

public class LobbyUIManager : MonoBehaviour
{
    #region 옵션
    [Header("옵션의 따른 버튼 및 오브젝트들")]
    [SerializeField] Button btnOption;

    [Header("Option Panel 오브젝트")]
    [SerializeField] GameObject optionPanel;
    #endregion

    #region 회원 탈퇴
    [Header("회원 탈퇴 Panel 오브젝트")]
    [SerializeField] GameObject memberShipDwraw;

    [Header("회원 탈퇴에 다른 입력 Password")]
    [SerializeField] TMP_InputField password;

    [SerializeField] TMP_InputField confirmPassword;

    [Header("회원 탈퇴 비밀번호 보기 Toggle")]
    [SerializeField] Toggle showPasswordToggle;
    #endregion

    #region 방 만들기
    [Header("방 만들기 Panel 오브젝트")]
    [SerializeField] GameObject createRoomPanel;

    [Header("방 만들기 제목 InputField")]
    [SerializeField] TMP_InputField inputRoomTitle;

    [Header("Toggle - 개인전, 팀전")]
    [SerializeField] Toggle singleToggle;
    [SerializeField] Toggle teamToggle;

    [Header("PlayerCount 입력")]
    [SerializeField] TMP_InputField inputTeamCount;

    [Header("인원 제한(개인전 2~4, 팀전 4 고정)")]
    [SerializeField] int soloMin = 2;
    [SerializeField] int soloMax = 4;
    [SerializeField] int teamFixed = 4;

    [Header("게임 맵 선택")]
    [SerializeField] Button[] btnMapSelect;

    int mapSelect = 0;
    [Header("게임 맵 이미지")]
    [SerializeField] Image mapImage;

    [Header("맵 선택 스프라이트 들")]
    public Sprite[] mapSprites;
    #endregion

    #region 방을 보여주는 곳
    [Header("만들어준 방을 보여주는 Panel 오브젝트")]
    [SerializeField] GameObject roomPanel;

    [SerializeField] RoomList roomList;

    [Header("최신순, 가나다 정렬")]
    [SerializeField] Toggle lastToggle;
    [SerializeField] Toggle alphaToggle;

    [Header("방 입력 및 찾는 기능")]
    [SerializeField] TMP_InputField inputRoomSearch;

    [Header("방 검색 버튼")]
    [SerializeField] Button btnRoomSearch;
    #endregion

    #region 토스 메시지 설정 변수
    [Header("토스 메시지")]
    public TextMeshProUGUI toasstMessage;

    [Header("토스트 설정")]
    public float toastShowSeconds = 1.2f; // 완전히 보이는 유지 시간
    public float toastFadeSeconds = 0.8f; // 페이드 아웃 시간

    public Coroutine toastRoutine;
    #endregion

    public static LobbyUIManager instance;

    ToggleGroup _modeGroup;
    bool _suppressToggleCallback;

    private void Awake()
    {
        instance = this;
    }

    private void Start()
    {
        SetActive(false);

        SetupToggleGroup();

        if (singleToggle != null)
        {
            singleToggle.onValueChanged.RemoveListener(OnSingleToggleChanged);
            singleToggle.onValueChanged.AddListener(OnSingleToggleChanged);
        }

        if (teamToggle != null)
        {
            teamToggle.onValueChanged.RemoveListener(OnTeamToggleChanged);
            teamToggle.onValueChanged.AddListener(OnTeamToggleChanged);
        }

        if (inputTeamCount != null)
        {
            inputTeamCount.contentType = TMP_InputField.ContentType.IntegerNumber;
            inputTeamCount.onValueChanged.AddListener(_ => EnforcePlayerCountRule_Live());
            inputTeamCount.onEndEdit.AddListener(_ => ClampPlayerCountFinal());
        }

        if (toasstMessage != null)
        {
            var c = toasstMessage.color;
            c.a = 0f;
            toasstMessage.color = c;
            toasstMessage.text = "";
        }


        if (btnRoomSearch != null)
        {
            btnRoomSearch.onClick.RemoveListener(OnClickRoomSearch);
            btnRoomSearch.onClick.AddListener(OnClickRoomSearch);
        }

        if (inputRoomSearch != null)
        {
            // 엔터(Submit)로도 검색되게
            inputRoomSearch.onSubmit.RemoveListener(OnSubmitRoomSearch);
            inputRoomSearch.onSubmit.AddListener(OnSubmitRoomSearch);
        }

        showPasswordToggle.isOn = false;
        LeaveToggleOnOff();
        NetWorkLauncher.instance.JoinLobbyIfNeeded();
        SetupSortToggles();
    }

    #region UI 켜주는 기능
    void SetActive(bool isActive)
    {
        optionPanel.SetActive(isActive);
        memberShipDwraw.SetActive(isActive);
        createRoomPanel.SetActive(isActive);
        roomPanel.SetActive(!isActive);
    }

    public async void BtnClick(int num)
    {
        SetActive(false);
        switch(num)
        {
            case 0:
                optionPanel.SetActive(true);
                break;
            case 1:
                try
                {
                    ShowToast("로그아웃 성공");
                    await FireBaseAuthManager.Instance.LogoutToScene0Async();
                }
                catch (Exception e)
                {
                    Debug.LogError(e);                  
                }
                break;
            case 2:
                memberShipDwraw.SetActive(true);
                password.text = "";
                confirmPassword.text = "";
                break;
            case 3:
                try
                {
                    string pass1 = password.text;
                    string pass2 = confirmPassword.text;
                    if(pass1 == pass2)
                    {
                        ShowToast("회원 탈퇴 성공");
                        await FireBaseAuthManager.Instance.DeleteCurrentAccountAsync(password.text);
                    }
                    else
                    {
                        ShowToast("비밀번호가 틀렸습니다. 확인 부탁드립니다.");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
                break;
            case 4:
                memberShipDwraw.SetActive(false);
                showPasswordToggle.isOn = false;
                LeaveToggleOnOff();
                break;
            case 5:
                createRoomPanel.SetActive(true);

                if (inputTeamCount != null)
                {
                    inputTeamCount.SetTextWithoutNotify("");
                    inputTeamCount.interactable = true;
                }

                // 기본 모드는 개인전
                _suppressToggleCallback = true;
                if (singleToggle != null) singleToggle.isOn = true;
                _suppressToggleCallback = false;

                ApplyModeUI(); // 개인전 기준 UI 세팅(placeholder 등)

                MapSelectUI_Korea();
                break;
            case 6:
                createRoomPanel.SetActive(false);
                break;
            case 7: //방 만들기
                CreateRoomFromUI();
                break;
        }
    }
    #endregion

    #region Toggle

    void SetupToggleGroup()
    {
        if (createRoomPanel == null) return;

        _modeGroup = createRoomPanel.GetComponent<ToggleGroup>();
        if (_modeGroup == null) _modeGroup = createRoomPanel.AddComponent<ToggleGroup>();

        _modeGroup.allowSwitchOff = false; // 항상 하나는 켜져있게

        if (singleToggle != null) singleToggle.group = _modeGroup;
        if (teamToggle != null) teamToggle.group = _modeGroup;
    }


    public void OnSingleToggleChanged(bool isOn)
    {
        if (_suppressToggleCallback) return;
        if (!isOn) return;

        ApplyModeUI();
    }

    public void OnTeamToggleChanged(bool isOn)
    {
        if (_suppressToggleCallback) return;
        if (!isOn) return;

        ApplyModeUI();
    }

    void ApplyModeUI()
    {
        bool isTeam = (teamToggle != null && teamToggle.isOn);

        if (isTeam)
        {
            if (!string.IsNullOrWhiteSpace(inputTeamCount.text))
            {
                inputTeamCount.SetTextWithoutNotify(teamFixed.ToString());
            }           
            else
            {
                SetPlaceholderByLang(kor: "팀전 : 4명 고정입니다..", eng: "Team battle: Fixed to 4 people..");
            }        
        }
        else
        {
            // 개인전: 입력 가능, 처음엔 빈칸 유지 (네 요구사항)
            if (inputTeamCount != null)
            {             
                // 이미 숫자가 들어있으면 범위만 보정, 빈칸이면 그대로 유지
                if (!string.IsNullOrWhiteSpace(inputTeamCount.text))
                {
                    int v = ParseOrDefault(inputTeamCount, soloMin);
                    v = Mathf.Clamp(v, soloMin, soloMax);
                    inputTeamCount.SetTextWithoutNotify(v.ToString());
                }
                else
                {
                    // 빈칸이면 placeholder만
                    SetPlaceholderByLang(kor: "개인전 : 2명 이상입니다..", eng: "Solo: 2 or more players..");
                }
            }
        }
    }
    #endregion

    #region 입력 제한

    void EnforcePlayerCountRule_Live()
    {
        if (inputTeamCount == null) return;

        //  팀전: 사용자가 뭘 치든 "4"로 즉시 교정 
        if (teamToggle != null && teamToggle.isOn)
        {
            if (inputTeamCount.text != teamFixed.ToString())
                inputTeamCount.SetTextWithoutNotify(teamFixed.ToString());
            return;
        }

        // 이하 개인전 로직 (숫자만 + 상한)
        string t = inputTeamCount.text;
        string digitsOnly = "";
        foreach (char c in t)
            if (char.IsDigit(c)) digitsOnly += c;

        if (digitsOnly != t)
            inputTeamCount.SetTextWithoutNotify(digitsOnly);

        //  아직 입력 안 했으면 그대로 빈칸 유지
        if (string.IsNullOrEmpty(digitsOnly)) return;

        // 너무 큰 값은 soloMax로 제한 (live)
        if (int.TryParse(digitsOnly, out int v) && v > soloMax)
            inputTeamCount.SetTextWithoutNotify(soloMax.ToString());
    }

    void ClampPlayerCountFinal()
    {
        if (inputTeamCount == null) return;

        // 팀전: 비어있어도 결국 4로 확정
        if (teamToggle != null && teamToggle.isOn)
        {
            inputTeamCount.SetTextWithoutNotify(teamFixed.ToString());
            return;
        }

        // 개인전: 비어있으면 최소값
        if (string.IsNullOrWhiteSpace(inputTeamCount.text))
        {
            inputTeamCount.SetTextWithoutNotify(soloMin.ToString());
            return;
        }

        int val = ParseOrDefault(inputTeamCount, soloMin);
        val = Mathf.Clamp(val, soloMin, soloMax);
        inputTeamCount.SetTextWithoutNotify(val.ToString());
    }

    int ParseOrDefault(TMP_InputField field, int defaultValue)
    {
        if (field == null) return defaultValue;
        if (int.TryParse(field.text, out int v)) return v;
        return defaultValue;
    }
    #endregion

    #region TMP Placeholder

    void SetPlaceholderByLang(string kor, string eng)
    {
        if (LaguageManager.Instance == null)
        {
            SetPlaceholder(kor);
            return;
        }

        switch (LaguageManager.Instance.currentLang)
        {
            case Lauaguage.Kor: SetPlaceholder(kor); break;
            case Lauaguage.Eng: SetPlaceholder(eng); break;
        }
    }

    private void SetPlaceholder(string msg)
    {
        // placeholder는 Graphic이라서 TMP_Text로 직접 캐스팅하면 실패할 수 있음
        var tmp = inputTeamCount.placeholder.GetComponent<TMP_Text>();
        if (tmp != null)
            tmp.text = msg;
        else
            Debug.LogWarning("Placeholder 오브젝트에 TMP_Text가 없습니다.");
    }
    #endregion

    #region 방 만들기 
    private void CreateRoomFromUI()
    {
        // 1) 방 제목
        string title = inputRoomTitle != null ? inputRoomTitle.text : "";
        title = string.IsNullOrWhiteSpace(title) ? "BulmabulRoom" : title.Trim();

        // 2) 모드 판단
        bool isTeam = (teamToggle != null && teamToggle.isOn);

        // 3) 인원수 파싱 + 강제 규칙 적용
        int count = ParseOrDefault(inputTeamCount, soloMin);

        if (isTeam)
        {
            // 팀전: 무조건 4
            count = teamFixed;
        }
        else
        {
            // 개인전: 2~4 제한
            count = Mathf.Clamp(count, soloMin, soloMax);
        }

        // 4) NetWorkLauncher에 값 전달 + 방 생성 호출
        if (NetWorkLauncher.instance == null)
        {
            Debug.LogError("NetWorkLauncher.instance가 없습니다! 씬에 NetWorkLauncher 오브젝트가 있어야 합니다.");
            return;
        }

        NetWorkLauncher.instance.SetSelectedMap(mapSelect);

        // 방 이름 세팅
        NetWorkLauncher.instance.SetRoomName(title);

        // 최대 인원 세팅(개인전일 때만 유효, 팀전은 런처에서 4로 고정 처리됨)
        NetWorkLauncher.instance.maxPlayers = count;

        // 모드별 생성 호출
        if (isTeam)
            NetWorkLauncher.instance.OnClickCreateRoom_Team();
        else
            NetWorkLauncher.instance.OnClickCreateRoom_Solo();
    }
    #endregion

    #region 방 보여지는 기능
    private void OnEnable()
    {
        NetWorkLauncher.instance.OnRoomsUpdated += OnRoomsUpdated;
        RoomLoad();
    }

    private void OnDisable()
    {
        if (NetWorkLauncher.instance != null)
            NetWorkLauncher.instance.OnRoomsUpdated -= OnRoomsUpdated;
    }

    private void OnRoomsUpdated(IReadOnlyList<SessionInfo> _)
    {
        RoomLoad();
    }

    public void RoomLoad()
    {
        roomList.RoomLoadList();
    }
    #endregion


    #region 방 만들 때 맵 선택하는 기능
    public void BtnMapSelect(int num)
    {
        if (num < 0 || num > 1) return;

        mapSelect = num;

        // 미리보기 갱신
        if (mapImage != null && mapSprites != null && mapSprites.Length > mapSelect)
            mapImage.sprite = mapSprites[mapSelect];

        UpdateMapButtonsInteractable();
    }
    
    void MapSelectUI_Korea()
    {
        mapSelect = 0;

        if (mapImage != null && mapSprites != null && mapSprites.Length > 0)
            mapImage.sprite = mapSprites[0];

        UpdateMapButtonsInteractable();
    }

    void UpdateMapButtonsInteractable()
    {
        if (btnMapSelect == null) return;

        // 안전 처리(버튼이 2개라고 가정: 0=한국, 1=미국)
        if (btnMapSelect.Length > 0 && btnMapSelect[0] != null)
            btnMapSelect[0].interactable = (mapSelect != 0); // 한국 선택 중이면 한국 버튼 비활성화

        if (btnMapSelect.Length > 1 && btnMapSelect[1] != null)
            btnMapSelect[1].interactable = (mapSelect != 1); // 미국 선택 중이면 미국 버튼 비활성화
    }

    #endregion

    #region 방 정렬

    ToggleGroup _sortGroup;
    bool _suppressSortToggle;

    void SetupSortToggles()
    {
        // 토글 그룹(하나만 켜지게)
        _sortGroup = roomPanel.GetComponent<ToggleGroup>();
        if (_sortGroup == null) _sortGroup = roomPanel.AddComponent<ToggleGroup>();
        _sortGroup.allowSwitchOff = false;

        if (alphaToggle != null) alphaToggle.group = _sortGroup;
        if (lastToggle != null) lastToggle.group = _sortGroup;

        if (alphaToggle != null)
        {
            alphaToggle.onValueChanged.RemoveListener(OnAlphaSortChanged);
            alphaToggle.onValueChanged.AddListener(OnAlphaSortChanged);
        }

        if (lastToggle != null)
        {
            lastToggle.onValueChanged.RemoveListener(OnLastSortChanged);
            lastToggle.onValueChanged.AddListener(OnLastSortChanged);
        }

        // 시작은 가나다순
        _suppressSortToggle = true;
        if (alphaToggle != null) alphaToggle.isOn = true;
        if (lastToggle != null) lastToggle.isOn = false;
        _suppressSortToggle = false;

        // 정렬모드 적용 + UI 갱신
        NetWorkLauncher.instance.SetSortMode(RoomSortMode.Alpha);
        RoomLoad();
    }

    void OnAlphaSortChanged(bool isOn)
    {
        if (_suppressSortToggle) return;
        if (!isOn) return;

        NetWorkLauncher.instance.SetSortMode(RoomSortMode.Alpha);
        RoomLoad();
    }

    void OnLastSortChanged(bool isOn)
    {
        if (_suppressSortToggle) return;
        if (!isOn) return;

        NetWorkLauncher.instance.SetSortMode(RoomSortMode.Latest);
        RoomLoad();
    }
    #endregion

    #region 방 찾기 기능(검색)
    public void OnClickRoomSearch()
    {
        ApplyRoomSearch();
    }

    private void OnSubmitRoomSearch(string _)
    {
        ApplyRoomSearch();
    }

    private void ApplyRoomSearch()
    {
        var launcher = NetWorkLauncher.instance;
        if (launcher == null) return;

        string keyword = inputRoomSearch != null ? inputRoomSearch.text : "";
        launcher.SetRoomSearchKeyword(keyword);

        // 결과 없으면 토스트(원하면 문구 다국어 처리)
        if (launcher.roomPrefabList.Count == 0)
        {
            ShowToast(string.IsNullOrWhiteSpace(keyword) ? "방이 없습니다." : "해당 이름의 방이 없습니다.");
        }

        RoomLoad();
    }
    #endregion

    #region 토스트 메세지

    public void ShowToast(string msg)
    {
        if (toasstMessage == null) return;

        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(CoToastFadeOut(msg, toastShowSeconds, toastFadeSeconds));
    }

    IEnumerator CoToastFadeOut(string msg, float showSec, float fadeSec)
    {
        toasstMessage.text = msg;

        Color c = toasstMessage.color;
        c.a = 1f;
        toasstMessage.color = c;

        if (showSec > 0f)
            yield return new WaitForSeconds(showSec);

        float t = 0f;
        float dur = Mathf.Max(0.01f, fadeSec);

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            c.a = Mathf.Lerp(1f, 0f, t / dur);
            toasstMessage.color = c;
            yield return null;
        }

        c.a = 0f;
        toasstMessage.color = c;
        toasstMessage.text = "";
        toastRoutine = null;
    }

    #endregion

    #region 비밀번호 보기 Toggle

    public void LeaveToggleOnOff()
    {
        TogglePassword(password, showPasswordToggle.isOn);
        TogglePassword(confirmPassword, showPasswordToggle.isOn);
    }

    void TogglePassword(TMP_InputField field, bool show)
    {
        field.contentType = show ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
        field.ForceLabelUpdate();
    }

    #endregion
}

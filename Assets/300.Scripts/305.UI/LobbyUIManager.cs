using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;

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
    }

    #region UI 켜주는 기능
    void SetActive(bool isActive)
    {
        optionPanel.SetActive(isActive);
        memberShipDwraw.SetActive(isActive);
        createRoomPanel.SetActive(isActive);
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
                    await FireBaseAuthManager.Instance.LogoutToScene0Async();
                }
                catch (Exception e)
                {
                    Debug.LogError(e);                  
                }
                break;
            case 2:
                memberShipDwraw.SetActive(true);
                break;
            case 3:
                try
                {
                    string pass1 = password.text;
                    string pass2 = confirmPassword.text;
                    if(pass1 == pass2)
                    {
                        await FireBaseAuthManager.Instance.DeleteCurrentAccountAsync(password.text);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
                break;
            case 4:
                memberShipDwraw.SetActive(false);
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
}

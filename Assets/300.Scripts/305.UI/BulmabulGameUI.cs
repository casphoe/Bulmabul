using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 부루마불 게임 씬의 UI 전담 스크립트.
/// 
/// 담당 기능:
/// 1. 현재 턴 안내 표시
/// 2. 턴 남은 시간 표시
/// 3. 플레이어 재화 표시
/// 4. 주사위 홀/짝 컨트롤 패널 표시
/// 5. 땅 구매 패널 표시
/// 6. 건물 건설 패널 표시
/// 7. 여행 이동 버튼/패널 표시
/// 8. 일시정지 버튼/패널 표시
/// 
/// 주의:
/// - 이 스크립트는 NetworkBehaviour가 아니다.
/// - NetworkObject가 필요 없다.
/// - 실제 게임 상태 변경은 BulmabulGameState에게 요청한다.
/// - UI는 BulmabulGameState.Instance 값을 읽어서 표시만 한다.
/// </summary>
public class BulmabulGameUI : MonoBehaviour
{
    [Header("Turn UI")]
    [SerializeField] private TMP_Text txtTurnInfo;
    [SerializeField] private TMP_Text txtTurnTimer;
    [SerializeField] private TMP_Text txtLog;

    [Header("Map View UI")]
    [Tooltip("전체 맵 보기 / 말 보기 전환 버튼")]
    [SerializeField] private Button btnMapView;

    [Tooltip("전체 맵 보기 버튼 안의 TMP_Text")]
    [SerializeField] private TMP_Text txtMapViewButton;

    [Tooltip("Main Camera에 붙어있는 BulmabulCameraFollow")]
    [SerializeField] private BulmabulCameraFollow cameraFollow;

    [Header("Player Info UI")]
    [Tooltip("0~3번 플레이어 정보 UI. Element 0 = 1번째 턴 플레이어")]
    [SerializeField] private BulmabulPlayerInfoUI[] playerInfoUis = new BulmabulPlayerInfoUI[4];

    [Header("Dice Control UI")]
    [Tooltip("주사위 홀/짝 선택 및 게이지 조작 패널. 예: DiceRoll/RollPanel")]
    [SerializeField] private GameObject diceControlPanel;

    [Tooltip("홀수 선택 버튼")]
    [SerializeField] private Button btnDiceOdd;

    [Tooltip("짝수 선택 버튼")]
    [SerializeField] private Button btnDiceEven;

    [Tooltip("확정 버튼. 누르고 있는 동안 게이지가 움직이고, 떼는 순간 굴림")]
    [SerializeField] private Button btnDiceControlConfirm;

    [Tooltip("실제 Fill Amount가 변하는 Image. ProgressBar4/Image 자식 오브젝트를 연결")]
    [SerializeField] private Image diceControlGaugeFill;

    [SerializeField] private TMP_Text txtDiceControlInfo;

    [Tooltip("확정 버튼을 누르고 있을 때 게이지가 차고 빠지는 속도")]
    [SerializeField] private float diceGaugeSpeed = 1.8f;

    [Header("Dice Control Texts")]
    [SerializeField] private TMP_Text txtDiceControlTitle;
    [SerializeField] private TMP_Text txtDiceOddButton;
    [SerializeField] private TMP_Text txtDiceEvenButton;
    [SerializeField] private TMP_Text txtDiceConfirmButton;
    [SerializeField] private TMP_Text txtDiceGaugeLabel;

    private bool _diceControlOpen = false;
    private bool _diceGaugeForward = true;
    private bool _diceGaugeHolding = false;
    private bool _waitingRollStateChange = false;

    private float _diceGaugeValue = 0f;
    private BulmabulDiceParityChoice _selectedDiceParity = BulmabulDiceParityChoice.None;

    private enum DiceControlRequestMode
    {
        NormalRoll = 0,
        JailEscapeRoll = 1
    }

    private DiceControlRequestMode _diceControlRequestMode = DiceControlRequestMode.NormalRoll;

    [Header("Land Purchase UI")]
    [Tooltip("빈 땅 도착 시 표시되는 구매 패널")]
    [SerializeField] private GameObject buyPanel;

    [SerializeField] private TMP_Text txtBuyInfo;

    [Tooltip("땅 구매 버튼")]
    [SerializeField] private Button btnBuyLand;

    [Tooltip("구매 스킵 버튼")]
    [SerializeField] private Button btnSkipBuyLand;

    [Header("Land Purchase Button Texts")]
    [Tooltip("Buy Land 버튼의 첫 번째 자식 TextMeshPro")]
    [SerializeField] private TMP_Text txtBuyLandButton;

    [Tooltip("Buy Skip 버튼의 첫 번째 자식 TextMeshPro")]
    [SerializeField] private TMP_Text txtSkipBuyLandButton;

    [Header("Build UI")]
    [Tooltip("건물 건설 선택 패널")]
    [SerializeField] private GameObject buildPanel;

    [SerializeField] private TMP_Text txtBuildInfo;

    [Tooltip("작은집 건설 버튼")]
    [SerializeField] private Button btnBuildSmallHouse;

    [Tooltip("집 건설 버튼")]
    [SerializeField] private Button btnBuildHouse;

    [Tooltip("큰집 건설 버튼")]
    [SerializeField] private Button btnBuildBigHouse;

    [Tooltip("호텔 건설 버튼")]
    [SerializeField] private Button btnBuildHotel;

    [Tooltip("건설하지 않고 넘기기 버튼")]
    [SerializeField] private Button btnSkipBuild;

    [Header("Build Button Texts")]
    [Tooltip("작은집 건설 버튼의 첫 번째 자식 TextMeshPro")]
    [SerializeField] private TMP_Text txtBuildSmallHouseButton;

    [Tooltip("집 건설 버튼의 첫 번째 자식 TextMeshPro")]
    [SerializeField] private TMP_Text txtBuildHouseButton;

    [Tooltip("큰집 건설 버튼의 첫 번째 자식 TextMeshPro")]
    [SerializeField] private TMP_Text txtBuildBigHouseButton;

    [Tooltip("호텔 건설 버튼의 첫 번째 자식 TextMeshPro")]
    [SerializeField] private TMP_Text txtBuildHotelButton;

    [Tooltip("건설 스킵 버튼의 첫 번째 자식 TextMeshPro")]
    [SerializeField] private TMP_Text txtSkipBuildButton;

    [Header("시작지점 도착 후 건설할 ")]
    [Tooltip("시작지점 도착 후 건설할 내 땅 선택 버튼들. 배열 인덱스 = 칸 인덱스")]
    [SerializeField] private Button[] btnBuildTargets;

    [Header("Travel UI")]
    [Tooltip("여행권이 있을 때 표시되는 여행 이동 버튼")]
    [SerializeField] private Button btnTravelMove;

    [Tooltip("목적지 선택 패널")]
    [SerializeField] private GameObject travelSelectPanel;

    [Tooltip("목적지 선택 버튼들. 배열 인덱스 = 칸 인덱스")]
    [SerializeField] private Button[] btnTravelTargets;

    [Header("Take Over UI")]
    [Tooltip("상대 땅 인수 선택 패널")]
    [SerializeField] private GameObject takeOverPanel;

    [SerializeField] private TMP_Text txtTakeOverInfo;

    [Tooltip("인수하기 버튼")]
    [SerializeField] private Button btnTakeOverLand;

    [Tooltip("인수하지 않고 넘기기 버튼")]
    [SerializeField] private Button btnSkipTakeOverLand;

    [Header("Take Over Button Texts")]
    [Tooltip("인수하기 버튼의 첫 번째 자식 TextMeshPro")]
    [SerializeField] private TMP_Text txtTakeOverLandButton;

    [Tooltip("인수 스킵 버튼의 첫 번째 자식 TextMeshPro")]
    [SerializeField] private TMP_Text txtSkipTakeOverLandButton;

    [Header("Pause UI")]
    [SerializeField] private Button btnPauseResume;
    [SerializeField] private TMP_Text txtPauseButton;
    [SerializeField] private TMP_Text txtPauseCount;
    [SerializeField] private TMP_Text txtPauseInfo;
    [SerializeField] private GameObject pauseBlockPanel;

    [Header("Exit UI")]
    [SerializeField] private Button btnExit;
    [SerializeField] private GameObject exitConfirmPanel;
    [SerializeField] private TMP_Text txtExitTitle;
    [SerializeField] private TMP_Text txtExitMessage;
    [SerializeField] private Button btnExitYes;
    [SerializeField] private Button btnExitNo;

    // 일시정지 토스트 중복 출력 방지용
    private bool _pauseStateInitialized = false;
    private bool _lastPausedState = false;
    private bool _lastPauseOwnerWasLocal = false;

    //방에 나갔는지 확인하는 변수
    private bool _leavingGame = false;

    [Header("Dice Rolling Result UI")]
    [SerializeField] private DiceRollingUI diceRollingUI;

    private bool _waitingDiceResult = false;
    private int _lastHandledRollVersion = -1;

    private readonly Dictionary<int, string> _loadedProfileUrls = new Dictionary<int, string>();

    [Header("Result Panel")]
    [SerializeField] private GameObject resultPanel;

    [Header("Win Result Texts")]
    [SerializeField] private TMP_Text txtWinResult;

    [Header("Result Settings")]
    [SerializeField] private float resultLeaveDelay = 1.5f;


    private bool _handledGameFinished = false;
    private bool _savingGameResult = false;

    private void Awake()
    {
        AutoBindBuyButtonTexts();
        AutoBindBuildButtonTexts();
        AutoBindTakeOverButtonTexts();
        BindButtons();
        CloseAllPopupPanels();
    }

    private void Update()
    {
        RefreshUI();
        RefreshDiceControlGauge();
        RefreshDiceRollResult();
        RefreshMapViewButtonText();

        HandleGameFinishedAndShowWinResult();

        // PC에서 버튼을 누른 채 밖으로 드래그한 뒤 마우스를 떼도 굴림 처리되도록 보정
        if (_diceGaugeHolding && Input.GetMouseButtonUp(0))
        {
            OnDiceConfirmPointerUp();
        }
    }

    /// <summary>
    /// 땅 구매 / 스킵 버튼 안의 첫 번째 자식 TextMeshPro를 자동으로 찾는다.
    /// 인스펙터에 직접 연결하지 않아도 작동하게 하기 위한 보정 함수.
    /// </summary>
    private void AutoBindBuyButtonTexts()
    {
        if (txtBuyLandButton == null && btnBuyLand != null && btnBuyLand.transform.childCount > 0)
        {
            txtBuyLandButton = btnBuyLand.transform.GetChild(0).GetComponent<TMP_Text>();
        }

        if (txtSkipBuyLandButton == null && btnSkipBuyLand != null && btnSkipBuyLand.transform.childCount > 0)
        {
            txtSkipBuyLandButton = btnSkipBuyLand.transform.GetChild(0).GetComponent<TMP_Text>();
        }
    }

    /// <summary>
    /// 건물 건설 버튼 안의 첫 번째 자식 TextMeshPro를 자동으로 찾는다.
    /// 인스펙터에 직접 연결하지 않아도 작동하게 하기 위한 보정 함수.
    /// </summary>
    private void AutoBindBuildButtonTexts()
    {
        if (txtBuildSmallHouseButton == null && btnBuildSmallHouse != null && btnBuildSmallHouse.transform.childCount > 0)
        {
            txtBuildSmallHouseButton = btnBuildSmallHouse.transform.GetChild(0).GetComponent<TMP_Text>();
        }

        if (txtBuildHouseButton == null && btnBuildHouse != null && btnBuildHouse.transform.childCount > 0)
        {
            txtBuildHouseButton = btnBuildHouse.transform.GetChild(0).GetComponent<TMP_Text>();
        }

        if (txtBuildBigHouseButton == null && btnBuildBigHouse != null && btnBuildBigHouse.transform.childCount > 0)
        {
            txtBuildBigHouseButton = btnBuildBigHouse.transform.GetChild(0).GetComponent<TMP_Text>();
        }

        if (txtBuildHotelButton == null && btnBuildHotel != null && btnBuildHotel.transform.childCount > 0)
        {
            txtBuildHotelButton = btnBuildHotel.transform.GetChild(0).GetComponent<TMP_Text>();
        }

        if (txtSkipBuildButton == null && btnSkipBuild != null && btnSkipBuild.transform.childCount > 0)
        {
            txtSkipBuildButton = btnSkipBuild.transform.GetChild(0).GetComponent<TMP_Text>();
        }
    }

    /// <summary>
    /// 인수 / 인수 스킵 버튼 안의 첫 번째 자식 TextMeshPro를 자동으로 찾는다.
    /// </summary>
    private void AutoBindTakeOverButtonTexts()
    {
        if (txtTakeOverLandButton == null && btnTakeOverLand != null && btnTakeOverLand.transform.childCount > 0)
        {
            txtTakeOverLandButton = btnTakeOverLand.transform.GetChild(0).GetComponent<TMP_Text>();
        }

        if (txtSkipTakeOverLandButton == null && btnSkipTakeOverLand != null && btnSkipTakeOverLand.transform.childCount > 0)
        {
            txtSkipTakeOverLandButton = btnSkipTakeOverLand.transform.GetChild(0).GetComponent<TMP_Text>();
        }
    }

    /// <summary>
    /// 게임 종료를 감지해서 승리한 로컬 유저에게 보상 지급 후 자동으로 방에서 나가게 한다.
    /// </summary>
    private void HandleGameFinishedAndShowWinResult()
    {
        if (_handledGameFinished || _savingGameResult)
            return;

        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        // 중요:
        // Fusion Spawned()가 끝나기 전에는 Networked Property 접근 금지.
        // GameFinished, WinnerIndex, Players 같은 Networked 값은
        // IsSpawnReady == true 이후에만 읽어야 한다.
        if (!state.IsSpawnReady)
            return;

        if (!state.GameFinished)
            return;

        if (!state.IsLocalPlayerWinner())
            return;

        _handledGameFinished = true;
        StartCoroutine(CoShowWinResultRewardAndLeave());
    }

    /// <summary>
    /// 승리 보상 저장 → ResultPanel/Win에 결과 표시 → 일정 시간 후 로비 이동.
    /// </summary>
    private IEnumerator CoShowWinResultRewardAndLeave()
    {
        _savingGameResult = true;

        Task<BulmabulRewardResult> rewardTask = BulmabulRewardService.ApplyLocalWinRewardAsync();

        while (rewardTask != null && !rewardTask.IsCompleted)
            yield return null;

        if (rewardTask != null && rewardTask.IsFaulted)
        {
            Debug.LogException(rewardTask.Exception);

            ShowWinResultPanel(
                "승리했습니다!\n\n상대 플레이어가 게임을 나갔습니다.\n하지만 보상 저장 중 오류가 발생했습니다.\n\n잠시 후 로비로 이동합니다.",
                "You won!\n\nThe other player left the game.\nHowever, failed to save rewards.\n\nReturning to the lobby shortly."
            );
        }
        else
        {
            BulmabulRewardResult result = rewardTask != null ? rewardTask.Result : null;

            if (result != null)
            {
                ShowWinResultPanel(
                    BuildWinRewardResultTextKor(result),
                    BuildWinRewardResultTextEng(result)
                );
            }
            else
            {
                ShowWinResultPanel(
                    "승리했습니다!\n\n상대 플레이어가 게임을 나갔습니다.\n승리 보상을 획득했습니다!\n\n잠시 후 로비로 이동합니다.",
                    "You won!\n\nThe other player left the game.\nYou received victory rewards!\n\nReturning to the lobby shortly."
                );
            }
        }

        float delay = Mathf.Max(3.5f, resultLeaveDelay);
        yield return new WaitForSeconds(delay);

        if (NetWorkLauncher.instance != null)
        {
            _ = NetWorkLauncher.instance.LeaveRoomToLobby(1);
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
        }

        _savingGameResult = false;
    }

    private void ShowWinResultPanel(string kor, string eng)
    {
        string msg = GetByLanguage(kor, eng);

        if (resultPanel != null)
            resultPanel.SetActive(true);

        if (txtWinResult != null)
            txtWinResult.text = msg;
    }

    private string BuildWinRewardResultTextKor(BulmabulRewardResult result)
    {
        if (result == null)
            return "승리했습니다!\n\n상대 플레이어가 게임을 나갔습니다.\n승리 보상을 획득했습니다!\n\n잠시 후 로비로 이동합니다.";

        string msg =
            $"승리했습니다!\n\n" +
            $"상대 플레이어가 게임을 나갔습니다.\n\n" +
            $"경험치 배율 : x{result.ExpMultiplier:0.##}\n" +
            $"획득 경험치 : +{result.FinalExpReward:N0}\n" +
            $"승리 보상 : +{result.WinCashReward:N0}";

        if (result.IsLevelUp)
        {
            msg +=
                $"\n\n레벨업!\n" +
                $"Lv.{result.BeforeLevel} → Lv.{result.AfterLevel}\n" +
                $"레벨업 보상 : +{result.LevelUpCashReward:N0}";
        }

        msg +=
            $"\n\n총 획득 재화 : +{result.TotalCashReward:N0}\n" +
            $"잠시 후 로비로 이동합니다.";

        return msg;
    }

    private string BuildWinRewardResultTextEng(BulmabulRewardResult result)
    {
        if (result == null)
            return "You won!\n\nThe other player left the game.\nYou received victory rewards!\n\nReturning to the lobby shortly.";

        string msg =
            $"You won!\n\n" +
            $"The other player left the game.\n\n" +
            $"EXP Multiplier : x{result.ExpMultiplier:0.##}\n" +
            $"EXP Gained : +{result.FinalExpReward:N0}\n" +
            $"Win Reward : +{result.WinCashReward:N0}";

        if (result.IsLevelUp)
        {
            msg +=
                $"\n\nLevel Up!\n" +
                $"Lv.{result.BeforeLevel} → Lv.{result.AfterLevel}\n" +
                $"Level Up Reward : +{result.LevelUpCashReward:N0}";
        }

        msg +=
            $"\n\nTotal Cash Gained : +{result.TotalCashReward:N0}\n" +
            $"Returning to the lobby shortly.";

        return msg;
    }

    /// <summary>
    /// UI 버튼 이벤트 연결.
    /// 인스펙터 OnClick에 직접 연결하지 않아도 되도록 코드에서 연결한다.
    /// </summary>
    private void BindButtons()
    {
        if (btnDiceOdd != null)
        {
            btnDiceOdd.onClick.RemoveListener(OnClickDiceOdd);
            btnDiceOdd.onClick.AddListener(OnClickDiceOdd);
        }

        if (btnDiceEven != null)
        {
            btnDiceEven.onClick.RemoveListener(OnClickDiceEven);
            btnDiceEven.onClick.AddListener(OnClickDiceEven);
        }

        if (btnDiceControlConfirm != null)
        {
            // 확정 버튼은 OnClick이 아니라 PointerDown / PointerUp으로 처리한다.
            btnDiceControlConfirm.onClick.RemoveAllListeners();
            BindDiceConfirmHoldEvents();
        }

        if (btnBuyLand != null)
        {
            btnBuyLand.onClick.RemoveListener(OnClickBuyLand);
            btnBuyLand.onClick.AddListener(OnClickBuyLand);
        }

        if (btnSkipBuyLand != null)
        {
            btnSkipBuyLand.onClick.RemoveListener(OnClickSkipBuyLand);
            btnSkipBuyLand.onClick.AddListener(OnClickSkipBuyLand);
        }

        if (btnBuildSmallHouse != null)
        {
            btnBuildSmallHouse.onClick.RemoveListener(OnClickBuildSmallHouse);
            btnBuildSmallHouse.onClick.AddListener(OnClickBuildSmallHouse);
        }

        if (btnBuildHouse != null)
        {
            btnBuildHouse.onClick.RemoveListener(OnClickBuildHouse);
            btnBuildHouse.onClick.AddListener(OnClickBuildHouse);
        }

        if (btnBuildBigHouse != null)
        {
            btnBuildBigHouse.onClick.RemoveListener(OnClickBuildBigHouse);
            btnBuildBigHouse.onClick.AddListener(OnClickBuildBigHouse);
        }

        if (btnBuildHotel != null)
        {
            btnBuildHotel.onClick.RemoveListener(OnClickBuildHotel);
            btnBuildHotel.onClick.AddListener(OnClickBuildHotel);
        }

        if (btnSkipBuild != null)
        {
            btnSkipBuild.onClick.RemoveListener(OnClickSkipBuild);
            btnSkipBuild.onClick.AddListener(OnClickSkipBuild);
        }

        if (btnTravelMove != null)
        {
            btnTravelMove.onClick.RemoveListener(OnClickTravelMove);
            btnTravelMove.onClick.AddListener(OnClickTravelMove);
        }

        if (btnPauseResume != null)
        {
            btnPauseResume.onClick.RemoveListener(OnClickPauseResume);
            btnPauseResume.onClick.AddListener(OnClickPauseResume);
        }

        if (btnMapView != null)
        {
            btnMapView.onClick.RemoveListener(OnClickMapView);
            btnMapView.onClick.AddListener(OnClickMapView);
        }

        if (btnExit != null)
        {
            btnExit.onClick.RemoveListener(OnClickExit);
            btnExit.onClick.AddListener(OnClickExit);
        }

        if (btnExitYes != null)
        {
            btnExitYes.onClick.RemoveListener(OnClickExitYes);
            btnExitYes.onClick.AddListener(OnClickExitYes);
        }

        if (btnExitNo != null)
        {
            btnExitNo.onClick.RemoveListener(OnClickExitNo);
            btnExitNo.onClick.AddListener(OnClickExitNo);
        }

        if (btnTakeOverLand != null)
        {
            btnTakeOverLand.onClick.RemoveListener(OnClickTakeOverLand);
            btnTakeOverLand.onClick.AddListener(OnClickTakeOverLand);
        }

        if (btnSkipTakeOverLand != null)
        {
            btnSkipTakeOverLand.onClick.RemoveListener(OnClickSkipTakeOverLand);
            btnSkipTakeOverLand.onClick.AddListener(OnClickSkipTakeOverLand);
        }

        BindTravelTargetButtons();
        BindBuildTargetButtons();
    }

    /// <summary>
    /// 확정 버튼을 누르는 순간 게이지 시작,
    /// 버튼에서 손을 떼는 순간 현재 게이지 값으로 주사위를 굴리도록 이벤트를 연결한다.
    /// </summary>
    private void BindDiceConfirmHoldEvents()
    {
        if (btnDiceControlConfirm == null)
            return;

        EventTrigger trigger = btnDiceControlConfirm.GetComponent<EventTrigger>();

        if (trigger == null)
            trigger = btnDiceControlConfirm.gameObject.AddComponent<EventTrigger>();

        trigger.triggers.Clear();

        EventTrigger.Entry pointerDown = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerDown
        };

        pointerDown.callback.AddListener((data) =>
        {
            OnDiceConfirmPointerDown();
        });

        trigger.triggers.Add(pointerDown);

        EventTrigger.Entry pointerUp = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerUp
        };

        pointerUp.callback.AddListener((data) =>
        {
            OnDiceConfirmPointerUp();
        });

        trigger.triggers.Add(pointerUp);
    }

    /// <summary>
    /// 여행 목적지 버튼 연결.
    /// 버튼 배열 인덱스가 보드 칸 인덱스와 같다고 가정한다.
    /// </summary>
    private void BindTravelTargetButtons()
    {
        if (btnTravelTargets == null)
            return;

        for (int i = 0; i < btnTravelTargets.Length; i++)
        {
            int cellIndex = i;
            Button button = btnTravelTargets[i];

            if (button == null)
                continue;

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                OnClickTravelTarget(cellIndex);
            });
        }
    }

    /// <summary>
    /// 시작지점 건설용 땅 선택 버튼 연결.
    /// 버튼 배열 인덱스가 보드 칸 인덱스와 같다고 가정한다.
    /// </summary>
    private void BindBuildTargetButtons()
    {
        if (btnBuildTargets == null)
            return;

        for (int i = 0; i < btnBuildTargets.Length; i++)
        {
            int cellIndex = i;
            Button button = btnBuildTargets[i];

            if (button == null)
                continue;

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                OnClickBuildTarget(cellIndex);
            });
        }
    }

    /// <summary>
    /// 최초 시작 시 모든 팝업성 패널 닫기.
    /// </summary>
    private void CloseAllPopupPanels()
    {
        if (buyPanel != null)
            buyPanel.SetActive(false);

        if (buildPanel != null)
            buildPanel.SetActive(false);

        if (travelSelectPanel != null)
            travelSelectPanel.SetActive(false);

        if (pauseBlockPanel != null)
            pauseBlockPanel.SetActive(false);

        if (diceControlPanel != null)
            diceControlPanel.SetActive(false);

        if (exitConfirmPanel != null)
            exitConfirmPanel.SetActive(false);

        if (takeOverPanel != null)
            takeOverPanel.SetActive(false);

        if (resultPanel != null)
            resultPanel.SetActive(false);

    }

    /// <summary>
    /// 전체 UI 갱신.
    /// </summary>
    private void RefreshUI()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        // 중요:
        // NetworkBehaviour가 씬에 있어도 Spawned() 전에는 Networked Property를 읽으면 안 된다.
        if (state == null || state.Runner == null || !state.IsSpawnReady)
        {
            RefreshWaitingState();
            return;
        }

        RefreshTurnUI(state);
        RefreshMoneyUI(state);
        RefreshMainButtons(state);
        RefreshBuyPanel(state);
        RefreshBuildPanel(state);
        RefreshTravelUI(state);
        RefreshTakeOverPanel(state);
        RefreshPauseUI(state);
        RefreshLog(state);
    }

    /// <summary>
    /// 아직 네트워크 게임 상태가 준비되지 않았을 때 표시.
    /// </summary>
    private void RefreshWaitingState()
    {
        if (txtTurnInfo != null)
            txtTurnInfo.text = "게임 상태 준비 중...";

        if (txtTurnTimer != null)
        {
            txtTurnTimer.text = GetByLanguage(
                "턴 시간: --초",
                "Turn Time: --s"
            );
        }

        if (txtPauseCount != null)
            txtPauseCount.text = "일시정지 남은 횟수: -";

        if (txtPauseInfo != null)
            txtPauseInfo.text = "";

        if (btnPauseResume != null)
            btnPauseResume.interactable = false;

        if (buyPanel != null)
            buyPanel.SetActive(false);

        if (buildPanel != null)
            buildPanel.SetActive(false);

        if (travelSelectPanel != null)
            travelSelectPanel.SetActive(false);

        if (pauseBlockPanel != null)
            pauseBlockPanel.SetActive(false);

        if (diceControlPanel != null)
            diceControlPanel.SetActive(false);

        _diceControlOpen = false;
        _diceGaugeHolding = false;
        _waitingRollStateChange = false;
    }

    /// <summary>
    /// 턴 안내 / 남은 시간 표시.
    /// </summary>
    private void RefreshTurnUI(BulmabulGameState state)
    {
        if (txtTurnInfo != null)
        {
            if (!state.HasValidCurrentTurn())
            {
                txtTurnInfo.text = "턴 정보 없음";
            }
            else
            {
                string currentName = state.GetCurrentTurnPlayerName();

                txtTurnInfo.text = state.IsMyTurn()
     ? $"내 턴입니다. ({currentName})\n홀수 또는 짝수를 선택한 뒤 확정 버튼을 누르고 있다가 떼세요."
     : $"{currentName}님의 턴입니다.";
            }
        }

        if (txtTurnTimer != null)
        {
            txtTurnTimer.text = GetTurnTimerText(state);
        }
    }

    /// <summary>
    /// 턴 타이머에 표시할 문구를 만든다.
    /// 숫자만 보여주지 않고, 현재 상태와 남은 시간을 같이 표시한다.
    /// </summary>
    private string GetTurnTimerText(BulmabulGameState state)
    {
        if (state == null)
            return GetByLanguage("턴 시간: --초", "Turn Time: --s");

        int remain = Mathf.CeilToInt(state.GetRemainTurnSeconds());
        remain = Mathf.Max(0, remain);

        if (state.IsPaused)
            return GetByLanguage($"일시정지 중 | {remain:00}초", $"Paused | {remain:00}s");

        if (!state.HasValidCurrentTurn())
            return GetByLanguage("턴 정보 없음 | --초", "No Turn | --s");

        if (state.IsMyTurn())
            return GetByLanguage($"내 턴 | {remain:00}초", $"My Turn | {remain:00}s");

        string currentName = state.GetCurrentTurnPlayerName();

        return GetByLanguage(
            $"{currentName}님 턴 | {remain:00}초",
            $"{currentName}'s Turn | {remain:00}s"
        );
    }

    /// <summary>
    /// 상대 땅 인수 패널 갱신.
    /// 통행료 지급 후 호텔이 없는 상대 땅이면 표시된다.
    /// </summary>
    private void RefreshTakeOverPanel(BulmabulGameState state)
    {
        if (takeOverPanel == null)
            return;

        bool show = state.ShouldShowTakeOverPanelForLocalPlayer();
        takeOverPanel.SetActive(show);

        if (!show)
            return;

        RefreshTakeOverButtonTexts();

        if (txtTakeOverInfo != null)
            txtTakeOverInfo.text = state.GetPendingTakeOverInfoText();

        if (btnTakeOverLand != null)
            btnTakeOverLand.interactable = true;

        if (btnSkipTakeOverLand != null)
            btnSkipTakeOverLand.interactable = true;
    }

    /// <summary>
    /// 인수 패널 버튼 문구를 현재 언어에 맞게 표시한다.
    /// </summary>
    private void RefreshTakeOverButtonTexts()
    {
        if (txtTakeOverLandButton != null)
        {
            txtTakeOverLandButton.text = GetByLanguage(
                "인수하기",
                "Take Over"
            );
        }

        if (txtSkipTakeOverLandButton != null)
        {
            txtSkipTakeOverLandButton.text = GetByLanguage(
                "스킵",
                "Skip"
            );
        }
    }

    private void OnClickTakeOverLand()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        if (state.TryGetPendingTakeOverLackAmount(out int lackAmount))
        {
            ShowLackMoneyToast(lackAmount);
            return;
        }

        state.RequestTakeOverLandLocal();
    }

    private void OnClickSkipTakeOverLand()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        state.RequestSkipTakeOverLandLocal();
    }

    /// <summary>
    /// 플레이어별 게임 재화 / 닉네임 / 레벨 / 팀 / 순서 / 리더 아이콘 / 프로필 이미지 갱신.
    /// </summary>
    private void RefreshMoneyUI(BulmabulGameState state)
    {
        if (playerInfoUis == null)
            return;

        for (int i = 0; i < playerInfoUis.Length; i++)
        {
            BulmabulPlayerInfoUI ui = playerInfoUis[i];

            if (ui == null)
                continue;

            if (!state.TryGetPlayerViewData(
                    i,
                    out string nickname,
                    out int level,
                    out int cash,
                    out int teamSideInt,
                    out int turnOrder,
                    out bool isLeader,
                    out bool isBankrupt,
                    out string photoUrl))
            {
                ui.Hide();
                _loadedProfileUrls.Remove(i);
                continue;
            }

            ui.Show(
                nickname,
                level,
                cash,
                teamSideInt,
                turnOrder,
                isLeader,
                isBankrupt
            );

            RefreshSingleProfileImage(i, ui, photoUrl);
        }
    }

    /// <summary>
    /// 내 턴이고 주사위를 굴릴 수 있으면 DiceControlPanel을 자동으로 열고,
    /// 굴릴 수 없으면 자동으로 닫는다.
    /// 
    /// 단, 감옥 탈출 주사위 모드일 때는 CanLocalRollDice()가 false여도
    /// CanLocalJailRollDice()가 true면 패널을 유지해야 한다.
    /// </summary>
    private void RefreshMainButtons(BulmabulGameState state)
    {
        bool isJailDiceMode = _diceControlRequestMode == DiceControlRequestMode.JailEscapeRoll;
        bool canNormalRoll = state.CanLocalRollDice();
        bool canJailRoll = state.CanLocalJailRollDice();

        if (isJailDiceMode)
        {
            if (!canJailRoll)
            {
                _waitingRollStateChange = false;

                if (_diceControlOpen)
                    CloseDiceControlPanel();

                if (diceControlPanel != null)
                    diceControlPanel.SetActive(false);
            }

            if (btnTravelMove != null)
            {
                btnTravelMove.gameObject.SetActive(false);
                btnTravelMove.interactable = false;
            }

            return;
        }

        if (!canNormalRoll)
        {
            _waitingRollStateChange = false;

            if (_diceControlOpen)
                CloseDiceControlPanel();

            if (diceControlPanel != null)
                diceControlPanel.SetActive(false);
        }
        else
        {
            if (!_diceControlOpen && !_waitingRollStateChange)
                OpenDiceControlPanel();
        }

        if (btnTravelMove != null)
        {
            bool canTravel = state.CanLocalUseTravel();
            btnTravelMove.gameObject.SetActive(canTravel);
            btnTravelMove.interactable = canTravel;
        }
    }

    /// <summary>
    /// 빈 땅 구매 패널 갱신.
    /// 땅 구매 / 스킵 버튼 텍스트도 현재 언어에 맞게 갱신한다.
    /// </summary>
    private void RefreshBuyPanel(BulmabulGameState state)
    {
        if (buyPanel == null)
            return;

        bool show = state.ShouldShowBuyPanelForLocalPlayer();
        buyPanel.SetActive(show);

        if (!show)
            return;

        RefreshBuyButtonTexts();

        if (txtBuyInfo != null)
            txtBuyInfo.text = state.GetPendingBuyInfoText();

        if (btnBuyLand != null)
            btnBuyLand.interactable = state.CanLocalConfirmBuyLand();

        if (btnSkipBuyLand != null)
            btnSkipBuyLand.interactable = true;
    }

    /// <summary>
    /// 땅 구매 패널 버튼 문구를 현재 언어에 맞게 표시한다.
    /// 한국어: 땅 구매 / 스킵
    /// 영어: Buy Land / Skip
    /// </summary>
    private void RefreshBuyButtonTexts()
    {
        if (txtBuyLandButton != null)
        {
            txtBuyLandButton.text = GetByLanguage(
                "땅 구매",
                "Buy Land"
            );
        }

        if (txtSkipBuyLandButton != null)
        {
            txtSkipBuyLandButton.text = GetByLanguage(
                "스킵",
                "Skip"
            );
        }
    }

    /// <summary>
    /// 건물 건설 패널 갱신.
    /// </summary>
    private void RefreshBuildPanel(BulmabulGameState state)
    {
        if (buildPanel == null)
            return;

        bool show = state.ShouldShowBuildPanelForLocalPlayer();
        buildPanel.SetActive(show);

        RefreshBuildTargetButtons(state, show);

        if (!show)
            return;

        RefreshBuildButtonTexts();

        if (txtBuildInfo != null)
            txtBuildInfo.text = state.GetPendingBuildInfoText();

        if (btnBuildSmallHouse != null)
            btnBuildSmallHouse.interactable = state.CanLocalBuildButtonClick(BulmabulBuildPart.SmallHouse);

        if (btnBuildHouse != null)
            btnBuildHouse.interactable = state.CanLocalBuildButtonClick(BulmabulBuildPart.House);

        if (btnBuildBigHouse != null)
            btnBuildBigHouse.interactable = state.CanLocalBuildButtonClick(BulmabulBuildPart.BigHouse);

        if (btnBuildHotel != null)
            btnBuildHotel.interactable = state.CanLocalBuildButtonClick(BulmabulBuildPart.Hotel);

        if (btnSkipBuild != null)
            btnSkipBuild.interactable = true;
    }

    /// <summary>
    /// 건물 건설 패널 버튼 문구를 현재 언어에 맞게 표시한다.
    /// 버튼 이미지는 글자 없는 PNG를 사용하고,
    /// 실제 문구는 버튼 첫 번째 자식 TextMeshPro에 표시한다.
    /// </summary>
    private void RefreshBuildButtonTexts()
    {
        if (txtBuildSmallHouseButton != null)
        {
            txtBuildSmallHouseButton.text = GetByLanguage(
                "작은집",
                "Small House"
            );
        }

        if (txtBuildHouseButton != null)
        {
            txtBuildHouseButton.text = GetByLanguage(
                "집",
                "House"
            );
        }

        if (txtBuildBigHouseButton != null)
        {
            txtBuildBigHouseButton.text = GetByLanguage(
                "큰집",
                "Big House"
            );
        }

        if (txtBuildHotelButton != null)
        {
            txtBuildHotelButton.text = GetByLanguage(
                "호텔",
                "Hotel"
            );
        }

        if (txtSkipBuildButton != null)
        {
            txtSkipBuildButton.text = GetByLanguage(
                "스킵",
                "Skip"
            );
        }
    }

    /// <summary>
    /// 시작지점 건설 상태일 때 내 땅 선택 버튼 표시.
    /// </summary>
    private void RefreshBuildTargetButtons(BulmabulGameState state, bool buildPanelVisible)
    {
        if (btnBuildTargets == null)
            return;

        for (int i = 0; i < btnBuildTargets.Length; i++)
        {
            Button button = btnBuildTargets[i];

            if (button == null)
                continue;

            bool active = buildPanelVisible && state.ShouldShowBuildTargetButton(i);

            button.gameObject.SetActive(active);
            button.interactable = active;
        }
    }

    /// <summary>
    /// 여행 이동 패널 상태 갱신.
    /// 여행 버튼은 RefreshMainButtons에서 처리하고,
    /// 목적지 패널은 사용자가 버튼을 눌렀을 때 열고 목적지 선택 후 닫는다.
    /// </summary>
    private void RefreshTravelUI(BulmabulGameState state)
    {
        if (travelSelectPanel == null)
            return;

        if (!state.CanLocalUseTravel())
            travelSelectPanel.SetActive(false);
    }

    /// <summary>
    /// 일시정지 UI 갱신.
    /// </summary>
    private void RefreshPauseUI(BulmabulGameState state)
    {
        bool isPaused = state.IsPaused;

        CheckPauseToast(state, isPaused);

        if (pauseBlockPanel != null)
            pauseBlockPanel.SetActive(isPaused);

        if (txtPauseCount != null)
        {
            txtPauseCount.text = GetByLanguage(
                $"일시정지 남은 횟수: {state.GetLocalPauseRemain()} / {state.MaxPauseCountPerPlayer}",
                $"Pauses left: {state.GetLocalPauseRemain()} / {state.MaxPauseCountPerPlayer}"
            );
        }

        if (txtPauseInfo != null)
        {
            if (isPaused)
            {
                string pauseOwnerNick = state.GetPauseOwnerNickname();

                txtPauseInfo.text = state.IsLocalPauseOwner()
                    ? GetByLanguage("내가 일시정지했습니다.", "You paused the game.")
                    : GetByLanguage(
                        $"{pauseOwnerNick} 플레이어가 일시정지했습니다.",
                        $"{pauseOwnerNick} paused the game."
                    );
            }
            else
            {
                txtPauseInfo.text = "";
            }
        }

        if (txtPauseButton != null)
        {
            if (isPaused)
            {
                txtPauseButton.text = state.IsLocalPauseOwner()
                    ? GetByLanguage("재개", "Resume")
                    : GetByLanguage("일시정지 중", "Paused");
            }
            else
            {
                txtPauseButton.text = GetByLanguage("일시정지", "Pause");
            }
        }

        if (btnPauseResume != null)
        {
            if (isPaused)
                btnPauseResume.interactable = state.IsLocalPauseOwner();
            else
                btnPauseResume.interactable = state.CanLocalPause();
        }
    }

    /// <summary>
    /// 일시정지 상태가 바뀐 순간에만 토스트를 출력한다.
    /// </summary>
    private void CheckPauseToast(BulmabulGameState state, bool isPaused)
    {
        bool ownerIsLocal = isPaused && state.IsLocalPauseOwner();

        if (!_pauseStateInitialized)
        {
            _pauseStateInitialized = true;
            _lastPausedState = isPaused;
            _lastPauseOwnerWasLocal = ownerIsLocal;

            if (isPaused)
                ShowPauseStartedToast(state, ownerIsLocal);

            return;
        }

        if (!_lastPausedState && isPaused)
        {
            ShowPauseStartedToast(state, ownerIsLocal);
        }

        _lastPausedState = isPaused;
        _lastPauseOwnerWasLocal = ownerIsLocal;
    }

    /// <summary>
    /// 일시정지 시작 토스트 출력.
    /// </summary>
    private void ShowPauseStartedToast(BulmabulGameState state, bool ownerIsLocal)
    {
        if (ToastMessageManager.instance == null)
            return;

        if (ownerIsLocal)
        {
            ToastMessageManager.instance.ShowToast(
                "내가 일시정지했습니다.",
                "You paused the game."
            );
        }
        else
        {
            string pauseOwnerNick = state.GetPauseOwnerNickname();

            ToastMessageManager.instance.ShowToast(
                $"{pauseOwnerNick} 플레이어가 일시정지했습니다.",
                $"{pauseOwnerNick} paused the game."
            );
        }
    }

    /// <summary>
    /// 현재 언어에 따라 한국어/영어 문구를 반환한다.
    /// LaguageManager가 없으면 한국어를 기본값으로 사용한다.
    /// </summary>
    private string GetByLanguage(string kor, string eng)
    {
        if (LaguageManager.Instance == null)
            return kor;

        return LaguageManager.Instance.currentLang == Lauaguage.Eng ? eng : kor;
    }

    /// <summary>
    /// 게임 로그 표시.
    /// </summary>
    private void RefreshLog(BulmabulGameState state)
    {
        if (txtLog != null)
            txtLog.text = state.LastLogMessage.ToString();
    }

    /// <summary>
    /// 주사위 컨트롤 패널 열기.
    /// </summary>
    private void OpenDiceControlPanel()
    {
        _diceControlOpen = true;
        _selectedDiceParity = BulmabulDiceParityChoice.None;
        _diceGaugeValue = 0f;
        _diceGaugeForward = true;
        _diceGaugeHolding = false;

        if (diceControlPanel != null)
            diceControlPanel.SetActive(true);

        SetDiceGaugeValue(0f);

        RefreshDiceControlStaticTexts();
        RefreshDiceControlInfo();
    }

    /// <summary>
    /// 감옥 탈출용 주사위 컨트롤 패널 열기.
    /// 감옥 팝업에서 주사위 굴리기를 눌렀을 때 호출한다.
    /// 
    /// 일반 주사위 이동이 아니라,
    /// 확정 시 RequestJailRollDiceLocal()로 보내기 위한 모드다.
    /// </summary>
    public void OpenDiceControlPanelForJailEscape()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        if (!state.CanLocalJailRollDice())
        {
            if (ToastMessageManager.instance != null)
            {
                ToastMessageManager.instance.ShowToast(
                    "지금은 감옥 탈출 주사위를 굴릴 수 없습니다.",
                    "You cannot roll for jail escape now."
                );
            }

            return;
        }

        _diceControlRequestMode = DiceControlRequestMode.JailEscapeRoll;
        _waitingRollStateChange = false;
        _waitingDiceResult = false;

        OpenDiceControlPanel();

        if (txtDiceControlTitle != null)
        {
            txtDiceControlTitle.text = GetByLanguage(
                "감옥 탈출 주사위",
                "Jail Escape Dice"
            );
        }

        if (txtDiceControlInfo != null)
        {
            txtDiceControlInfo.text = GetByLanguage(
                "홀수 또는 짝수를 선택한 뒤 확정 버튼을 누르고 떼세요.\n더블이 나오면 감옥에서 탈출합니다.",
                "Choose odd or even, then hold and release Confirm.\nRoll doubles to escape jail."
            );
        }
    }

    /// <summary>
    /// 주사위 컨트롤 패널 닫기.
    /// </summary>
    private void CloseDiceControlPanel()
    {
        _diceControlOpen = false;
        _diceGaugeHolding = false;
        _selectedDiceParity = BulmabulDiceParityChoice.None;
        _diceControlRequestMode = DiceControlRequestMode.NormalRoll;

        if (diceControlPanel != null)
            diceControlPanel.SetActive(false);
    }

    /// <summary>
    /// 홀수 선택.
    /// </summary>
    private void OnClickDiceOdd()
    {
        _selectedDiceParity = BulmabulDiceParityChoice.Odd;
        RefreshDiceControlInfo();
    }

    /// <summary>
    /// 짝수 선택.
    /// </summary>
    private void OnClickDiceEven()
    {
        _selectedDiceParity = BulmabulDiceParityChoice.Even;
        RefreshDiceControlInfo();
    }

    /// <summary>
    /// 확정 버튼을 누르기 시작했을 때.
    /// 이때부터 게이지가 움직인다.
    /// </summary>
    private void OnDiceConfirmPointerDown()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        if (!_diceControlOpen)
            return;

        bool canRoll = _diceControlRequestMode == DiceControlRequestMode.JailEscapeRoll
            ? state.CanLocalJailRollDice()
            : state.CanLocalRollDice();

        if (!canRoll)
        {
            CloseDiceControlPanel();
            return;
        }

        if (_selectedDiceParity == BulmabulDiceParityChoice.None)
        {
            if (ToastMessageManager.instance != null)
            {
                ToastMessageManager.instance.ShowToast(
                    "먼저 홀수 또는 짝수를 선택해주세요.",
                    "Please choose odd or even first."
                );
            }

            return;
        }

        _diceGaugeHolding = true;
    }

    /// <summary>
    /// 확정 버튼에서 손을 뗐을 때.
    /// 현재 게이지 값으로 주사위를 굴린다.
    /// </summary>
    private void OnDiceConfirmPointerUp()
    {
        if (!_diceGaugeHolding)
            return;

        _diceGaugeHolding = false;

        RollDiceByCurrentGauge();
    }

    /// <summary>
    /// 현재 게이지 값으로 주사위 굴림 요청.
    /// 일반 턴 주사위와 감옥 탈출 주사위를 같은 DiceRollPanel에서 처리한다.
    /// </summary>
    private void RollDiceByCurrentGauge()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        bool isJailDiceMode = _diceControlRequestMode == DiceControlRequestMode.JailEscapeRoll;

        bool canRoll = isJailDiceMode
            ? state.CanLocalJailRollDice()
            : state.CanLocalRollDice();

        if (!canRoll)
        {
            CloseDiceControlPanel();
            return;
        }

        if (_selectedDiceParity == BulmabulDiceParityChoice.None)
        {
            if (ToastMessageManager.instance != null)
            {
                ToastMessageManager.instance.ShowToast(
                    "홀수 또는 짝수를 선택해주세요.",
                    "Please choose odd or even."
                );
            }

            return;
        }

        int gaugePermille = Mathf.RoundToInt(Mathf.Clamp01(_diceGaugeValue) * 1000f);

        _waitingRollStateChange = true;
        _waitingDiceResult = true;

        CloseDiceControlPanel();

        if (cameraFollow != null)
        {
            cameraFollow.ForceFollowView(true);
            RefreshMapViewButtonText();
        }

        OwnedDice equippedDice = GetLocalEquippedDice();

        if (diceRollingUI != null)
            diceRollingUI.ShowRolling(equippedDice);

        if (isJailDiceMode)
        {
            state.RequestJailRollDiceLocal((int)_selectedDiceParity, gaugePermille);
        }
        else
        {
            state.RequestRollDiceLocal((int)_selectedDiceParity, gaugePermille);
        }
    }

    /// <summary>
    /// 주사위 컨트롤 게이지 갱신.
    /// 확정 버튼을 누르고 있는 동안만 0~1 사이를 왕복한다.
    /// </summary>
    private void RefreshDiceControlGauge()
    {
        if (!_diceControlOpen)
            return;

        if (!_diceGaugeHolding)
            return;

        float delta = Time.deltaTime * diceGaugeSpeed;

        if (_diceGaugeForward)
        {
            _diceGaugeValue += delta;

            if (_diceGaugeValue >= 1f)
            {
                _diceGaugeValue = 1f;
                _diceGaugeForward = false;
            }
        }
        else
        {
            _diceGaugeValue -= delta;

            if (_diceGaugeValue <= 0f)
            {
                _diceGaugeValue = 0f;
                _diceGaugeForward = true;
            }
        }

        SetDiceGaugeValue(_diceGaugeValue);
    }

    /// <summary>
    /// 게임 상태에서 새 주사위 결과가 들어왔는지 감지하고,
    /// 들어왔으면 연출 UI를 최종 결과로 멈춘다.
    /// </summary>
    private void RefreshDiceRollResult()
    {
        if (!_waitingDiceResult)
            return;

        BulmabulGameState state = BulmabulGameState.Instance;
        if (state == null)
            return;

        // 아래는 예시다.
        // 네 BulmabulGameState에 "최근 주사위 결과"를 가져오는 방식이 있어야 한다.
        if (!state.TryGetLatestDiceResultForLocalPlayer(out int rollVersion, out int dice1, out int dice2))
            return;

        // 이미 처리한 결과면 무시
        if (rollVersion == _lastHandledRollVersion)
            return;

        _lastHandledRollVersion = rollVersion;
        _waitingDiceResult = false;

        if (diceRollingUI != null)
        {
            OwnedDice equippedDice = GetLocalEquippedDice();

            // 주사위 UI는 결과를 보여주고 resultShowTime 이후 자동으로 꺼진다.
            // Pawn 이동은 GameState가 정해진 딜레이 후 RPC_PlayPawnMoveVisual로 처리한다.
            diceRollingUI.StopWithResult(dice1, dice2, equippedDice, null);
        }
    }

    /// <summary>
    /// 게이지 UI 값 적용.
    /// Image Filled 방식 기준.
    /// </summary>
    private void SetDiceGaugeValue(float value)
    {
        _diceGaugeValue = Mathf.Clamp01(value);

        if (diceControlGaugeFill != null)
            diceControlGaugeFill.fillAmount = _diceGaugeValue;
    }

    /// <summary>
    /// 주사위 컨트롤 안내 문구 갱신.
    /// </summary>
    private void RefreshDiceControlInfo()
    {
        if (txtDiceControlInfo == null)
            return;

        if (_selectedDiceParity == BulmabulDiceParityChoice.None)
        {
            txtDiceControlInfo.text = GetByLanguage(
                "홀수 또는 짝수를 선택하세요.",
                "Choose odd or even."
            );
        }
        else if (_selectedDiceParity == BulmabulDiceParityChoice.Odd)
        {
            txtDiceControlInfo.text = GetByLanguage(
                "홀수를 선택했습니다. 확정 버튼을 누르고 있다가 원하는 타이밍에 떼세요.",
                "Odd selected. Hold Confirm and release at the right timing."
            );
        }
        else
        {
            txtDiceControlInfo.text = GetByLanguage(
                "짝수를 선택했습니다. 확정 버튼을 누르고 있다가 원하는 타이밍에 떼세요.",
                "Even selected. Hold Confirm and release at the right timing."
            );
        }
    }

    /// <summary>
    /// 주사위 컨트롤 UI의 고정 문구를 현재 언어에 맞게 갱신한다.
    /// </summary>
    private void RefreshDiceControlStaticTexts()
    {
        if (txtDiceControlTitle != null)
            txtDiceControlTitle.text = GetByLanguage("주사위 컨트롤", "Dice Control");

        if (txtDiceOddButton != null)
            txtDiceOddButton.text = GetByLanguage("홀수", "Odd");

        if (txtDiceEvenButton != null)
            txtDiceEvenButton.text = GetByLanguage("짝수", "Even");

        if (txtDiceConfirmButton != null)
            txtDiceConfirmButton.text = GetByLanguage("확정", "Confirm");

        if (txtDiceGaugeLabel != null)
            txtDiceGaugeLabel.text = GetByLanguage(
                "확정 버튼을 누르고 있다가 원하는 타이밍에 떼세요",
                "Hold Confirm and release at the right timing"
            );
    }

    /// <summary>
    /// 현재 로그인 계정의 장착 주사위를 가져온다.
    /// 없으면 null 반환.
    /// </summary>
    private OwnedDice GetLocalEquippedDice()
    {
        if (FireBaseAuthManager.Instance == null)
            return null;

        Account acc = FireBaseAuthManager.Instance.CurrentAccount;

        if (acc == null)
            return null;

        // 혹시 장착 정보가 비어있으면 복원 시도
        if (acc.EquippedDice == null)
            DiceEquipService.RestoreEquippedDice(acc);

        return acc.EquippedDice;
    }

    /// <summary>
    /// 우측 상단 Exit 버튼 클릭.
    /// 바로 나가지 않고 확인 패널을 먼저 띄운다.
    /// </summary>
    private void OnClickExit()
    {
        RefreshExitTexts();

        if (exitConfirmPanel != null)
            exitConfirmPanel.SetActive(true);
    }

    /// <summary>
    /// 게임 나가기 확인 패널 문구 갱신.
    /// </summary>
    private void RefreshExitTexts()
    {
        if (txtExitTitle != null)
        {
            txtExitTitle.text = GetByLanguage(
                "게임 나가기",
                "Leave Game"
            );
        }

        if (txtExitMessage != null)
        {
            txtExitMessage.text = GetByLanguage(
                "게임을 나가면 패배 처리됩니다.\n정말 나가시겠습니까?",
                "Leaving the game will count as a defeat.\nDo you really want to leave?"
            );
        }
    }

    /// <summary>
    /// 나가기 취소.
    /// </summary>
    private void OnClickExitNo()
    {
        if (exitConfirmPanel != null)
            exitConfirmPanel.SetActive(false);
    }

    /// <summary>
    /// 나가기 확정.
    /// 내 플레이어를 패배 처리한 뒤 룸에서 나간다.
    /// </summary>
    private void OnClickExitYes()
    {
        if (_leavingGame)
            return;

        if (exitConfirmPanel != null)
            exitConfirmPanel.SetActive(false);

        StartCoroutine(CoLeaveGameAfterDefeatRequest());
    }

    private IEnumerator CoLeaveGameAfterDefeatRequest()
    {
        _leavingGame = true;

        if (ToastMessageManager.instance != null)
        {
            ToastMessageManager.instance.ShowToast(
                "게임에서 나가 패배했습니다. 잠시 후 로비로 이동합니다.",
                "You left the game and were defeated. Returning to the lobby shortly."
            );
        }

        BulmabulGameState state = BulmabulGameState.Instance;

        if (state != null)
        {
            // 서버에 이탈 패배 요청
            state.RequestLeaveGameLocal();
        }

        // 로컬 계정에 이탈 기록 적용
        // 오늘 이탈 5회째부터만 재화/시간 패널티 적용
        _ = BulmabulPenaltyService.ApplyLocalLeaveRecordAsync();

        // 토스트를 볼 수 있도록 잠깐 대기
        yield return new WaitForSeconds(2f);

        if (NetWorkLauncher.instance != null)
        {
            _ = NetWorkLauncher.instance.LeaveRoomToLobby(1);
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
        }
    }

    private void OnClickBuyLand()
    {
        BulmabulGameState state = BulmabulGameState.Instance;
        if (state == null) return;

        if (state.TryGetPendingBuyLackAmount(out int lackAmount))
        {
            ShowLackMoneyToast(lackAmount);
            return;
        }

        state.RequestBuyLandLocal();
    }

    /// <summary>
    /// 재화 부족 토스트 출력.
    /// </summary>
    private void ShowLackMoneyToast(int lackAmount)
    {
        if (ToastMessageManager.instance == null)
            return;

        ToastMessageManager.instance.ShowToast(
            $"재화가 {lackAmount:N0} 부족합니다.",
            $"Not enough money. Need {lackAmount:N0} more."
        );
    }

    private void OnClickSkipBuyLand()
    {
        BulmabulGameState state = BulmabulGameState.Instance;
        if (state == null) return;

        state.RequestSkipBuyLandLocal();
    }

    private void OnClickBuildSmallHouse()
    {
        RequestBuild(BulmabulBuildPart.SmallHouse);
    }

    private void OnClickBuildHouse()
    {
        RequestBuild(BulmabulBuildPart.House);
    }

    private void OnClickBuildBigHouse()
    {
        RequestBuild(BulmabulBuildPart.BigHouse);
    }

    private void OnClickBuildHotel()
    {
        RequestBuild(BulmabulBuildPart.Hotel);
    }

    private void RequestBuild(BulmabulBuildPart part)
    {
        BulmabulGameState state = BulmabulGameState.Instance;
        if (state == null)
            return;

        if (state.TryGetPendingBuildLackAmount(part, out int lackAmount))
        {
            ShowLackMoneyToast(lackAmount);
            return;
        }

        state.RequestBuildLocal(part);
    }

    private void OnClickSkipBuild()
    {
        BulmabulGameState state = BulmabulGameState.Instance;
        if (state == null) return;

        state.RequestSkipBuildLocal();
    }

    private void OnClickBuildTarget(int cellIndex)
    {
        BulmabulGameState state = BulmabulGameState.Instance;
        if (state == null) return;

        state.RequestSelectBuildTargetLocal(cellIndex);
    }

    private void OnClickTravelMove()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        if (!state.CanLocalUseTravel())
            return;

        if (travelSelectPanel != null)
            travelSelectPanel.SetActive(true);
    }

    private void OnClickTravelTarget(int targetCellIndex)
    {
        BulmabulGameState state = BulmabulGameState.Instance;
        if (state == null) return;

        if (travelSelectPanel != null)
            travelSelectPanel.SetActive(false);

        state.RequestTravelMoveLocal(targetCellIndex);
    }

    private void OnClickPauseResume()
    {
        BulmabulGameState state = BulmabulGameState.Instance;
        if (state == null) return;

        state.RequestPauseResumeLocal();
    }

    private async void RefreshSingleProfileImage(int playerIndex, BulmabulPlayerInfoUI ui, string photoUrl)
    {
        if (ui == null)
            return;

        RawImage image = ui.ProfileImage;

        if (image == null)
            return;

        if (string.IsNullOrWhiteSpace(photoUrl))
        {
            ui.SetProfileTexture(ui.DefaultProfileTexture);
            _loadedProfileUrls[playerIndex] = "";
            return;
        }

        if (_loadedProfileUrls.TryGetValue(playerIndex, out string loadedUrl) &&
            loadedUrl == photoUrl &&
            image.texture != null)
        {
            return;
        }

        _loadedProfileUrls[playerIndex] = photoUrl;

        Texture2D texture = await FriendProfileImageCache.GetAsync(photoUrl);

        if (this == null || ui == null)
            return;

        ui.SetProfileTexture(texture);
    }

    /// <summary>
    /// 전체 맵 보기 버튼 클릭.
    /// 현재 Pawn 따라가기 상태면 90도 전체 맵 보기로 전환하고,
    /// 전체 맵 보기 상태면 다시 45도 Pawn 따라가기로 돌아간다.
    /// </summary>
    private void OnClickMapView()
    {
        if (cameraFollow == null)
            return;

        cameraFollow.ToggleFullMapView();
        RefreshMapViewButtonText();
    }

    /// <summary>
    /// 전체 맵 보기 버튼 문구 갱신.
    /// 한국어 / 영어를 현재 언어에 맞게 표시한다.
    /// </summary>
    private void RefreshMapViewButtonText()
    {
        if (txtMapViewButton == null)
            return;

        if (cameraFollow == null)
        {
            txtMapViewButton.text = GetByLanguage("전체 맵", "Full Map");
            return;
        }

        if (cameraFollow.IsFullMapView)
        {
            txtMapViewButton.text = GetByLanguage("말 보기", "Pawn View");
        }
        else
        {
            txtMapViewButton.text = GetByLanguage("전체 맵", "Full Map");
        }
    }
}
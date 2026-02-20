using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public enum ShopUi
{
    Dice,Coin
}

public class ShopUiManager : MonoBehaviour
{
    [Header("주사위 뽑기, 재화 구매 버튼")]
    [SerializeField] Button[] btnShops;

    [Header("주사위 뽑기, 재화 구매 오브젝트 들")]
    [SerializeField] GameObject[] shopObjects;

    [Header("주사위 1회/10회 버튼 (0=1회, 1=10회)")]
    [SerializeField] Button[] btnDices;


    [Header("뽑기 횟수 50회에서 SR 보장 텍스트(남은 횟수)")]
    [SerializeField] Text txtPick;

    [Header("확인 팝업")]
    [SerializeField] GameObject pullConfirmPopup;
    [SerializeField] Text txtConfirmDesc;
    [SerializeField] Button btnConfirmClose;
    [SerializeField] GameObject doubleClickArea;

    [SerializeField] GameObject noSkipRoot; // 0번 패널
    [SerializeField] GameObject skipRoot;   // 1번 패널


    [Header("더블클릭 연출 컨트롤러(선택)")]
    [SerializeField] DiceClickRevealFX diceRevealFx; // 아래 스크립트

    [Header("결과 팝업")]
    [SerializeField] DicePullResultPopup resultPopup;

    [Header("현재 샵 정보,주사위 뽑기인지 아니면 재화 뽑기(코인 뽑기)")]
    [SerializeField] ShopUi shop;

    [SerializeField] Toggle toggleSkip; // 인스펙터 연결

    const int ONE_COST = 100;

    static int TEN_COST => (ONE_COST * 10) - ONE_COST;

    int _pendingPullCount = 0;
    bool _isRolling = false;

    DoubleClickTrigger _dc;
    FireBaseAuthManager _auth;

    private void Awake()
    {
        shop = ShopUi.Dice;
        for (int i = 0; i < btnShops.Length; i++)
        {
            int idx = i; // 클로저 방지
            btnShops[i].onClick.AddListener(() => ShopUiClick(idx));
        }
        _auth = FireBaseAuthManager.Instance;
        // 뽑기 버튼 연결 + 가격 텍스트 세팅
        ApplyShopUI(shop);

        SetupDiceButtons();
        SetupConfirmPopup();
    }

    private void OnEnable()
    {
        RefreshPityText(_auth.CurrentAccount);
    }

    #region 주사위 뽑기 함수

    bool IsSkipOn
    {
        get => (toggleSkip != null && toggleSkip.isOn);
    }

    void ShopUiClick(int num)
    {
        shop = (ShopUi)num;
        ApplyShopUI(shop);
    }

    void ApplyShopUI(ShopUi target)
    {
        for (int i = 0; i < shopObjects.Length; i++)
            shopObjects[i].SetActive(i == (int)target);
    }

    void SetupDiceButtons()
    {
        if (btnDices == null || btnDices.Length < 2) return;

        btnDices[0].onClick.AddListener(() => OnPressPullButton(1));
        btnDices[1].onClick.AddListener(() => OnPressPullButton(10));
    }

    void SetupConfirmPopup()
    {
        if (pullConfirmPopup != null) pullConfirmPopup.SetActive(false);

        if (btnConfirmClose != null)
            btnConfirmClose.onClick.AddListener(CloseConfirmPopup);

        if (doubleClickArea != null)
        {
            _dc = doubleClickArea.GetComponent<DoubleClickTrigger>();
            if (_dc == null) _dc = doubleClickArea.AddComponent<DoubleClickTrigger>();
            _dc.onDoubleClick = () => _ = OnConfirmDoubleClickAsync();
            _dc.DisOpen(); // 시작은 닫힘 상태
        }
    }

    void OnPressPullButton(int pullCount)
    {
        if (_isRolling) return;

        if (!CanPullOrToast(pullCount)) return;

        // 스킵 ON이면 "바로 뽑기"
        if (IsSkipOn)
        {
            _ = TryPullAsync(pullCount, skipReveal: true);
            return;
        }

        // 스킵 OFF면 확인창(0번 자식) 띄우고 더블클릭 대기
        OpenConfirmPopup_NoSkip(pullCount);
    }


    bool CanPullOrToast(int pullCount)
    {
        var auth = _auth;
        if (auth == null || auth.CurrentAccount == null)
        {
            ToastMessageManager.instance.ShowToast("계정 정보가 없습니다.", "No account data.");
            return false;
        }

        int cost = (pullCount == 10) ? TEN_COST : ONE_COST;
        float cash = auth.CurrentAccount.Cash;

        // NaN 방어(가끔 로드 꼬이면 NaN 나와서 비교가 항상 false가 될 수 있음)
        if (float.IsNaN(cash) || cash < cost)
        {
            float lack = float.IsNaN(cash) ? cost : (cost - cash);
            ToastMessageManager.instance.ShowToast(
                $"재화가 부족합니다. {lack:0}원 부족해요.",
                $"Not enough currency. You need {lack:0} more."
            );
            return false;
        }

        return true;
    }

    void OpenConfirmPopup_NoSkip(int pullCount)
    {
        _pendingPullCount = pullCount;

        if (pullConfirmPopup != null)
            pullConfirmPopup.SetActive(true);

        SetConfirmPopupMode(skipMode: false);

        // 매번 새 세션 오픈 (중요)
        if (_dc != null) _dc.Open();

        // 연출 초기화 + 흔들림 시작
        if (diceRevealFx != null)
        {
            diceRevealFx.ResetForNewPull();
            diceRevealFx.BeginIdle();
        }


        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        if (txtConfirmDesc != null)
        {
            txtConfirmDesc.text = (lang == Lauaguage.Eng)
                ? "Double-click to start."
                : "더블클릭하면 뽑기를 시작합니다.";
        }
    }

    void SetConfirmPopupMode(bool skipMode)
    {
        if (noSkipRoot != null) noSkipRoot.SetActive(!skipMode);
        if (skipRoot != null) skipRoot.SetActive(skipMode);
    }


    void CloseConfirmPopup()
    {
        _pendingPullCount = 0;

        if (_dc != null) _dc.DisOpen();

        // EndIdle + 크기원복까지 포함
        if (diceRevealFx != null)
            diceRevealFx.ResetForNewPull();

        if (pullConfirmPopup != null)
            pullConfirmPopup.SetActive(false);
    }

    async Task OnConfirmDoubleClickAsync()
    {
        if (_isRolling) return;
        if (_pendingPullCount != 1 && _pendingPullCount != 10) return;

        if (_dc != null) _dc.DisOpen();

        // 더블클릭 시: 800x800 확대 + crack 연출 먼저
        if (diceRevealFx != null)
        {
            await diceRevealFx.PlayCrackAndBurstAsync();
            SetConfirmPopupMode(skipMode: true);
        }

        await TryPullAsync(_pendingPullCount, skipReveal: false);
    }

    async Task TryPullAsync(int pullCount, bool skipReveal)
    {
        if (_isRolling) return;
        _isRolling = true;

        try
        {
            var auth = _auth;
            if (auth == null || auth.CurrentAccount == null)
            {
                ToastMessageManager.instance.ShowToast("계정 정보가 없습니다.", "No account data.");
                return;
            }

            int cost = (pullCount == 10) ? TEN_COST : ONE_COST;
            float cash = auth.CurrentAccount.Cash;

            // CSV 로드
            DiceTables.LoadOrThrow();

            CloseConfirmPopup();

            if (resultPopup != null)
                resultPopup.Open(pullCount);

            // 뽑기 결과
            var rolledList = new List<Dice>();
            if (pullCount == 1)
            {
                rolledList.Add(RollWithPity(auth.CurrentAccount, PullType.Single));
            }
            else
            {
                for (int i = 0; i < 9; i++)
                    rolledList.Add(RollWithPity(auth.CurrentAccount, PullType.Ten_1to9));

                rolledList.Add(RollWithPity(auth.CurrentAccount, PullType.Ten_Slot10_Guarantee));
            }

            bool wasPity49 = (auth.CurrentAccount.DicePityCount >= 49);

            // "이번 뽑기 단위"로 pity 최종값 고정 (SR 나오면 무조건 0)
            ApplyPityAfterBatch(auth.CurrentAccount, pullCount, rolledList, wasPity49);

            // 결과 표시
            if (resultPopup != null)
            {
                if (skipReveal) resultPopup.ShowResultsInstant(rolledList);
                else await resultPopup.PlayEffectThenShowAsync(rolledList);
            }

            // 재화 차감
            auth.CurrentAccount.Cash = Mathf.Max(0, auth.CurrentAccount.Cash - cost);

            auth.CurrentAccount.DiceTotalPullCount += pullCount;

            // 인벤 반영(여기서 pity도 이미 RollWithPity에서 누적됨)
            foreach (var d in rolledList)
                DiceTables.AddToAccountInventory(auth.CurrentAccount, d);

            //  먼저 pity 텍스트 갱신 (현재 acc 기준)
            RefreshPityText(auth.CurrentAccount);

            // 그 다음 전체 UI(캐시 등) 갱신
            LobbyUIManager.instance.RefreshPlayerUI();

            // 마지막에 저장
            await AccountCloudStore.SaveFullAsync(auth.CurrentAccount);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            ToastMessageManager.instance.ShowToast("뽑기 처리 중 오류", "Error during pull.");
        }
        finally
        {
            _isRolling = false; // 무조건 풀림
        }
    }

    void ApplyPityAfterBatch(Account acc, int pullCount, List<Dice> rolledList, bool wasPity49)
    {
        if (acc == null || rolledList == null || rolledList.Count == 0) return;

        bool anyEpic = false;
        for (int i = 0; i < rolledList.Count; i++)
        {
            if (rolledList[i].grade >= DiceGrade.Epic)
            {
                anyEpic = true;
                break;
            }
        }

        if (anyEpic)
        {
            // SR(Epic)+가 하나라도 나오면 리셋
            acc.DicePityCount = 0;
            return;
        }

        // SR이 하나도 안 나왔음
        if (wasPity49)
        {
            // 천장 보장: "이번 배치"에서 1개를 Epic 이상으로 강제 교체
            int idx = UnityEngine.Random.Range(0, rolledList.Count);

            // Epic 이상이 나올 때까지 재뽑 (안전장치)
            const int GUARD = 100;
            for (int g = 0; g < GUARD; g++)
            {
                // ten이면 보장슬롯 테이블을 써주는 게 가장 깔끔
                // (너가 이미 Ten_Slot10_Guarantee가 있으니까 그걸 이용)
                Dice forced = DiceTables.RollByPullType(PullType.Ten_Slot10_Guarantee);

                if (forced.grade >= DiceGrade.Epic)
                {
                    rolledList[idx] = forced;
                    break;
                }
            }

            // 보장 성공했으니 리셋
            acc.DicePityCount = 0;
        }

        else
        {
            // 이번 뽑기에서 SR이 하나도 없으면 뽑은 횟수만큼 누적
            acc.DicePityCount = Mathf.Clamp(acc.DicePityCount + pullCount, 0, 49);
        }
    }

    Dice RollWithPity(Account acc, PullType pullType)
    {
        // 너 rules에 dicePityCount 저장하도록 뚫어놨으니 Account에도 int로 들고있거나
        // accountEnc에 포함시키거나(권장) 해야 함.
        // 여기서는 acc에 "int DicePityCount" 필드가 있다고 가정하기 어려워서
        // 예시: accountEnc 내부에 있다면 그걸 연결해줘.
        // ----
        // 그래서 아래는 "필드가 있다고 가정한 샘플"이야:
        // acc.DicePityCount (0~49)

        // ⭐ 너 Account에 아래 필드 추가 추천:
        // [SerializeField] private int dicePityCount;
        // public int DicePityCount { get => dicePityCount; set => dicePityCount = Mathf.Clamp(value, 0, 49); }

        int pity = GetPityCount(acc); // <- 아래 헬퍼
        bool pityNow = (pity >= 49);

        Dice d = DiceTables.RollByPullType(pullType);

        // DiceTables.RollFrom이 private라면:
        // - 너가 RollDiceTen을 쓰고 있으니, Ten은 그냥 RollDiceTen으로 뽑고
        // - 1~9/10번째 개별 처리하고 싶으면 DiceTables에 public wrapper 하나 추가하는 게 제일 깔끔해.
        // 여기서는 "설명용"으로 썼고, 실제로는:
        // 1) single -> RollDiceSingle()
        // 2) ten -> RollDiceTen()으로 한 번에 받고 pity는 결과로 판단해서 pityCount만 업데이트
        // 로 단순화해도 돼.

        // pity 발동이면 Epic 이상 나올 때까지 재추첨 (안전장치 30회)
        if (pityNow)
        {
            int guard = 30;
            while (d.grade < DiceGrade.Epic && guard-- > 0)
            {
                d = (pullType == PullType.Single)
                    ? DiceTables.RollDiceSingle()
                    : DiceTables.RollByPullType(pullType);
            }
        }

        // pityCount 갱신    
        return d;
    }

    // ===== pityCount 저장을 Account에 연결하기 위한 헬퍼 =====
    // 네가 Account에 DicePityCount를 추가하면 아래 두 함수는 한 줄로 끝남.
    int GetPityCount(Account acc)
    {
        return Mathf.Clamp(acc.DicePityCount, 0, 49);
    }

    void RefreshPityText(Account acc)
    {
        if (txtPick == null) return;
        if (acc == null) return;

        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        int pity = Mathf.Clamp(acc.DicePityCount, 0, 49);
        int remain = Mathf.Clamp(50 - pity, 1, 50);

        txtPick.text = (lang == Lauaguage.Eng)
            ? $"SR(Epic) guaranteed in {remain}"
            : $"SR(Epic) 보장까지 {remain}회";

        string oneText = (lang == Lauaguage.Eng) ? $"Single {ONE_COST}" : $"1회 뽑기 {ONE_COST}원";
        string tenText = (lang == Lauaguage.Eng) ? $"10 Pull {TEN_COST}" : $"10회 뽑기 {TEN_COST}원";

        SetButtonChildText(btnDices[0], oneText);
        SetButtonChildText(btnDices[1], tenText);
    }

    void SetButtonChildText(Button btn, string value)
    {

        if (btn == null) return;
        var t = btn.GetComponentInChildren<Text>(true);
        if (t != null) t.text = value;
    }
    #endregion
}

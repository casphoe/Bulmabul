using Fusion;
using UnityEngine;

/// <summary>
/// 테스트 전용 보관 카드 지급 핫키.
/// 
/// 게임창에서 숫자키 7번을 누르면,
/// 현재 내 턴일 때만 천사 카드 / 감옥 탈출 카드 / 여행 카드를 각각 1장씩 지급한다.
/// 
/// 주의:
/// - 테스트 전용 스크립트다.
/// - 빌드 전에는 비활성화하거나 제거하는 것을 권장한다.
/// - 반드시 NetworkObject가 붙은 오브젝트에 붙여야 RPC가 정상 동작한다.
/// - BulmabulGameState와 같은 오브젝트에 붙이는 것을 추천한다.
/// </summary>
public class BulmabulDebugGrantKeptCardsHotkey : NetworkBehaviour
{
    [Header("Debug Hotkey")]
    [SerializeField] private bool enableDebugHotkey = true;

    [SerializeField] private KeyCode grantKey = KeyCode.Alpha7;

    [Header("Options")]
    [Tooltip("true면 내 턴일 때만 테스트 카드 지급 가능")]
    [SerializeField] private bool onlyMyTurn = true;

    [Tooltip("true면 일시정지 중에는 지급 불가")]
    [SerializeField] private bool blockWhenPaused = true;

    [Tooltip("true면 턴 처리 중에는 지급 불가")]
    [SerializeField] private bool blockWhenTurnBusy = false;

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!enableDebugHotkey)
            return;

        if (!Input.GetKeyDown(grantKey))
            return;

        TryRequestGrantKeptCards();
#endif
    }

    private void TryRequestGrantKeptCards()
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
        {
            Debug.LogWarning("[DebugGrantCards] BulmabulGameState.Instance가 없습니다.");
            return;
        }

        if (!state.IsSpawnReady)
        {
            Debug.LogWarning("[DebugGrantCards] GameState가 아직 SpawnReady 상태가 아닙니다.");
            return;
        }

        if (state.Runner == null)
        {
            Debug.LogWarning("[DebugGrantCards] NetworkRunner가 없습니다.");
            return;
        }

        if (state.GameFinished)
        {
            Debug.LogWarning("[DebugGrantCards] 게임이 종료된 상태라 테스트 카드를 지급하지 않습니다.");
            return;
        }

        if (blockWhenPaused && state.IsPaused)
        {
            Debug.LogWarning("[DebugGrantCards] 게임 일시정지 중이라 테스트 카드를 지급하지 않습니다.");
            return;
        }

        if (blockWhenTurnBusy && state.TurnBusy)
        {
            Debug.LogWarning("[DebugGrantCards] 턴 처리 중이라 테스트 카드를 지급하지 않습니다.");
            return;
        }

        if (onlyMyTurn && !state.IsMyTurn())
        {
            Debug.LogWarning("[DebugGrantCards] 내 턴이 아니라서 테스트 카드를 지급하지 않습니다.");

            if (ToastMessageManager.instance != null)
            {
                ToastMessageManager.instance.ShowToast(
                    "내 턴일 때만 테스트 카드를 받을 수 있습니다.",
                    "You can receive test cards only on your turn."
                );
            }

            return;
        }

        int localPlayerIndex = state.FindPlayerIndex(state.Runner.LocalPlayer);

        if (localPlayerIndex < 0)
        {
            Debug.LogWarning("[DebugGrantCards] 로컬 플레이어 인덱스를 찾지 못했습니다.");
            return;
        }

        RPC_RequestGrantKeptCards(localPlayerIndex);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestGrantKeptCards(int requestPlayerIndex, RpcInfo info = default)
    {
        BulmabulGameState state = BulmabulGameState.Instance;

        if (state == null)
            return;

        if (!Object.HasStateAuthority)
            return;

        if (!state.IsSpawnReady)
            return;

        if (state.GameFinished)
            return;

        if (requestPlayerIndex < 0 || requestPlayerIndex >= BulmabulGameState.MaxPlayers)
            return;

        BulmabulGameState.PlayerGameSlot slot = state.Players.Get(requestPlayerIndex);

        if (slot.occupied == 0)
            return;

        if (slot.bankrupt)
            return;

        if (slot.leftGame)
            return;

        /*
         * 요청한 플레이어와 실제 RPC 요청자가 같은지 검증.
         * 다른 클라이언트가 남의 카드 상태를 바꾸는 것을 방지한다.
         */
        bool validRequester = info.Source == slot.player;

        /*
         * 호스트 플레이 상황에서 Source 검증이 꼬일 수 있는 경우를 위한 보조 방어.
         */
        if (!validRequester && state.Runner != null && state.Runner.LocalPlayer == slot.player)
            validRequester = true;

        if (!validRequester)
        {
            Debug.LogWarning(
                $"[DebugGrantCards] 요청자 검증 실패. requestPlayerIndex={requestPlayerIndex}, Source={info.Source}, slot.player={slot.player}"
            );
            return;
        }

        if (onlyMyTurn && state.CurrentTurnIndex != requestPlayerIndex)
        {
            Debug.LogWarning(
                $"[DebugGrantCards] 현재 턴이 아니라 지급 거부. current={state.CurrentTurnIndex}, request={requestPlayerIndex}"
            );
            return;
        }

        if (blockWhenPaused && state.IsPaused)
            return;

        if (blockWhenTurnBusy && state.TurnBusy)
            return;

        /*
         * 보관 카드는 bool 구조라 플레이어당 최대 1장이다.
         * 이미 가지고 있으면 그대로 true 유지.
         */
        slot.hasAngelCard = true;
        slot.hasJailEscapeCard = true;
        slot.hasTravelCard = true;

        state.Players.Set(requestPlayerIndex, slot);

        /*
         * 카드 인벤토리 UI가 Revision을 보고 갱신하므로 직접 증가시킨다.
         */
        state.Revision = state.Revision + 1;

        state.RPC_ShowPawnFloatingText(
            requestPlayerIndex,
            "테스트 카드 지급",
            "Test Cards Granted",
            4
        );

        Debug.Log(
            $"[DebugGrantCards] {requestPlayerIndex}번 플레이어에게 테스트 보관 카드 지급 완료. " +
            "Angel=1, JailEscape=1, Travel=1"
        );
    }
}
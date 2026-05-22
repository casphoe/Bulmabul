/// <summary>
/// 찬스 카드 효과 종류.
/// 즉시 실행 카드와 보관형 카드를 모두 포함한다.
/// </summary>
public enum BulmabulChanceCardType
{
    None = 0,

    // 즉시 실행
    ReceiveMoney,        // 은행에서 돈 받기
    PayMoney,            // 은행에 돈 내기
    MoveToStart,         // 시작지점으로 이동
    MoveToJail,          // 감옥으로 이동
    MoveForward,         // 앞으로 N칸 이동
    MoveBackward,        // 뒤로 N칸 이동
    PayToAllPlayers,     // 모든 플레이어에게 돈 지급
    ReceiveFromAllPlayers, // 모든 플레이어에게 돈 받기

    /// <summary>
    /// 현재 위치 기준으로 앞으로 가장 가까운 적 소유 땅으로 이동.
    /// 개인전: 다른 생존 플레이어의 땅.
    /// 팀전: 같은 팀 땅 제외, 상대 팀 소유 땅.
    /// </summary>
    MoveToNearestEnemyLand,

    // 보관 후 사용
    AngelCard,           // 통행료/벌금 방어
    JailEscapeCard,      // 감옥 탈출
    /// <summary>
    /// 여행 칸으로 이동하는 보관 카드.
    /// 기존 FreeTravelCard를 대체한다.
    /// </summary>
    MoveToTravelCard
}

/// <summary>
/// 찬스 카드가 바로 실행되는지, 보관되는지 구분.
/// </summary>
public enum BulmabulChanceCardUseType
{
    Immediate = 0,   // 즉시 실행
    Keep = 1         // 보관 후 나중에 사용
}
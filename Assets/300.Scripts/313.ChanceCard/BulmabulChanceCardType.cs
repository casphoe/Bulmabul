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

    // 보관 후 사용
    AngelCard,           // 통행료/벌금 방어
    JailEscapeCard,      // 감옥 탈출
    FreeTravelCard       // 원하는 위치 이동
}

/// <summary>
/// 찬스 카드가 바로 실행되는지, 보관되는지 구분.
/// </summary>
public enum BulmabulChanceCardUseType
{
    Immediate = 0,   // 즉시 실행
    Keep = 1         // 보관 후 나중에 사용
}
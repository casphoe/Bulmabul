
//로그인 할 계정의 대한 정보가 담길 클래스
using System;
using System.Collections.Generic;

[Serializable]
public class Account
{
    //해당 계정 이름
    public string Name { get; set; }

    //해당 계정 닉네임
    public string NickName { get; set; }

    //이메일 계정
    public string Email { get; set; }

    //마지막 로그인 날짜
    public string LoginDate { get; set; }

    //마지막 로그아웃 날짜
    public string LogoutDate { get; set; }

    //계정 재화(돈)
    public float Money { get; set; }

    #region 출석 (월간)
    // 마지막으로 "출석 처리"한 날짜 (yyyy-MM-dd)
    public string LastAttendanceDate { get; set; }

    // 지금 저장된 출석 데이터가 어느 달인지 (yyyy-MM)
    public string AttendanceMonthKey { get; set; }

    // 이번 달 누적 출석 일수 (1일 1회만 증가)
    public int AttendanceCountThisMonth { get; set; }

    // 이번 달에서 보상 수령한 "일차"들 (예: 1,2,5일차 보상 수령)
    public List<int> ClaimedAttendanceDays { get; set; } = new List<int>();
    #endregion

    #region 주사위 인벤토리
    // 보유 주사위 목록 (grade/star/level + count)
    public List<OwnedDice> DiceInventory { get; set; } = new List<OwnedDice>();

    // 현재 장착/선택한 주사위(없으면 null/빈값)
    public OwnedDice EquippedDice { get; set; }
    #endregion
}

[Serializable]
public class OwnedDice
{
    public DiceGrade Grade;
    public int Star;   // 1..5
    public int Level;  // 1..10

    public int Count;  // 보유 개수(중복 관리용)
    public int Exp; //경험치

    public string Key => $"{Grade}|{Star}|{Level}";
}

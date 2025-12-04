
//로그인 할 계정의 대한 정보가 담길 클래스
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Account
{
    // ===== 기본 정보 =====

    [Header("기본 정보")]
    [SerializeField] private string name;   // 실명/표시명 (예: 회원가입 시 입력한 이름)
    public string Name
    {
        get => name;
        set => name = value?.Trim();
    }

    [SerializeField] private string nickName; // 게임 내 표시 닉네임(중복 체크 대상)
    public string NickName
    {
        get => nickName;
        set => nickName = value?.Trim();
    }

    [SerializeField] private string email;  // 로그인 이메일(표시/고객지원/복구용)
    public string Email
    {
        get => email;
        set => email = value?.Trim();
    }

    [Header("로그인 기록")]

    [SerializeField] private string loginDate;   // 마지막 로그인 날짜 (yyyy-MM-dd HH:mm:ss)
    public string LoginDate
    {
        get => loginDate;
        set => loginDate = value;
    }

    [SerializeField] private string logoutDate;  // 마지막 로그아웃 날짜 (yyyy-MM-dd HH:mm:ss)
    public string LogoutDate
    {
        get => logoutDate;
        set => logoutDate = value;
    }

    [Header("부루마불 재화")]
    [SerializeField] private float cash; // 부루마불에서 사용하는 현금(캐시)
    public float Cash 
    { 
        get => cash; set => cash = value;
    }

    // 출석 (월간)
    [SerializeField] private string lastAttendanceDate;
    public string LastAttendanceDate
    {
        get => lastAttendanceDate;
        set => lastAttendanceDate = value;
    }

    [SerializeField] private string attendanceMonthKey;
    public string AttendanceMonthKey
    {
        get => attendanceMonthKey;
        set => attendanceMonthKey = value;
    }

    [SerializeField] private int attendanceCountThisMonth;
    public int AttendanceCountThisMonth
    {
        get => attendanceCountThisMonth;
        set => attendanceCountThisMonth = value;
    }

    //  List는 null로 들어올 수도 있으니, backing field + getter에서 null 방어를 권장
    [SerializeField] private List<int> claimedAttendanceDays = new List<int>();
    public List<int> ClaimedAttendanceDays
    {
        get => claimedAttendanceDays ??= new List<int>();
        set => claimedAttendanceDays = value;
    }

    // 주사위 인벤토리
    [SerializeField] private List<OwnedDice> diceInventory = new List<OwnedDice>();
    public List<OwnedDice> DiceInventory
    {
        get => diceInventory ??= new List<OwnedDice>();
        set => diceInventory = value;
    }

    [SerializeField] private OwnedDice equippedDice;
    public OwnedDice EquippedDice
    {
        get => equippedDice;
        set => equippedDice = value;
    }

    [Header("계정 레벨 / 경험치")]
    [SerializeField] private int accountLevel = 1;   // 기본 1레벨
    public int AccountLevel { get => accountLevel; set => accountLevel = Mathf.Max(1, value); }

    [SerializeField] private long accountExp = 0;    // 누적 경험치(크면 int 넘을 수 있어 long 추천)
    public long AccountExp { get => accountExp; set => accountExp = Math.Max(0, value); }

}

[Serializable]
public class OwnedDice
{
    [SerializeField] private DiceGrade grade; // 등급 (Common/Rare/...)
    public DiceGrade Grade
    {
        get => grade;
        set => grade = value;
    }

    [SerializeField] private int star;   // 1..5
    public int Star
    {
        get => star;
        set => star = value;
    }

    [SerializeField] private int level;  // 1..10
    public int Level
    {
        get => level;
        set => level = value;
    }

    [SerializeField] private int count; // 보유 개수
    public int Count
    {
        get => count;
        set => count = value;
    }

    [SerializeField] private int exp; // 경험치
    public int Exp
    {
        get => exp;
        set => exp = value;
    }

    public string Key => $"{Grade}|{Star}|{Level}";
}
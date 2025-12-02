using System;
using UnityEngine;

//승리 조건
public enum WinType
{
    BankruptcyLastSurvivor,   // 전원 파산
    LandmarkMonopoly,         // 관광지/랜드마크 독점
    LineMonopoly,             // 라인 통일
    TurnLimitScore            // 턴 제한 점수제(옵션)
}

//게임 룰
[Serializable]
public class BulmabulRule
{
    [Header("부루마불 룰(밸런스 기준)")]
    [Tooltip("시작 현금(캐시)")] 
    public long StartCash = 2_000_000; 

    [Tooltip("출발지(START) 통과/도착 시 급여(보너스)")]
    [Header("출발지(START) 통과/도착 시 급여(보너스)")]
    public long SalaryOnStart = 200_000; 

    [Tooltip("경제 스케일 기준값(예: 2000만). 승리조건이 아니라 밸런싱/가격 스케일 기준")]
    [Header("경제 스케일 기준값(예: 2000만). 승리조건이 아니라 밸런싱/가격 스케일 기준")]
    public long EconomyScale = 20_000_000;

    [Header("승리 조건")]
    [Tooltip("파산 승리 사용 여부")]
    public bool EnableBankruptcyWin = true;

    [Tooltip("랜드마크(관광지) 독점 승리 사용 여부")]
    public bool EnableLandmarkMonopolyWin = true;

    [Tooltip("라인(색/구역) 통일 승리 사용 여부")]
    public bool EnableLineMonopolyWin = true;

    [Tooltip("턴 제한 점수제 사용 여부(키오스크용 옵션)")]
    public bool EnableTurnLimitWin = false;

    [Tooltip("턴 제한(EnableTurnLimitWin=true 일 때만 의미)")]
    public int TurnLimit = 30;

    [Tooltip("라인 통일 승리: 몇 개 라인을 완성해야 승리인가(게임 룰 선택)")]
    public int RequiredCompletedLinesToWin = 1;
}

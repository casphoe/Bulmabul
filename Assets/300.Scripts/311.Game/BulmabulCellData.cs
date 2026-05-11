using System;
using UnityEngine;

/// <summary>
/// 부루마불 보드 칸 1개의 데이터.
/// 
/// 땅마다 구매 가격, 건물 가격, 통행료가 다를 수 있으므로
/// 모든 가격 정보를 칸 데이터에 넣는다.
/// 
/// 주의:
/// - 호텔은 최종 랜드마크 역할을 한다.
/// - isLandmark는 "건물 건설 불가인 관광지/특수 지역" 의미로 사용한다.
/// </summary>
[Serializable]
public class BulmabulCellData
{
    [Header("기본 정보")]
    public string cellName;
    public BulmabulCellType cellType;
    public Transform point;

    [Header("땅 기본 가격")]
    public int buyCost;
    public int tollCost;

    [Header("건물 건설 비용")]
    public int smallHouseBuildCost;
    public int houseBuildCost;
    public int bigHouseBuildCost;
    public int hotelBuildCost;

    [Header("건물 추가 통행료")]
    public int smallHouseToll;
    public int houseToll;
    public int bigHouseToll;
    public int hotelToll;

    [Header("세금 / 보너스")]
    public int taxCost;
    [Header("보너스 랜덤 지급")]
    [Tooltip("보너스 칸 최소 지급액")]
    public int bonusMinAmount = 10000;

    [Tooltip("보너스 칸 최대 지급액")]
    public int bonusMaxAmount = 50000;

    [Header("여행 칸")]
    public int travelCost = 100000;

    [Header("독점 / 특수 지역")]
    public int lineId = -1;

    [Tooltip("관광지/특수 지역 여부. true면 작은집/집/큰집/호텔 건설 불가")]
    public bool isLandmark;
}
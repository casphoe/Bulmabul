using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 부루마불 보드 데이터 관리.
/// 
/// 역할:
/// - 칸 데이터 제공
/// - 특정 칸의 위치 반환
/// - 자동 생성된 칸 데이터 등록
/// 
/// 중요:
/// - cells 배열 순서가 실제 이동 순서다.
/// - cells[index].point가 실제 말 이동 위치다.
/// </summary>
public class BulmabulBoard : MonoBehaviour
{
    [Header("보드 칸 데이터 목록")]
    [SerializeField] private BulmabulCellData[] cells;

    public int CellCount => cells != null ? cells.Length : 0;

    /// <summary>
    /// 특정 칸의 게임 데이터 반환.
    /// BulmabulGameState의 구매/건설/세금/보너스 처리에서 사용한다.
    /// </summary>
    public BulmabulCellData GetCell(int index)
    {
        if (cells == null || cells.Length == 0)
            return null;

        index = ClampCellIndex(index);

        if (index < 0 || index >= cells.Length)
            return null;

        return cells[index];
    }

    /// <summary>
    /// 특정 칸의 Transform 반환.
    /// </summary>
    public Transform GetCellTransform(int index)
    {
        BulmabulCellData cell = GetCell(index);

        if (cell == null)
            return null;

        return cell.point;
    }

    /// <summary>
    /// 특정 칸의 실제 월드 위치를 가져온다.
    /// </summary>
    public Vector3 GetCellPosition(int index)
    {
        Transform point = GetCellTransform(index);

        if (point == null)
            return Vector3.zero;

        return point.position;
    }

    public bool IsValidCellIndex(int index)
    {
        return cells != null && index >= 0 && index < cells.Length;
    }

    /// <summary>
    /// 보드 칸 인덱스를 안전하게 보정한다.
    /// </summary>
    public int ClampCellIndex(int index)
    {
        if (CellCount <= 0)
            return 0;

        return Mathf.Clamp(index, 0, CellCount - 1);
    }

    /// <summary>
    /// 자동 생성된 칸 데이터를 Board에 등록한다.
    /// </summary>
    public void SetCellsFromGeneratedMap(IReadOnlyList<BulmabulCellData> generatedCells)
    {
        if (generatedCells == null)
        {
            cells = new BulmabulCellData[0];
            return;
        }

        cells = new BulmabulCellData[generatedCells.Count];

        for (int i = 0; i < generatedCells.Count; i++)
            cells[i] = generatedCells[i];

        Debug.Log($"[BulmabulBoard] Cells registered. Count = {cells.Length}");
    }

    /// <summary>
    /// 모든 셀의 LabelRoot / 이름 / 가격 텍스트 위치를
    /// 보드 중앙 기준으로 다시 배치한다.
    /// </summary>
    public void RefreshAllCellLabelLayouts()
    {
        Vector3 boardCenter = GetBoardCenterPosition();

        for (int i = 0; i < CellCount; i++)
        {
            Transform cell = GetCellTransform(i);

            if (cell == null)
                continue;

            BulmabulCellLabelLayout layout =
                cell.GetComponentInChildren<BulmabulCellLabelLayout>(true);

            if (layout == null)
                continue;

            layout.RefreshLayout(boardCenter);
        }
    }

    /// <summary>
    /// 전체 Cell 위치를 기준으로 보드 중앙 위치를 계산한다.
    /// </summary>
    public Vector3 GetBoardCenterPosition()
    {
        bool initialized = false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);

        for (int i = 0; i < CellCount; i++)
        {
            Transform cell = GetCellTransform(i);

            if (cell == null)
                continue;

            if (!initialized)
            {
                bounds = new Bounds(cell.position, Vector3.zero);
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(cell.position);
            }
        }

        return initialized ? bounds.center : transform.position;
    }
}
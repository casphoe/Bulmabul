using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 주사위 화면 연출 담당.
/// 네트워크 상태는 관리하지 않는다.
/// </summary>
public class BulmabulDiceVisual : MonoBehaviour
{
    [Header("Dice Text")]
    [SerializeField] private TMP_Text txtDiceLeft;
    [SerializeField] private TMP_Text txtDiceRight;
    [SerializeField] private TMP_Text txtDiceResult;

    [Header("Animation")]
    [SerializeField] private float diceRollAnimSeconds = 0.8f;

    public float AnimSeconds => diceRollAnimSeconds;

    public void Play(int left, int right)
    {
        StopAllCoroutines();
        StartCoroutine(CoPlay(left, right));
    }

    private IEnumerator CoPlay(int left, int right)
    {
        float t = 0f;

        while (t < diceRollAnimSeconds)
        {
            t += Time.deltaTime;

            if (txtDiceLeft != null)
                txtDiceLeft.text = Random.Range(1, 7).ToString();

            if (txtDiceRight != null)
                txtDiceRight.text = Random.Range(1, 7).ToString();

            yield return null;
        }

        if (txtDiceLeft != null)
            txtDiceLeft.text = left.ToString();

        if (txtDiceRight != null)
            txtDiceRight.text = right.ToString();

        if (txtDiceResult != null)
        {
            int sum = left + right;
            bool isDouble = left == right;

            txtDiceResult.text = isDouble
                ? $"주사위 {left} + {right} = {sum} / 더블!"
                : $"주사위 {left} + {right} = {sum}";
        }
    }
}
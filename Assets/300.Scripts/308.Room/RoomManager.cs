using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] TMP_InputField inputRoomTitle;

    [SerializeField] Button btnReady;
    [SerializeField] Button btnGameStart;

    [SerializeField] Button btnLeave;
}

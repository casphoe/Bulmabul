using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FriendRequestToastUI : MonoBehaviour
{
    public GameObject root;
    public TextMeshProUGUI txtNick;

    public Button btnAccept;
    public Button btnDecline;

 
    public void Hide()
    {
        if (root) root.SetActive(false);
    }

    public void Show(string nick, Texture photo, Action onAccept, Action onDecline)
    {   
        if (root) root.SetActive(true);

        var lang = (LaguageManager.Instance != null)
            ? LaguageManager.Instance.currentLang
            : Lauaguage.Kor;

        if (txtNick) txtNick.text = (lang == Lauaguage.Kor) ? $"{nick} 님이 친구 요청을 보냈습니다." : $"{nick} You sent me a friend request.";

        if (btnAccept)
        {
            btnAccept.onClick.RemoveAllListeners();
            btnAccept.interactable = (onAccept != null);
            btnAccept.onClick.AddListener(() =>
            {
                try { onAccept?.Invoke(); }
                catch (Exception e) { Debug.LogWarning($"[FriendToast] Accept error: {e}"); }
            });
        }

        if (btnDecline)
        {
            btnDecline.onClick.RemoveAllListeners();
            btnDecline.interactable = (onDecline != null);
            btnDecline.onClick.AddListener(() =>
            {
                try { onDecline?.Invoke(); }
                catch (Exception e) { Debug.LogWarning($"[FriendToast] Decline error: {e}"); }
            });
        } 
    }
}

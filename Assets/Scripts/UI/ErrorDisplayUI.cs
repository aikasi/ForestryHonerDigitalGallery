using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 에러 발생 시 터치 모니터(Display 0)에 에러 메시지를 표시하는 UI 컴포넌트
/// MediaCacheManager(-800)의 Awake에서 Show()가 호출될 수 있으므로 먼저 초기화되어야 함
/// </summary>
[DefaultExecutionOrder(-850)]
public class ErrorDisplayUI : MonoBehaviour
{
    [Tooltip("에러 메시지를 표시할 UI 패널 (검정 반투명 배경 등)")]
    [SerializeField] private GameObject errorPanel;

    [Tooltip("실제 에러 내용이 출력될 Text")]
    [SerializeField] private TextMeshProUGUI errorText;

    [Tooltip("화면 전체를 덮는 투명/반투명 버튼 (클릭 시 닫힘 처리용)")]
    [SerializeField] private Button backgroundDismissButton;

    public System.Action OnDismissed;

    private void Awake()
    {
        if (errorPanel != null) errorPanel.SetActive(false);
        if (backgroundDismissButton != null)
        {
            backgroundDismissButton.onClick.AddListener(Dismiss);
        }
    }

    private void OnDestroy()
    {
        // UI 버튼 이벤트 리스너 해제로 메모리 누수 방지
        if (backgroundDismissButton != null)
        {
            backgroundDismissButton.onClick.RemoveListener(Dismiss);
        }
    }

    /// <summary>
    /// 지정된 메시지로 에러 UI를 화면에 표시합니다.
    /// </summary>
    public void Show(string message)
    {
        if (errorText != null) errorText.text = message;
        if (errorPanel != null) errorPanel.SetActive(true);
    }

    /// <summary>
    /// 화면을 클릭하여 에러 UI를 닫을 때 호출됩니다.
    /// </summary>
    private void Dismiss()
    {
        if (errorPanel != null) errorPanel.SetActive(false);
        OnDismissed?.Invoke();
    }
}

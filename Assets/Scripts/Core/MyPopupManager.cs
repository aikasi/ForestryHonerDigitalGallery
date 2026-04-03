using UnityEngine;

// 새 프로젝트에서 팝업을 띄울 때 사용할 매니저 스크립트 예시
public class MyPopupManager : MonoBehaviour
{
    public CanvasGroup popupCanvasGroup; // 팝업으로 쓸 CanvasGroup 연결
    public PopupTransition transition;   // 방금 붙인 리소스 연결

    // 팝업 열기 
    public async void OpenPopup()
    {
        Debug.Log("1. OpenPopup 버튼 눌림! 시작됨!");
        // 팝업이 튀어오르는 애니메이션이 끝날 때까지 대기
        await transition.PlayEnterAsync(popupCanvasGroup);
        Debug.Log("팝업 열기 애니메이션 완료!");
    }

    // 팝업 닫기
    public async void ClosePopup()
    {
        await transition.PlayExitAsync(popupCanvasGroup);
        Debug.Log("팝업 닫기 애니메이션 완료!");
    }
}

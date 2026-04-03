using System.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

// 크로스페이드 + 튀어오르는 스케일(Scale) 방식의 팝업 애니메이션
public class PopupTransition : MonoBehaviour, ITransitionEffect
{
    [Tooltip("전환 속도 (초 단위)")]
    [SerializeField] private float transitionSpeed = 0.5f;

    public async Task PlayEnterAsync(CanvasGroup target)
    {
        if (target == null) return;

        RectTransform rectTransform = target.GetComponent<RectTransform>();

        // 이전 애니메이션 강제 종료 (스케일 꼬임 버그 방지)
        target.DOKill();
        if (rectTransform != null) rectTransform.DOKill();

        // 초기 상태: 투명도 0, 스케일 0
        target.alpha = 0f;
        if (rectTransform != null) rectTransform.localScale = Vector3.zero;

        target.gameObject.SetActive(true);

        // 동시에 페이드인 + 스케일업 실행
        Sequence sequence = DOTween.Sequence();
        sequence.Join(target.DOFade(1f, transitionSpeed).SetEase(Ease.OutCubic));

        if (rectTransform != null)
        {
            sequence.Join(rectTransform.DOScale(Vector3.one, transitionSpeed)
                .SetEase(Ease.OutBack) // OutBack: 팝업이 약간 통통 튕기며 나타나게 하는 효과
            );
        }

        sequence.SetUpdate(true); // 게임 일시정지 상태(Time.timeScale=0)에서도 동작하도록 설정

        // 비동기 꼬임 방지를 위해 DOTween 네이티브 대기(Awaiter) 사용
        await sequence.AsyncWaitForCompletion();
    }

    public async Task PlayExitAsync(CanvasGroup target)
    {
        if (target == null) return;

        RectTransform rectTransform = target.GetComponent<RectTransform>();

        // 이전 애니메이션 강제 종료 (스케일 꼬임 버그 방지)
        target.DOKill();
        if (rectTransform != null) rectTransform.DOKill();

        // 동시에 페이드아웃 + 스케일다운 실행
        Sequence sequence = DOTween.Sequence();
        sequence.Join(target.DOFade(0f, transitionSpeed).SetEase(Ease.InCubic));

        if (rectTransform != null)
        {
            sequence.Join(rectTransform.DOScale(Vector3.zero, transitionSpeed)
                .SetEase(Ease.InBack) // InBack: 팝업이 뒤로 살짝 밀렸다가 작아지는 효과
            );
        }

        sequence.SetUpdate(true);

        // 비동기 꼬임 방지를 위해 DOTween 네이티브 대기(Awaiter) 사용
        await sequence.AsyncWaitForCompletion();

        // 퇴장 완료 시 비활성화
        target.gameObject.SetActive(false);

        // 다음 번 등장을 위해 미리 스케일 원복
        if (rectTransform != null) rectTransform.localScale = Vector3.one;
    }
}

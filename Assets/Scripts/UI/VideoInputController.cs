using UnityEngine;

/// <summary>
/// 터치 UI(Display 0)의 버튼 입력을 처리하고 Transition 잠금을 관리하는 컨트롤러
/// </summary>
public class VideoInputController : MonoBehaviour
{
    [SerializeField] private PlaybackManager playbackManager;
    [SerializeField] private Logger logger;

    private void Start()
    {
        if (logger == null) logger = FindAnyObjectByType<Logger>();
        if (playbackManager == null) playbackManager = FindAnyObjectByType<PlaybackManager>();

        // 전환 완료 콜백 구독 (전환 성공/실패 시 로그 기록)
        if (playbackManager != null)
            playbackManager.OnTransitionComplete += OnTransitionComplete;
    }

    private void OnDestroy()
    {
        // 콜백 해제 (메모리 누수 방지)
        if (playbackManager != null)
            playbackManager.OnTransitionComplete -= OnTransitionComplete;
    }

    /// <summary>
    /// 전환 완료 콜백 (성공/실패 구분)
    /// </summary>
    private void OnTransitionComplete(bool success)
    {
        if (!success)
        {
            if (logger != null) logger.Enqueue("[VideoInputController] 영상 전환 실패 또는 취소됨");
        }
    }

    /// <summary>
    /// Unity UI Button의 OnClick 이벤트에서 호출할 메서드
    /// </summary>
    /// <param name="index">재생할 영상의 인덱스 (1~8)</param>
    public void OnVideoButtonClicked(int index)
    {
        if (playbackManager == null) return;

        if (playbackManager.IsTransitioning)
        {
            if (logger != null) logger.Enqueue($"[VideoInputController] 무시됨: 현재 비디오 전환 중 (요청 Index: {index})");
            return;
        }

        playbackManager.TransitionTo(index);
    }
}


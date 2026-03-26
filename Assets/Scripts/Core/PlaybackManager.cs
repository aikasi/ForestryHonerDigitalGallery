using DG.Tweening;
using RenderHeads.Media.AVProVideo;
using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 듀얼 버퍼 구조로 비디오 크로스페이드 재생을 담당하는 매니저 클래스 (Display 1 담당)
/// </summary>
public class PlaybackManager : MonoBehaviour
{
    [Header("AVPro Media Players")]
    [SerializeField] private MediaPlayer bufferA;
    [SerializeField] private MediaPlayer bufferB;

    [Header("UI Canvas Groups")]
    [SerializeField] private CanvasGroup canvasA;
    [SerializeField] private CanvasGroup canvasB;
    [SerializeField] private UnityEngine.UI.Graphic displayUguiA; // AVPro DisplayUGUI는 Graphic을 상속함
    [SerializeField] private UnityEngine.UI.Graphic displayUguiB;

    [Header("Dependencies")]
    [SerializeField] private MediaCacheManager cacheManager;
    [SerializeField] private Logger logger;
    [SerializeField] private ErrorDisplayUI errorUI;

    // 현재/대기 버퍼 포인터
    private MediaPlayer activeBuffer;
    private MediaPlayer standbyBuffer;
    private CanvasGroup activeCanvas;
    private CanvasGroup standbyCanvas;

    public bool IsTransitioning { get; private set; }
    public MediaPlayer ActiveMediaPlayer => activeBuffer;
    public Action<bool> OnTransitionComplete;

    public int CurrentPlayingIndex { get; private set; } = -1;
    public Action<bool, int> OnPlaybackStateChanged;

    private int idleVideoIndex;
    private float crossfadeDuration;
    private float loadTimeout;
    private bool showErrorOnLoadFail;

    private bool IsIdleVideo(int index) => index == idleVideoIndex;

    private Coroutine timeoutCoroutine;

    // 현재/가장 최근에 로드 요청된 영상 인덱스 (무한 루프 방지용)
    private int currentlyLoadingIndex = -1;

    // 음소거 관련
    private bool videoMute;
    // 화면 맞춤 관련
    private ScaleMode videoScaleMode = ScaleMode.StretchToFill;

    private void Start()
    {
        if (cacheManager == null) cacheManager = FindAnyObjectByType<MediaCacheManager>();
        if (logger == null) logger = FindAnyObjectByType<Logger>();
        if (errorUI == null) errorUI = FindAnyObjectByType<ErrorDisplayUI>();

        LoadSettings();
        InitializeBuffers();
    }

    private void OnDestroy()
    {
        // AVPro 이벤트 리스너 해제 (메모리 누수 방지)
        if (bufferA != null) bufferA.Events.RemoveListener(OnVideoEvent);
        if (bufferB != null) bufferB.Events.RemoveListener(OnVideoEvent);

        // 진행 중인 DOTween 애니메이션 정리
        if (activeCanvas != null) activeCanvas.DOKill();
        if (standbyCanvas != null) standbyCanvas.DOKill();

        // 타임아웃 코루틴 정리
        if (timeoutCoroutine != null) StopCoroutine(timeoutCoroutine);

        // ErrorDisplayUI 콜백 해제
        if (errorUI != null) errorUI.OnDismissed -= OnErrorUIDismissed;
    }

    private void LoadSettings()
    {
        idleVideoIndex = CSVReader.GetIntValue("IdleVideoIndex", 0);
        crossfadeDuration = CSVReader.GetFloatValue("CrossfadeDuration", 0.5f);
        loadTimeout = CSVReader.GetFloatValue("LoadTimeout", 10f);

        string showErrorStr = CSVReader.GetStringValue("ShowErrorOnLoadFail", "false");
        showErrorOnLoadFail = bool.TryParse(showErrorStr, out bool parsed) && parsed;

        // 음소거 설정
        string muteStr = CSVReader.GetStringValue("VideoMute", "false");
        videoMute = bool.TryParse(muteStr, out bool muteParsed) && muteParsed;

        // 화면 맞춤 설정 (StretchToFill / ScaleToFit / ScaleToFill)
        string scaleModeStr = CSVReader.GetStringValue("VideoScaleMode", "StretchToFill");
        if (Enum.TryParse(scaleModeStr, out ScaleMode parsedMode))
        {
            videoScaleMode = parsedMode;
        }
    }

    private void InitializeBuffers()
    {
        // 초기 상태: A가 Active, B가 Standby
        activeBuffer = bufferA;
        standbyBuffer = bufferB;
        activeCanvas = canvasA;
        standbyCanvas = canvasB;

        // 트랜지션 강제 취소 대비 명시적 초기화
        activeCanvas.DOKill();
        standbyCanvas.DOKill();
        activeCanvas.alpha = 1f;
        standbyCanvas.alpha = 0f;

        // 이벤트 리스너 등록
        bufferA.Events.AddListener(OnVideoEvent);
        bufferB.Events.AddListener(OnVideoEvent);

        // 화면 맞춤, 음소거 적용
        ApplyMute();
        ApplyScaleMode();

        // 00번 (대기 영상) 루프 재생으로 시작
        PlayIdleVideoOnActiveBuffer();
    }

    private void PlayIdleVideoOnActiveBuffer()
    {
        var mediaData = cacheManager.GetMediaData(idleVideoIndex);
        if (mediaData.HasValue && mediaData.Value.IsValid)
        {
            currentlyLoadingIndex = idleVideoIndex;
            activeBuffer.OpenMedia(new MediaPath(mediaData.Value.AbsolutePath, MediaPathType.AbsolutePathOrURL), autoPlay: true);

            // OpenMedia 호출 직후 Control이 null일 가능성을 대비해 루프 설정은 FirstFrameReady 이벤트에서도 보장해야 함
            if (activeBuffer.Control != null)
                activeBuffer.Control.SetLooping(true);
        }
        else
        {
            if (logger != null) logger.Enqueue("[PlaybackManager] 오류: 00번(대기 영상)을 찾을 수 없거나 유효하지 않습니다.");
        }
    }

    /// <summary>
    /// 지정된 인덱스의 영상으로 크로스페이드 전환을 시도합니다.
    /// </summary>
    public void TransitionTo(int index)
    {
        if (IsTransitioning) return;

        if (CurrentPlayingIndex == index && !IsIdleVideo(index))
        {
            ReturnToIdle();
            return;
        }

        IsTransitioning = true;
        currentlyLoadingIndex = index;

        var mediaData = cacheManager.GetMediaData(index);

        // 캐시 데이터가 없거나, 검증에 실패한 파일인 경우
        if (!mediaData.HasValue || !mediaData.Value.IsValid)
        {
            HandleLoadFailure($"영상 {index:D2}번의 캐시 정보가 없거나 파일이 손상/누락되었습니다.");
            return;
        }

        // 대기 버퍼 설정 (루프 설정은 Control 널 가능성 때문에 안전하게 FirstFrameReady 등에서도 재확인)
        if (standbyBuffer.Control != null)
            standbyBuffer.Control.SetLooping(false); // 일반 영상은 루프 안함

        // 비동기 로드 시작
        standbyBuffer.OpenMedia(new MediaPath(mediaData.Value.AbsolutePath, MediaPathType.AbsolutePathOrURL), autoPlay: false);

        // 타임아웃 감시 시작
        if (timeoutCoroutine != null) StopCoroutine(timeoutCoroutine);
        timeoutCoroutine = StartCoroutine(LoadTimeoutRoutine(index));
    }

    private IEnumerator LoadTimeoutRoutine(int index)
    {
        yield return new WaitForSeconds(loadTimeout);

        // 타임아웃 발생 시
        HandleLoadFailure($"영상 {index:D2}번 로드 타임아웃 ({loadTimeout}초)");
    }

    private void OnVideoEvent(MediaPlayer mp, MediaPlayerEvent.EventType eventType, ErrorCode errorCode)
    {
        // 1. 대기 버퍼 영상 준비 완료 -> 크로스페이드 시작
        if (mp == standbyBuffer && eventType == MediaPlayerEvent.EventType.FirstFrameReady)
        {
            // Control 늦은 초기화 대응하여 루프 설정 재확인 적용
            if (mp.Control != null)
            {
                mp.Control.SetLooping(currentlyLoadingIndex == idleVideoIndex);
                mp.Control.MuteAudio(videoMute); // 음소거 재적용
            }

            if (IsTransitioning)
            {
                if (timeoutCoroutine != null)
                {
                    StopCoroutine(timeoutCoroutine);
                    timeoutCoroutine = null;
                }

                ExecuteCrossfade();
            }
        }
        // 활성 버퍼 영상 준비 완료 (초기화 시 대기 영상용 대응)
        else if (mp == activeBuffer && eventType == MediaPlayerEvent.EventType.FirstFrameReady)
        {
            if (mp.Control != null)
            {
                mp.Control.SetLooping(currentlyLoadingIndex == idleVideoIndex);
                mp.Control.MuteAudio(videoMute); // 음소거 재적용
            }
        }
        // 2. 비동기 로드 에러
        else if (mp == standbyBuffer && eventType == MediaPlayerEvent.EventType.Error)
        {
            if (IsTransitioning)
            {
                if (timeoutCoroutine != null)
                {
                    StopCoroutine(timeoutCoroutine);
                    timeoutCoroutine = null;
                }
                HandleLoadFailure($"AVPro 로드 에러 (ErrorCode: {errorCode})");
            }
        }
        // 3. 영상 재생 완료 (EOF)
        else if (mp == activeBuffer && eventType == MediaPlayerEvent.EventType.FinishedPlaying)
        {
            // 루프 중이지 않은 영상(01~08)이 끝난 경우 대기로 복귀
            if (activeBuffer.Control != null && !activeBuffer.Control.IsLooping())
            {
                ReturnToIdle();
            }
        }
        // 4. 대기 복귀 완료 (Idle 상태로 돌아옴)
        else if (mp == activeBuffer && eventType == MediaPlayerEvent.EventType.FirstFrameReady && IsIdleVideo(currentlyLoadingIndex))
        {
            // 이미 대기 상태로 복귀된 경우에만 이벤트 발생
            if (CurrentPlayingIndex != -1)
            {
                CurrentPlayingIndex = -1;
                OnPlaybackStateChanged?.Invoke(false, -1);
            }
        }
    }

    private void ExecuteCrossfade()
    {
        standbyBuffer.Play();

        // 중복 트랜지션 강제 취소 대비
        activeCanvas.DOKill();
        standbyCanvas.DOKill();

        // DOTween 크로스페이드 (active: 1->0, standby: 0->1)
        activeCanvas.DOFade(0f, crossfadeDuration);
        standbyCanvas.DOFade(1f, crossfadeDuration).OnComplete(() =>
        {
            // 전환 완료 시점
            activeBuffer.CloseMedia(); // 이전 메모리 해제

            // 포인터 스왑
            var tempBuffer = activeBuffer;
            activeBuffer = standbyBuffer;
            standbyBuffer = tempBuffer;

            var tempCanvas = activeCanvas;
            activeCanvas = standbyCanvas;
            standbyCanvas = tempCanvas;

            // 현재 재생 인덱스 업데이트 및 이벤트 발생
            bool isIdle = IsIdleVideo(currentlyLoadingIndex);
            CurrentPlayingIndex = isIdle ? -1 : currentlyLoadingIndex;
            OnPlaybackStateChanged?.Invoke(!isIdle, CurrentPlayingIndex);

            IsTransitioning = false;
            OnTransitionComplete?.Invoke(true);
        });
    }

    /// <summary>
    /// 로드 실패 시 에러 처리 및 상태 초기화
    /// </summary>
    private void HandleLoadFailure(string msg)
    {
        if (logger != null) logger.Enqueue($"[PlaybackManager] 로드 실패: {msg}");

        standbyBuffer.CloseMedia();

        IsTransitioning = false;
        OnTransitionComplete?.Invoke(false);

        if (showErrorOnLoadFail && errorUI != null)
        {
            // 00번 로드 자체가 실패했다면 다시 복귀 요청 시도하는 무한 루프 차단
            bool isIdleVideoFail = (currentlyLoadingIndex == idleVideoIndex);

            errorUI.OnDismissed -= OnErrorUIDismissed;
            errorUI.OnDismissed += OnErrorUIDismissed;
            errorUI.Show($"로드 실패:\n{msg}" + (isIdleVideoFail ? "\n\n(시스템 복구 불가 상태. 관리자 문의 요망)" : ""));
        }
        else
        {
            // 00번 실패인 경우 ReturnToIdle 재호출로 인한 무한루프 방지
            if (currentlyLoadingIndex == idleVideoIndex)
            {
                if (logger != null) logger.Enqueue("[PlaybackManager] 심각한 오류: 대기 영상(00번) 로드 실패. 앱이 정상 작동하지 않을 수 있습니다.");
                return;
            }
            ReturnToIdle();
        }
    }

    private void OnErrorUIDismissed()
    {
        if (errorUI != null) errorUI.OnDismissed -= OnErrorUIDismissed;

        if (currentlyLoadingIndex == idleVideoIndex)
        {
            if (logger != null) logger.Enqueue("[PlaybackManager] 대기 영상 복구 실패 상태이므로 복귀를 포기합니다.");
            return;
        }

        ReturnToIdle();
    }

    /// <summary>
    /// 대기 영상(00)으로 크로스페이드 복귀
    /// </summary>
    public void ReturnToIdle(bool force = false)
    {
        if (IsTransitioning) return;

        // 이미 00번 재생 중이고 루프 상태면 무시 (단, 강제 리셋 요청 시 방어 로직 무시)
        if (!force && activeBuffer.Control != null && activeBuffer.Control.IsLooping()) return;

        IsTransitioning = true;
        currentlyLoadingIndex = idleVideoIndex;

        var mediaData = cacheManager.GetMediaData(idleVideoIndex);
        if (mediaData.HasValue && mediaData.Value.IsValid)
        {
            if (standbyBuffer.Control != null)
                standbyBuffer.Control.SetLooping(true);
            standbyBuffer.OpenMedia(new MediaPath(mediaData.Value.AbsolutePath, MediaPathType.AbsolutePathOrURL), autoPlay: false);

            if (timeoutCoroutine != null) StopCoroutine(timeoutCoroutine);
            timeoutCoroutine = StartCoroutine(LoadTimeoutRoutine(idleVideoIndex));
        }
        else
        {
            HandleLoadFailure("대기 영상(00번)을 찾을 수 없거나 유효하지 않습니다.");
        }
    }

    /// <summary>
    /// Watchdog 등에서 강제로 리셋할 때 사용. 내부적으로 크로스페이드(ReturnToIdle)를 통해 진행
    /// </summary>
    public void ForceResetToIdle()
    {
        if (logger != null) logger.Enqueue("[PlaybackManager] 영상 강제 리셋 (ForceResetToIdle) 요청됨.");

        // 기존 진행 중인 트랜지션 강제 취소
        if (IsTransitioning)
        {
            if (timeoutCoroutine != null) StopCoroutine(timeoutCoroutine);
            activeCanvas.DOKill();
            standbyCanvas.DOKill();

            // 트랜지션 취소 시 알파값 엉킴 방지
            activeCanvas.alpha = 1f;
            standbyCanvas.alpha = 0f;

            standbyBuffer.CloseMedia();
            IsTransitioning = false;
            OnTransitionComplete?.Invoke(false);
        }
        ReturnToIdle(force: true); // 강제로 껐다 켜기 (루프 무시)
    }

    /// <summary>
    /// 두 MediaPlayer에 음소거 설정을 적용합니다.
    /// </summary>
    private void ApplyMute()
    {
        if (bufferA.Control != null) bufferA.Control.MuteAudio(videoMute);
        if (bufferB.Control != null) bufferB.Control.MuteAudio(videoMute);
    }

    /// <summary>
    /// DisplayUGUI 컴포넌트의 ScaleMode를 설정합니다.
    /// </summary>
    private void ApplyScaleMode()
    {
        // DisplayUGUI 캐스팅 후 ScaleMode 프로퍼티 변경
        if (displayUguiA is DisplayUGUI guiA) guiA.ScaleMode = videoScaleMode;
        if (displayUguiB is DisplayUGUI guiB) guiB.ScaleMode = videoScaleMode;
    }
}

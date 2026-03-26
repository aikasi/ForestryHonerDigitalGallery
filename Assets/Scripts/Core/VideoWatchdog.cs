using RenderHeads.Media.AVProVideo;
using UnityEngine;

/// <summary>
/// AVPro MediaPlayer의 프레임 정지(프리징) 현상을 감지하고 자가 복구를 수행하는 Watchdog 클래스
/// </summary>
public class VideoWatchdog : MonoBehaviour
{
    [SerializeField] private PlaybackManager playbackManager;
    [SerializeField] private Logger logger;

    [Header("Debug")]
    [Tooltip("에디터 실행 중 체크하면 강제로 프레임이 멈춘 것처럼 시뮬레이션합니다.")]
    [SerializeField] private bool simulateFreeze = false;

    // WatchdogInterval (기본 1.0초)마다 검사
    private float checkInterval = 1.0f;
    private float stallThreshold = 2.0f;
    private int maxRetryCount = 3;

    private float timer = 0f;
    private float stallTimer = 0f;
    private int lastFrameCount = 0;

    private int currentRetryCount = 0;

    private void Start()
    {
        if (logger == null) logger = FindAnyObjectByType<Logger>();
        if (playbackManager == null) playbackManager = FindAnyObjectByType<PlaybackManager>();

        LoadSettings();
    }

    private void LoadSettings()
    {
        stallThreshold = CSVReader.GetFloatValue("WatchdogStallThreshold", 2.0f);
        maxRetryCount = CSVReader.GetIntValue("WatchdogRetryCount", 3);
    }

    private void Update()
    {
        if (playbackManager == null) return;

        // 전환 중에는 오감지 방지를 위해 감시 일시 중지
        if (playbackManager.IsTransitioning)
        {
            ResetWatchdogState(); // 전환 중이면 상태 초기화
            return;
        }

        MediaPlayer activePlayer = playbackManager.ActiveMediaPlayer;
        if (activePlayer == null || activePlayer.Control == null) return;

        // 영상이 재생 중일 때만 감시
        if (activePlayer.Control.IsPlaying())
        {
            timer += Time.deltaTime;
            if (timer >= checkInterval)
            {
                timer = 0f;
                CheckForStalls(activePlayer);
            }
        }
        else
        {
            ResetWatchdogState();
        }
    }

    private void CheckForStalls(MediaPlayer activePlayer)
    {
        int currentFrameCount = activePlayer.TextureProducer != null ? activePlayer.TextureProducer.GetTextureFrameCount() : 0;

        // [디버그용] 인스펙터에서 체크하면 영상이 멈춘 것처럼 시뮬레이션
        if (simulateFreeze)
        {
            currentFrameCount = lastFrameCount;
        }

        // 프레임이 증가하지 않고 멈춰있음
        if (currentFrameCount == lastFrameCount && currentFrameCount > 0)
        {
            stallTimer += checkInterval;

            if (stallTimer >= stallThreshold)
            {
                HandleStall(activePlayer);
            }
        }
        else // 정상 작동 중
        {
            lastFrameCount = currentFrameCount;
            ResetWatchdogState();
        }
    }

    private void HandleStall(MediaPlayer activePlayer)
    {
        currentRetryCount++;

        if (currentRetryCount <= maxRetryCount)
        {
            string msg = $"[VideoWatchdog] 비디오 정지 감지. 재생 복구 시도 ({currentRetryCount}/{maxRetryCount})";
            if (logger != null) logger.Enqueue(msg);

            // 2단계: Play() 재호출 시도
            if (activePlayer.Control != null)
            {
                activePlayer.Play();
            }
            // 재시도 간격 확보를 위해 stallTimer는 약간만 차감하거나 0으로
            stallTimer = 0f;
        }
        else
        {
            string msg = $"[VideoWatchdog] 복구 실패 (재시도 {maxRetryCount}회 초과). 강제 리셋을 수행합니다.";
            if (logger != null) logger.Enqueue(msg);

            // 3단계: 강제 리셋 (00.mp4 대기로 복귀)
            playbackManager.ForceResetToIdle();
            ResetWatchdogState();
        }
    }

    private void ResetWatchdogState()
    {
        stallTimer = 0f;
        currentRetryCount = 0;

        if (playbackManager != null && playbackManager.ActiveMediaPlayer != null && playbackManager.ActiveMediaPlayer.TextureProducer != null)
        {
            lastFrameCount = playbackManager.ActiveMediaPlayer.TextureProducer.GetTextureFrameCount();
        }
        else
        {
            lastFrameCount = 0;
        }
    }
}
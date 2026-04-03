using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;

/// <summary>
/// 터치 모니터에서 인물 버튼 선택 시 캡션(디테일) 패널을 표시하는 매니저.
/// StreamingAssets 폴더에서 이미지를 비동기로 불러오고, 닫힐 때 메모리를 즉시 해제합니다.
/// 인스펙터의 Is Feature Enabled 체크박스로 기능 전체를 끄고 켤 수 있습니다.
/// </summary>
public class DetailPanelManager : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("캡션(디테일) 이미지를 띄울 전체 패널 오브젝트 (활성화/비활성화 용도)")]
    [SerializeField] private GameObject captionPanel;
    [Tooltip("캡션 이미지를 실제로 표시할 RawImage 컴포넌트")]
    [SerializeField] private RawImage captionRawImage;
    [Tooltip("패널을 닫을 버튼 (할당하면 자동으로 닫기 기능이 연결됩니다)")]
    [SerializeField] private Button closeButton;

    [Header("Settings")]
    [Tooltip("이 캡션 기능의 사용 여부 (체크 해제 시 모든 동작이 무시됩니다)")]
    [SerializeField] private bool isFeatureEnabled = true;

    [Header("Dependencies")]
    [SerializeField] private PlaybackManager playbackManager;
    [SerializeField] private Logger logger;
    [Tooltip("팝업 애니메이션 효과 (옵션) - 할당 시 열고 닫을 때 부드러운 애니메이션 적용")]
    [SerializeField] private PopupTransition popupTransition;

    // 비동기 로딩 코루틴 참조 (광클릭 방어용)
    private Coroutine loadCoroutine;
    // 현재 진행 중인 웹 요청 참조 (도중 취소용)
    private UnityWebRequest currentRequest;
    // 현재 화면에 표시 중인 텍스처 (메모리 해제 대상)
    private Texture2D currentTexture;
    
    // 비동기 닫힘 대기 중 새 로딩이 시작되는 것을 감지하기 위한 토큰
    private int loadToken = 0;

    private void Start()
    {
        // 의존성 자동 탐색
        if (playbackManager == null) playbackManager = FindAnyObjectByType<PlaybackManager>();
        if (logger == null) logger = FindAnyObjectByType<Logger>();
        if (popupTransition == null) popupTransition = GetComponent<PopupTransition>();

        // 시작 시 패널 닫기
        HidePanelImmediate();

        // 영상 재생 상태 변경 이벤트 구독
        if (playbackManager != null)
        {
            playbackManager.OnPlaybackStateChanged += OnPlaybackStateChanged;
        }

        // 닫기 버튼 자동 연결
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnCloseButtonClicked);
        }
    }

    private void OnDestroy()
    {
        // 이벤트 구독 해제 (메모리 누수 방지)
        if (playbackManager != null)
        {
            playbackManager.OnPlaybackStateChanged -= OnPlaybackStateChanged;
        }

        // 닫기 버튼 리스너 해제
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(OnCloseButtonClicked);
        }

        // 잔여 텍스처 메모리 강제 반환
        ReleaseTexture();
    }

    /// <summary>
    /// PlaybackManager에서 영상 재생/정지 이벤트가 발생할 때 호출됩니다.
    /// </summary>
    private void OnPlaybackStateChanged(bool isPlaying, int index)
    {
        // 기능 비활성화 상태면 무시
        if (!isFeatureEnabled) return;

        if (isPlaying && index >= 1 && index <= 8)
        {
            // 영상 재생 시작 → 해당 인덱스의 디테일 이미지 로드
            LoadAndShow(index);
        }
        else
        {
            // 영상 종료 또는 Idle 복귀 → 패널 자동 닫기 및 메모리 해제
            ClosePanel(returnToIdle: false);
        }
    }

    /// <summary>
    /// 지정된 인덱스의 디테일 이미지를 비동기로 로드하여 패널에 표시합니다.
    /// </summary>
    private void LoadAndShow(int index)
    {
        loadToken++; // 새 로딩이 시작됨을 표기 (진행 중인 닫기 애니메이션 무효화)

        // 이전 로딩 작업이 진행 중이면 즉시 취소 (광클릭 방어)
        CancelLoading();

        loadCoroutine = StartCoroutine(LoadImageCoroutine(index));
    }

    /// <summary>
    /// 비동기 이미지 로딩 코루틴.
    /// StreamingAssets/0001_Detail.png 를 탐색하여 로드합니다.
    /// 취소 시 메모리 최적화를 위해 using 대신 명시적 수동 해제를 사용합니다.
    /// </summary>
    private IEnumerator LoadImageCoroutine(int index)
    {
        // 파일명 포맷: 01_Detail, 02_Detail ... 08_Detail (기존 영상 파일명 규칙과 통일)
        string filename = $"{index:D2}_Detail.png";
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, filename);

        currentRequest = UnityWebRequestTexture.GetTexture(path);
        
        yield return currentRequest.SendWebRequest();

        // 도중 CancelLoading() 이 호출되어 currentRequest가 null이 된 경우 중단
        if (currentRequest == null) yield break;

        if (currentRequest.result == UnityWebRequest.Result.Success)
        {
            // 이전 텍스처가 남아있다면 먼저 해제
            ReleaseTexture();

            // 새 텍스처 추출 및 저장
            Texture2D texture = DownloadHandlerTexture.GetContent(currentRequest);
            texture.filterMode = FilterMode.Bilinear;
            currentTexture = texture;

            // UI에 텍스처 할당 및 패널 활성화
            if (captionRawImage != null)
            {
                captionRawImage.texture = currentTexture;
                captionRawImage.color = Color.white;
            }

            // 팝업 애니메이션 처리
            if (popupTransition != null && captionPanel != null)
            {
                CanvasGroup cg = captionPanel.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    _ = popupTransition.PlayEnterAsync(cg);
                }
                else
                {
                    captionPanel.SetActive(true);
                }
            }
            else if (captionPanel != null)
            {
                captionPanel.SetActive(true);
            }

            if (logger != null)
                logger.Enqueue($"[DetailPanelManager] 캡션 패널 열기 완료: {filename}");
        }
        else
        {
            if (logger != null)
                logger.Enqueue($"[DetailPanelManager] 캡션 이미지 로드 실패: {filename}, 오류: {currentRequest.error}");
            // 로드 실패 시 투명 팝업이 유지되지 않도록 방어
            HidePanelImmediate();
        }

        // 정상/실패 종료 시 리소스 해제
        if (currentRequest != null)
        {
            currentRequest.Dispose();
            currentRequest = null;
        }

        loadCoroutine = null;
    }

    /// <summary>
    /// 사용자 직접 터치(ex: 닫기 버튼)에 의해 호출됩니다.
    /// 패널을 닫음과 동시에 영상을 대기 영상(Idle)으로 전환합니다.
    /// </summary>
    public void OnCloseButtonClicked()
    {
        // UI 패널 닫기 모션 처리 및 메인 영상 대기 화면 전환
        ClosePanel(returnToIdle: true);
    }

    /// <summary>
    /// 패널을 닫고 메모리를 해제합니다.
    /// returnToIdle 가 true일 경우, 닫힘과 동시에 메인 시스템을 대기 영상으로 돌려보냅니다.
    /// </summary>
    public async void ClosePanel(bool returnToIdle = false)
    {
        int currentToken = ++loadToken; // 닫기 작업 시작 표식

        CancelLoading();

        // 메인 영상 복귀는 애니메이션과 동시에 즉각 반응하도록 선처리
        if (returnToIdle && playbackManager != null)
        {
            playbackManager.ReturnToIdle();
            if (logger != null) logger.Enqueue("[DetailPanelManager] 캡션 수동 닫힘 - 영상 대기 전환을 요청합니다.");
        }

        // 팝업 퇴장 애니메이션 실행 (할당되어 있고, 패널이 현재 활성 상태인 경우)
        bool hasAnimated = false;
        if (popupTransition != null && captionPanel != null && captionPanel.activeSelf)
        {
            CanvasGroup cg = captionPanel.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                hasAnimated = true;
                await popupTransition.PlayExitAsync(cg);
            }
        }

        // 애니메이션 대기 도중 새 캡션 로드가 시작되었다면 여기서 중단 (닫기 무효화)
        if (loadToken != currentToken) return;

        // 애니메이션이 없었거나 끝난 후 최종 정리
        if (!hasAnimated)
        {
            HidePanelImmediate();
        }
        ReleaseTexture();
    }

    /// <summary>
    /// 진행 중인 비동기 로딩 코루틴과 웹 요청을 명시적으로 취소합니다.
    /// </summary>
    private void CancelLoading()
    {
        if (loadCoroutine != null)
        {
            StopCoroutine(loadCoroutine);
            loadCoroutine = null;
        }

        if (currentRequest != null)
        {
            currentRequest.Abort();
            currentRequest.Dispose();
            currentRequest = null;
        }
    }

    /// <summary>
    /// 패널 UI를 즉시 비활성화합니다.
    /// </summary>
    private void HidePanelImmediate()
    {
        if (captionPanel != null && captionPanel.activeSelf)
        {
            captionPanel.SetActive(false);
        }

        if (captionRawImage != null)
        {
            captionRawImage.texture = null;
        }
    }

    /// <summary>
    /// 현재 보유한 텍스처의 VRAM 메모리를 즉시 해제합니다.
    /// </summary>
    private void ReleaseTexture()
    {
        if (currentTexture != null)
        {
            Destroy(currentTexture);
            currentTexture = null;
        }
    }
}

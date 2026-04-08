using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;

/// <summary>
/// 터치 모니터에서 인물 버튼 선택 시 디테일 팝업을 표시하는 매니저.
/// StreamingAssets 폴더에서 배경, 닫기 버튼, 인물별 이미지 2장을 비동기로 로드합니다.
/// 모든 이미지는 외부(StreamingAssets)에서 동적으로 로드되므로 빌드 없이 교체 가능합니다.
/// </summary>
public class DetailPanelManager : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("디테일 팝업 전체 패널 오브젝트 (활성화/비활성화 용도)")]
    [SerializeField] private GameObject captionPanel;

    [Tooltip("팝업 배경 이미지 (DetailBackground.png 동적 로드 대상)")]
    [SerializeField] private RawImage backgroundImage;

    [Tooltip("인물 소개 텍스트 카드 ({index}_1_detail.png 동적 로드 대상)")]
    [SerializeField] private RawImage detailInfoImage;

    [Tooltip("인물 사진 ({index}_2_detail.png 동적 로드 대상)")]
    [SerializeField] private RawImage detailPhotoImage;

    [Tooltip("닫기(뒤로가기) 버튼 이미지 (detail_back.png 동적 로드 대상)")]
    [SerializeField] private Image closeButtonImage;

    [Tooltip("팝업 텍스트 디자인 파츠 (0_1_main.png 동적 로드 대상)")]
    [SerializeField] private RawImage commonDecoImage1;

    [Tooltip("팝업 타이틀 디자인 파츠 (0_3_main.png 동적 로드 대상)")]
    [SerializeField] private RawImage commonDecoImage3;

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
    private UnityWebRequest currentRequest1;
    private UnityWebRequest currentRequest2;
    // 현재 화면에 표시 중인 텍스처들 (메모리 해제 대상)
    private Texture2D currentInfoTexture;
    private Texture2D currentPhotoTexture;
    // 공통 리소스 텍스처 (앱 종료 시 해제)
    private Texture2D backgroundTexture;
    private Sprite closeButtonSprite;
    private Texture2D deco1Texture; // 0_1_main
    private Texture2D deco3Texture; // 0_3_main

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

        // 공통 리소스(배경, 닫기 버튼) 1회 로드
        StartCoroutine(LoadCommonResources());

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
        // 최우선으로 진행 중인 모든 비동기 다운로드 및 코루틴 강제 취소
        CancelLoading();

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

        // 인물별 텍스처 메모리 강제 반환
        ReleaseDetailTextures();

        // 공통 리소스 메모리 해제
        if (backgroundTexture != null)
        {
            Destroy(backgroundTexture);
            backgroundTexture = null;
        }
        if (deco1Texture != null)
        {
            Destroy(deco1Texture);
            deco1Texture = null;
        }
        if (deco3Texture != null)
        {
            Destroy(deco3Texture);
            deco3Texture = null;
        }
        if (closeButtonSprite != null)
        {
            if (closeButtonSprite.texture != null) Destroy(closeButtonSprite.texture);
            Destroy(closeButtonSprite);
            closeButtonSprite = null;
        }
    }

    /// <summary>
    /// 공통 리소스(배경, 닫기 버튼 이미지)를 앱 시작 시 1회만 로드합니다.
    /// </summary>
    private IEnumerator LoadCommonResources()
    {
        // 배경 이미지 로드
        yield return StartCoroutine(LoadTextureCoroutine("DetailBackground.png", (texture) =>
        {
            if (texture == null) return;
            backgroundTexture = texture; // UI 연결 여부와 무관하게 반드시 추적 (누수 방지)
            if (backgroundImage != null)
            {
                backgroundImage.texture = texture;
                backgroundImage.color = Color.white;
                if (logger != null) logger.Enqueue("[DetailPanelManager] 공통 배경 이미지 로드 완료");
            }
            else
            {
                if (logger != null) logger.Enqueue("[DetailPanelManager] 경고: backgroundImage가 미할당 상태입니다. 텍스처는 추적 중.");
            }
        }));

        // 닫기 버튼 이미지 로드
        yield return StartCoroutine(LoadTextureCoroutine("detail_back.png", (texture) =>
        {
            if (texture == null) return;
            // Sprite.Create는 원본 텍스처를 참조하므로, Sprite를 통해 텍스처의 수명도 관리됨
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f
            );
            sprite.name = "detail_back";
            closeButtonSprite = sprite; // UI 연결 여부와 무관하게 반드시 추적 (누수 방지)
            if (closeButtonImage != null)
            {
                closeButtonImage.sprite = sprite;
                if (logger != null) logger.Enqueue("[DetailPanelManager] 닫기 버튼 이미지 로드 완료");
            }
            else
            {
                if (logger != null) logger.Enqueue("[DetailPanelManager] 경고: closeButtonImage가 미할당 상태입니다. 스프라이트는 추적 중.");
            }
        }));

        // 팝업 장식용 (0_1_main) 로드
        yield return StartCoroutine(LoadTextureCoroutine("0_1_main.png", (texture) =>
        {
            if (texture == null) return;
            deco1Texture = texture; // 반드시 추적
            if (commonDecoImage1 != null)
            {
                commonDecoImage1.texture = texture;
                commonDecoImage1.color = Color.white;
            }
        }));

        // 팝업 장식용 (0_3_main) 로드
        yield return StartCoroutine(LoadTextureCoroutine("0_3_main.png", (texture) =>
        {
            if (texture == null) return;
            deco3Texture = texture; // 반드시 추적
            if (commonDecoImage3 != null)
            {
                commonDecoImage3.texture = texture;
                commonDecoImage3.color = Color.white;
            }
        }));
    }

    /// <summary>
    /// 범용 텍스처 로딩 코루틴. 파일명을 받아 StreamingAssets에서 로드 후 콜백으로 전달합니다.
    /// </summary>
    private IEnumerator LoadTextureCoroutine(string filename, System.Action<Texture2D> onComplete)
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, filename);

        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(path))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                texture.filterMode = FilterMode.Bilinear;
                onComplete?.Invoke(texture);
            }
            else
            {
                if (logger != null) logger.Enqueue($"[DetailPanelManager] 이미지 로드 실패: {filename}, 오류: {request.error}");
                onComplete?.Invoke(null);
            }
        }
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
    /// 지정된 인덱스의 디테일 이미지 2장을 비동기로 로드하여 패널에 표시합니다.
    /// </summary>
    private void LoadAndShow(int index)
    {
        loadToken++; // 새 로딩이 시작됨을 표기 (진행 중인 닫기 애니메이션 무효화)

        // 이전 로딩 작업이 진행 중이면 즉시 취소 (광클릭 방어)
        CancelLoading();

        loadCoroutine = StartCoroutine(LoadDetailImagesCoroutine(index));
    }

    /// <summary>
    /// 인물별 디테일 이미지 2장을 동시에 로드하는 코루틴.
    /// {index}_1_detail.png (소개 카드) + {index}_2_detail.png (인물 사진)
    /// </summary>
    private IEnumerator LoadDetailImagesCoroutine(int index)
    {
        string filename1 = $"{index}_1_detail.png";
        string filename2 = $"{index}_2_detail.png";
        string path1 = System.IO.Path.Combine(Application.streamingAssetsPath, filename1);
        string path2 = System.IO.Path.Combine(Application.streamingAssetsPath, filename2);

        // 두 요청을 동시에 시작
        currentRequest1 = UnityWebRequestTexture.GetTexture(path1);
        currentRequest2 = UnityWebRequestTexture.GetTexture(path2);

        var op1 = currentRequest1.SendWebRequest();
        var op2 = currentRequest2.SendWebRequest();

        // 두 요청 모두 완료될 때까지 대기
        while (!op1.isDone || !op2.isDone)
        {
            yield return null;
        }

        // 도중 CancelLoading()이 호출되어 요청이 null이 된 경우 중단
        if (currentRequest1 == null || currentRequest2 == null) yield break;

        bool success1 = currentRequest1.result == UnityWebRequest.Result.Success;
        bool success2 = currentRequest2.result == UnityWebRequest.Result.Success;

        if (success1 && success2)
        {
            // 이전 텍스처가 남아있다면 먼저 해제
            ReleaseDetailTextures();

            // 새 텍스처 추출 및 저장
            Texture2D infoTex = DownloadHandlerTexture.GetContent(currentRequest1);
            infoTex.filterMode = FilterMode.Bilinear;
            currentInfoTexture = infoTex;

            Texture2D photoTex = DownloadHandlerTexture.GetContent(currentRequest2);
            photoTex.filterMode = FilterMode.Bilinear;
            currentPhotoTexture = photoTex;

            // UI에 텍스처 할당
            if (detailInfoImage != null)
            {
                detailInfoImage.texture = currentInfoTexture;
                detailInfoImage.color = Color.white;
            }
            if (detailPhotoImage != null)
            {
                detailPhotoImage.texture = currentPhotoTexture;
                detailPhotoImage.color = Color.white;
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
                logger.Enqueue($"[DetailPanelManager] 디테일 패널 열기 완료: {filename1}, {filename2}");
        }
        else
        {
            // 하나라도 실패하면 에러 로그 출력 및 패널 숨김
            if (!success1 && logger != null)
                logger.Enqueue($"[DetailPanelManager] 이미지 로드 실패: {filename1}, 오류: {currentRequest1.error}");
            if (!success2 && logger != null)
                logger.Enqueue($"[DetailPanelManager] 이미지 로드 실패: {filename2}, 오류: {currentRequest2.error}");
            HidePanelImmediate();
        }

        // 요청 리소스 정리
        DisposeRequests();
        loadCoroutine = null;
    }

    /// <summary>
    /// 사용자 직접 터치(닫기 버튼)에 의해 호출됩니다.
    /// 패널을 닫음과 동시에 영상을 대기 영상(Idle)으로 전환합니다.
    /// </summary>
    public void OnCloseButtonClicked()
    {
        ClosePanel(returnToIdle: true);
    }

    /// <summary>
    /// 패널을 닫고 메모리를 해제합니다.
    /// returnToIdle이 true일 경우, 메인 시스템을 대기 영상으로 돌려보냅니다.
    /// </summary>
    public async void ClosePanel(bool returnToIdle = false)
    {
        int currentToken = ++loadToken;

        CancelLoading();

        // 메인 영상 복귀는 애니메이션과 동시에 즉각 반응하도록 선처리
        if (returnToIdle && playbackManager != null)
        {
            playbackManager.ReturnToIdle();
            if (logger != null) logger.Enqueue("[DetailPanelManager] 캡션 수동 닫힘 - 영상 대기 전환을 요청합니다.");
        }

        // 팝업 퇴장 애니메이션 실행
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

        // 애니메이션 대기 도중 새 캡션 로드가 시작되었다면 닫기 무효화
        if (loadToken != currentToken) return;

        // 애니메이션이 없었거나 끝난 후 최종 정리
        if (!hasAnimated)
        {
            HidePanelImmediate();
        }
        else
        {
            // 애니메이션이 끝났더라도 RawImage의 texture 참조를 null로 비워서 dangling reference 방지
            if (detailInfoImage != null) detailInfoImage.texture = null;
            if (detailPhotoImage != null) detailPhotoImage.texture = null;
        }
        ReleaseDetailTextures();
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

        DisposeRequests();
    }

    /// <summary>
    /// 현재 보유 중인 UnityWebRequest를 정리합니다.
    /// </summary>
    private void DisposeRequests()
    {
        if (currentRequest1 != null)
        {
            if (!currentRequest1.isDone) currentRequest1.Abort();
            currentRequest1.Dispose();
            currentRequest1 = null;
        }
        if (currentRequest2 != null)
        {
            if (!currentRequest2.isDone) currentRequest2.Abort();
            currentRequest2.Dispose();
            currentRequest2 = null;
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

        if (detailInfoImage != null) detailInfoImage.texture = null;
        if (detailPhotoImage != null) detailPhotoImage.texture = null;
    }

    /// <summary>
    /// 인물별 텍스처의 VRAM 메모리를 즉시 해제합니다.
    /// 공통 리소스(배경, 닫기 버튼)는 해제하지 않습니다.
    /// </summary>
    private void ReleaseDetailTextures()
    {
        if (currentInfoTexture != null)
        {
            Destroy(currentInfoTexture);
            currentInfoTexture = null;
        }
        if (currentPhotoTexture != null)
        {
            Destroy(currentPhotoTexture);
            currentPhotoTexture = null;
        }
    }
}

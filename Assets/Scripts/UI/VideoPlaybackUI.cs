using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using DG.Tweening;

public class VideoPlaybackUI : MonoBehaviour
{
    [Header("Button References")]
    [Tooltip("1~8번 인물 버튼 이미지 배열 (buttonImages[0]은 사용하지 않음)")]
    [SerializeField] private Image[] buttonImages;

    [Header("Standby Screen Images")]
    [Tooltip("대기화면 이미지 슬롯 3개 (0_1_main, 0_2_main, 0_3_main 동적 로드 대상)")]
    [SerializeField] private Image[] standbyImages;

    [Header("Feature Toggles")]
    [Tooltip("체크 해제 시 버튼 이미지 교체(On/Off) 기능이 비활성화됩니다.")]
    [SerializeField] private bool enableImageSwap = true;
    
    [Tooltip("체크 해제 시 버튼 흔들림(Shake) 효과가 비활성화됩니다.")]
    [SerializeField] private bool enableShakeEffect = true;

    [Header("Animation Settings")]
    [SerializeField] private float shakeAmplitude = 10f;
    [SerializeField] private float shakeDuration = 0.5f;

    [Header("Dependencies")]
    [SerializeField] private PlaybackManager playbackManager;
    [SerializeField] private Logger logger;

    private Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
    private Dictionary<int, Sequence> shakeSequences = new Dictionary<int, Sequence>();
    private Dictionary<int, Vector3> originalPositions = new Dictionary<int, Vector3>();
    private bool isInitialLoadingComplete = false;
    private bool isTransitioning = false;
    private UnityEngine.UI.GridLayoutGroup gridLayout;
    private int currentPlayingButtonIndex = -1;
    private HashSet<string> loadedOnSprites = new HashSet<string>();
    // 대기화면 이미지용 스프라이트 캐시 (별도 관리)
    private List<Sprite> standbySprites = new List<Sprite>();

    private void Start()
    {
        if (playbackManager == null) playbackManager = FindAnyObjectByType<PlaybackManager>();
        if (logger == null) logger = FindAnyObjectByType<Logger>();

        gridLayout = GetComponentInParent<UnityEngine.UI.GridLayoutGroup>();

        if (playbackManager != null)
        {
            playbackManager.OnPlaybackStateChanged += OnPlaybackStateChanged;
            playbackManager.OnTransitionComplete += OnTransitionComplete;
        }

        StartCoroutine(InitialSpriteLoad());
    }

    private void OnDestroy()
    {
        if (playbackManager != null)
        {
            playbackManager.OnPlaybackStateChanged -= OnPlaybackStateChanged;
            playbackManager.OnTransitionComplete -= OnTransitionComplete;
        }

        foreach (var seq in shakeSequences.Values)
        {
            seq.Kill();
        }
        shakeSequences.Clear();

        DestroyAllOnSprites();

        // _off 등 아직 해제되지 않은 모든 잔여 텍스처 강제 반환 (VRAM 누수 차단)
        foreach (var kvp in spriteCache)
        {
            Sprite sprite = kvp.Value;
            if (sprite != null)
            {
                if (sprite.texture != null)
                {
                    Destroy(sprite.texture);
                }
                Destroy(sprite);
            }
        }
        spriteCache.Clear();

        // 대기화면 이미지 텍스처 해제
        foreach (var sprite in standbySprites)
        {
            if (sprite != null)
            {
                if (sprite.texture != null) Destroy(sprite.texture);
                Destroy(sprite);
            }
        }
        standbySprites.Clear();
    }

    private IEnumerator InitialSpriteLoad()
    {
        int buttonCount = buttonImages != null ? buttonImages.Length : 0;

        UpdateAllButtonsInteractable(false);

        List<Coroutine> loadCoroutines = new List<Coroutine>();

        // 대기화면 이미지 3장 로드 (0_1_main, 0_2_main, 0_3_main)
        int standbyCount = standbyImages != null ? standbyImages.Length : 0;
        for (int i = 0; i < standbyCount; i++)
        {
            loadCoroutines.Add(StartCoroutine(LoadStandbyImageCoroutine(i)));
        }

        // 인물 버튼 off 이미지 로드 (1번부터)
        for (int i = 1; i < buttonCount; i++)
        {
            loadCoroutines.Add(StartCoroutine(LoadSpriteCoroutine(i, false)));
        }

        foreach (var coroutine in loadCoroutines)
        {
            yield return coroutine;
        }

        // 인물 버튼 off 이미지 적용 (1번부터)
        for (int i = 1; i < buttonCount; i++)
        {
            SetButtonImage(i, false);
        }

        isInitialLoadingComplete = true;
        UpdateAllButtonsInteractable(true);

        if (logger != null) logger.Enqueue("[VideoPlaybackUI] 초기 스프라이트 로딩 완료");
    }

    /// <summary>
    /// 대기화면 이미지를 StreamingAssets에서 로드하여 standbyImages에 적용합니다.
    /// 파일명: 0_{slotIndex+1}_main.png
    /// </summary>
    private IEnumerator LoadStandbyImageCoroutine(int slotIndex)
    {
        string filename = $"0_{slotIndex + 1}_main.png";
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, filename);

        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(path))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                texture.filterMode = FilterMode.Bilinear;

                // 대기화면 이미지의 RectTransform 기준으로 PPU 계산
                float ppu = 100f;
                if (standbyImages != null && slotIndex < standbyImages.Length && standbyImages[slotIndex] != null)
                {
                    RectTransform rect = standbyImages[slotIndex].rectTransform;
                    Vector2 size = rect.sizeDelta;
                    if (size.x > 0 && size.y > 0)
                    {
                        float ppuW = texture.width / size.x;
                        float ppuH = texture.height / size.y;
                        ppu = (ppuW + ppuH) / 2f;
                    }
                }

                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    ppu
                );
                sprite.name = filename;
                standbySprites.Add(sprite);

                // 슬롯에 즉시 적용
                if (standbyImages != null && slotIndex < standbyImages.Length && standbyImages[slotIndex] != null)
                {
                    standbyImages[slotIndex].sprite = sprite;
                }

                if (logger != null) logger.Enqueue($"[VideoPlaybackUI] 대기화면 이미지 로드: {filename}, PPU: {ppu:F2}");
            }
            else
            {
                if (logger != null) logger.Enqueue($"[VideoPlaybackUI] 대기화면 이미지 로드 실패: {filename}, 오류: {request.error}");
            }
        }
    }

    private IEnumerator LoadSpriteCoroutine(int index, bool isOn)
    {
        // index 0은 더 이상 여기서 처리하지 않음 (LoadStandbyImageCoroutine에서 담당)
        if (index <= 0) yield break;

        string filename = $"{index}_{(isOn ? "on" : "off")}.png";

        string path = System.IO.Path.Combine(Application.streamingAssetsPath, filename);

        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(path))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                texture.filterMode = FilterMode.Point;

                float ppu = CalculatePixelsPerUnit(texture, index);

                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    ppu
                );
                sprite.name = filename;

                spriteCache[filename] = sprite;

                if (logger != null) logger.Enqueue($"[VideoPlaybackUI] Loaded sprite: {filename}, PPU: {ppu:F2}");
            }
            else
            {
                if (logger != null) logger.Enqueue($"[VideoPlaybackUI] Failed to load sprite: {filename}, Error: {request.error}");
            }
        }
    }

    private float CalculatePixelsPerUnit(Texture2D texture, int index)
    {
        if (buttonImages == null || index < 0 || index >= buttonImages.Length)
            return 1f;

        RectTransform rect = buttonImages[index].rectTransform;
        Vector2 size = rect.sizeDelta;

        if (size.x <= 0 || size.y <= 0)
            return 1f;

        float ppuW = texture.width / size.x;
        float ppuH = texture.height / size.y;

        return (ppuW + ppuH) / 2f;
    }

    private Sprite GetCachedSprite(int index, bool isOn)
    {
        if (index <= 0) return null;

        string filename = $"{index}_{(isOn ? "on" : "off")}.png";
        
        if (spriteCache.TryGetValue(filename, out Sprite cachedSprite))
        {
            return cachedSprite;
        }
        return null;
    }

    private void LoadOnSpriteIfNeeded(int index)
    {
        if (index <= 0) return;

        string filename = $"{index}_on.png";
        if (loadedOnSprites.Contains(filename)) return;

        string path = System.IO.Path.Combine(Application.streamingAssetsPath, filename);
        byte[] bytes = System.IO.File.ReadAllBytes(path);
        Texture2D texture = new Texture2D(2, 2);
        texture.LoadImage(bytes);
        texture.filterMode = FilterMode.Point;

        float ppu = CalculatePixelsPerUnit(texture, index);

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            ppu
        );
        sprite.name = filename;

        spriteCache[filename] = sprite;
        loadedOnSprites.Add(filename);

        if (logger != null) logger.Enqueue($"[VideoPlaybackUI] Loaded ON sprite sync: {filename}, PPU: {ppu:F2}");
    }

    private bool IsOnSpriteLoaded(int index)
    {
        if (index <= 0) return true;
        string filename = $"{index}_on.png";
        return spriteCache.ContainsKey(filename);
    }

    private void DestroyAllOnSprites()
    {
        int count = loadedOnSprites.Count;
        foreach (var filename in loadedOnSprites)
        {
            if (spriteCache.TryGetValue(filename, out Sprite sprite))
            {
                if (sprite != null)
                {
                    if (sprite.texture != null)
                    {
                        Destroy(sprite.texture);
                    }
                    Destroy(sprite);
                }
                spriteCache.Remove(filename);
            }
        }
        loadedOnSprites.Clear();
        if (logger != null) logger.Enqueue($"[VideoPlaybackUI] Destroyed {count} ON sprites, cache count: {spriteCache.Count}");
    }

    private void OnPlaybackStateChanged(bool isPlaying, int index)
    {
        if (isPlaying)
        {
            if (currentPlayingButtonIndex >= 0 && currentPlayingButtonIndex != index)
            {
                StopShake(currentPlayingButtonIndex);
                SetButtonImage(currentPlayingButtonIndex, false);
            }
            
            if (index >= 0 && index < buttonImages.Length)
            {
                currentPlayingButtonIndex = index;
                if (enableImageSwap)
                {
                    LoadOnSpriteIfNeeded(index);
                    SetButtonImage(index, true);
                }
                if (enableShakeEffect)
                {
                    PlayShake(index);
                }
            }
        }
        else
        {
            if (index >= 0 && index < buttonImages.Length)
            {
                StopShake(index);
                SetButtonImage(index, false);
            }

            if (index == -1)
            {
                StopShake(currentPlayingButtonIndex);
                currentPlayingButtonIndex = -1;
                ResetAllButtonsToOff();
                DestroyAllOnSprites();
            }
        }
    }

    private void OnTransitionComplete(bool success)
    {
        isTransitioning = false;
        UpdateAllButtonsInteractable(true);
    }

    public void OnButtonClicked(int index)
    {
        if (!isInitialLoadingComplete)
        {
            if (logger != null) logger.Enqueue($"[VideoPlaybackUI] Ignored: Initial loading not complete");
            return;
        }

        if (playbackManager != null && playbackManager.IsTransitioning)
        {
            if (logger != null) logger.Enqueue($"[VideoPlaybackUI] Ignored: Transition in progress");
            return;
        }

        if (playbackManager != null)
        {
            isTransitioning = true;
            UpdateAllButtonsInteractable(false);
            playbackManager.TransitionTo(index);
        }
    }

    private void SetButtonImage(int index, bool isOn)
    {
        // enableImageSwap이 꺼져 있어도 off(기본) 이미지는 항상 적용 허용
        // on 이미지 전환만 차단하여, 버튼이 빈 상태로 남는 것을 방지
        if (!enableImageSwap && isOn) return;
        if (index < 0 || index >= buttonImages.Length) return;

        Sprite sprite = GetCachedSprite(index, isOn);
        if (sprite != null)
        {
            buttonImages[index].sprite = sprite;
        }
        else
        {
            if (logger != null) logger.Enqueue($"[VideoPlaybackUI] Sprite not found: {index}_{ (isOn ? "on" : "off" )}");
        }
    }

    private void PlayShake(int index)
    {
        if (!enableShakeEffect) return;
        if (index < 0 || index >= buttonImages.Length) return;

        StopShake(index);

        if (gridLayout != null) gridLayout.enabled = false;

        RectTransform rect = buttonImages[index].rectTransform;
        Vector3 originalPos = rect.localPosition;
        originalPositions[index] = originalPos;

        Sequence seq = DOTween.Sequence();
        seq.Append(rect.DOLocalMoveY(originalPos.y + shakeAmplitude, shakeDuration / 2f).SetEase(Ease.InOutSine));
        seq.Append(rect.DOLocalMoveY(originalPos.y - shakeAmplitude, shakeDuration / 2f).SetEase(Ease.InOutSine));
        seq.SetLoops(-1, LoopType.Yoyo);
        seq.Play();

        shakeSequences[index] = seq;
    }

    private void StopShake(int index)
    {
        if (shakeSequences.TryGetValue(index, out Sequence seq))
        {
            seq.Kill();
            shakeSequences.Remove(index);

            RectTransform rect = buttonImages[index].rectTransform;

            if (originalPositions.TryGetValue(index, out Vector3 originalPos))
            {
                rect.localPosition = originalPos;
                originalPositions.Remove(index);
            }

            if (gridLayout != null) gridLayout.enabled = true;
        }
    }

    private void UpdateAllButtonsInteractable(bool interactable)
    {
        if (buttonImages == null) return;

        foreach (var img in buttonImages)
        {
            if (img != null && img.GetComponent<Button>() != null)
            {
                img.GetComponent<Button>().interactable = interactable;
            }
        }
    }

    private void ResetAllButtonsToOff()
    {
        if (buttonImages == null) return;

        for (int i = 1; i < buttonImages.Length; i++)
        {
            StopShake(i);
            SetButtonImage(i, false);
        }

        if (gridLayout != null && shakeSequences.Count == 0)
        {
            gridLayout.enabled = true;
        }
    }
}

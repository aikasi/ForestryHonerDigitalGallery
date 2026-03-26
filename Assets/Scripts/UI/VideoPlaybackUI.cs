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
    [SerializeField] private Image[] buttonImages;

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
    }

    private IEnumerator InitialSpriteLoad()
    {
        int buttonCount = buttonImages != null ? buttonImages.Length : 0;

        UpdateAllButtonsInteractable(false);

        List<Coroutine> loadCoroutines = new List<Coroutine>();

        loadCoroutines.Add(StartCoroutine(LoadSpriteCoroutine(0, false)));
        for (int i = 1; i < buttonCount; i++)
        {
            loadCoroutines.Add(StartCoroutine(LoadSpriteCoroutine(i, false)));
        }

        foreach (var coroutine in loadCoroutines)
        {
            yield return coroutine;
        }

        SetButtonImage(0, false);
        for (int i = 1; i < buttonCount; i++)
        {
            SetButtonImage(i, false);
        }

        isInitialLoadingComplete = true;
        UpdateAllButtonsInteractable(true);

        if (logger != null) logger.Enqueue("[VideoPlaybackUI] Initial sprite loading complete");
    }

    private IEnumerator LoadSpriteCoroutine(int index, bool isOn)
    {
        string filename;
        if (index == 0)
        {
            filename = "0_main.png";
        }
        else
        {
            filename = $"{index}_{ (isOn ? "on" : "off" )}.png";
        }

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
        string filename;
        if (index == 0)
        {
            filename = "0_main.png";
        }
        else
        {
            filename = $"{index}_{ (isOn ? "on" : "off" )}.png";
        }

        if (spriteCache.TryGetValue(filename, out Sprite sprite))
        {
            return sprite;
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
                LoadOnSpriteIfNeeded(index);
                SetButtonImage(index, true);
                PlayShake(index);
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

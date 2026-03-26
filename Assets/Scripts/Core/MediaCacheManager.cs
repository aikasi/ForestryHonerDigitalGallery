using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 미디어 파일 경로 스캔 및 캐싱, 파일 검증을 담당하는 매니저
/// CSVReader(-1000) → DisplayInitializer(-900) → MediaCacheManager(-800) → 나머지 스크립트(Start)
/// Awake에서 캐싱을 완료하여, PlaybackManager 등 Start()에서 캐시를 사용하는 스크립트의 실행 순서에 의존하지 않도록 보장
/// </summary>
[DefaultExecutionOrder(-800)]
public class MediaCacheManager : MonoBehaviour
{
    [SerializeField] private Logger logger;

    [Tooltip("캐실할 최대 비디오 인덱스 (기본 8인 경우 00.mp4 ~ 08.mp4)")]
    [SerializeField] private int maxVideoIndex = 8;

    // 비디오 데이터 캐시 (Index -> MediaData)
    private readonly Dictionary<int, MediaData> mediaCache = new Dictionary<int, MediaData>();

    public bool ShowErrorOnLoadFail { get; private set; }
    public string[] SupportedExtensions { get; private set; }

    private void Awake()
    {
        if (logger == null)
            logger = FindAnyObjectByType<Logger>();

        LoadSettings();
        CacheMediaFiles();
    }

    private void LoadSettings()
    {
        string showErrorStr = CSVReader.GetStringValue("ShowErrorOnLoadFail", "false");
        ShowErrorOnLoadFail = bool.TryParse(showErrorStr, out bool parsed) && parsed;

        string extStr = CSVReader.GetStringValue("SupportedExtensions", ".mp4;.mov;.webm;.mkv;.avi");
        SupportedExtensions = extStr.Split(';');
    }

    private void CacheMediaFiles()
    {
        string streamingAssetsPath = Application.streamingAssetsPath;
        List<string> errorMessages = new List<string>();

        // 00.* (Idle) ~ maxVideoIndex.*
        for (int i = 0; i <= maxVideoIndex; i++)
        {
            string foundFileName = null;
            string absolutePath = null;
            bool isValid = false;

            // 허용된 확장자(.mp4, .mov 등) 목록을 돌면서 가장 먼저 발견되는 영상 파일 채택
            foreach (var ext in SupportedExtensions)
            {
                string cleanExt = ext.Trim();
                if (!cleanExt.StartsWith(".")) cleanExt = "." + cleanExt;

                string testFileName = $"{i:D2}{cleanExt}";
                string testPath = Path.Combine(streamingAssetsPath, testFileName);

                if (File.Exists(testPath))
                {
                    foundFileName = testFileName;
                    absolutePath = testPath;
                    break;
                }
            }

            // 파일 검증
            if (foundFileName != null)
            {
                FileInfo fileInfo = new FileInfo(absolutePath);
                if (fileInfo.Length > 0)
                {
                    isValid = true;
                }
                else
                {
                    string msg = $" 파일 손상(0바이트): {foundFileName} : 파일을 교체해 주세요.";
                    if (logger != null) logger.Enqueue(msg);
                    errorMessages.Add(msg);
                }
            }
            else
            {
                string msg = $" 파일 누락: {i:D2} 번호의 영상을 하나라도 찾을 수 없습니다. 지원 확장자: {string.Join(", ", SupportedExtensions)} 파일을 추가해주세요.";
                if (logger != null) logger.Enqueue(msg);
                errorMessages.Add(msg);

                // 앱이 크래시 나지 않도록 임시 기본값 연결 (에러 UI가 방어함)
                absolutePath = Path.Combine(streamingAssetsPath, $"{i:D2}.mp4");
            }

            mediaCache[i] = new MediaData(i, absolutePath, isValid);
        }

        // ShowErrorOnLoadFail 옵션이 켜져 있고 오류가 발생했다면 터치 UI(ErrorDisplayUI)에 표시
        if (ShowErrorOnLoadFail && errorMessages.Count > 0)
        {
            // Unity 최신 버전 Obsolete 경고(CS0618) 해결: FindObjectsInactive.Include 사용하여 비활성화 객체 탐색
            ErrorDisplayUI errorUI = FindAnyObjectByType<ErrorDisplayUI>(FindObjectsInactive.Include);
            if (errorUI != null)
            {
                // 게임 오브젝트 자체가 꺼져있다면 강제로 켬
                errorUI.gameObject.SetActive(true);
                errorUI.Show(string.Join("\n", errorMessages));
            }
            else
            {
                if (logger != null) logger.Enqueue(" ErrorDisplayUI 컴포넌트를 찾을 수 없어 캐싱 에러를 UI에 표시하지 못했습니다.");
            }
        }
    }

    /// <summary>
    /// 캐싱된 미디어 데이터를 반환합니다.
    /// </summary>
    public MediaData? GetMediaData(int index)
    {
        if (mediaCache.TryGetValue(index, out MediaData data))
        {
            return data;
        }
        return null;
    }
}

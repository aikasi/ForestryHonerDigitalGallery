using UnityEngine;

/// <summary>
/// 듀얼 모니터 활성화 및 해상도 설정을 담당하는 초기화 클래스
/// </summary>
[DefaultExecutionOrder(-900)] // CSVReader(-1000) 이후, 다른 스크립트보다 먼저 실행
public class DisplayInitializer : MonoBehaviour
{
    private void Awake()
    {
        InitializeDisplays();
    }

    private void InitializeDisplays()
    {
        // Settings.txt에서 해상도 정보 로드 (CSVReader가 먼저 Awake에서 파싱해둠)
        int mainWidth = CSVReader.GetIntValue("MainScreenWidth", 1920);
        int mainHeight = CSVReader.GetIntValue("MainScreenHeight", 1080);
        int subWidth = CSVReader.GetIntValue("SubScreenWidth", 1920);
        int subHeight = CSVReader.GetIntValue("SubScreenHeight", 1080);

        // 메인 모니터 (Display 0) 해상도 설정
        Screen.SetResolution(mainWidth, mainHeight, FullScreenMode.FullScreenWindow);
        Debug.Log($"[DisplayInitializer] Display 0 (Main/Touch) Set Resolution: {mainWidth}x{mainHeight}");

        // 서브 모니터 (Display 1) 활성화 및 해상도 설정
        if (Display.displays.Length > 1)
        {
            Display.displays[1].Activate(subWidth, subHeight, new RefreshRate { numerator = 60, denominator = 1 });
            Debug.Log($"[DisplayInitializer] Display 1 (Sub/Video) Activated: {subWidth}x{subHeight}");
        }
        else
        {
            Debug.LogWarning("[DisplayInitializer] Display 1 is not available. Please check monitor connection.");
        }

        // 런타임 백그라운드 실행 유지 (터치 중 포커스 상실로 인한 비디오 정지 원천 차단)
        Application.runInBackground = true;
        
        // 마우스 커서 숨김 처리 (Settings.txt 설정값 기반)
        string hideCursorStr = CSVReader.GetStringValue("HideCursor", "true");
        bool hideCursor = true; // 기본값 true (키오스크 환경)
        if (bool.TryParse(hideCursorStr, out bool parsedHide))
        {
            hideCursor = parsedHide;
        }
        
        Cursor.visible = !hideCursor;
        if (hideCursor)
        {
            Cursor.lockState = CursorLockMode.Confined; // 커서를 창 내부로 가둠 (Locked는 중앙 고정이라 UI 입력과 충돌)
        }
        
        Debug.Log($"[DisplayInitializer] Application.runInBackground=true, Cursor.visible={!hideCursor}");
    }
}

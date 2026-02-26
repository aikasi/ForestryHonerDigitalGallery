# Forestry Honer Digital Gallery(D.1-4)

Unity 기반 듀얼 모니터 인터랙티브 비디오 갤러리 플레이어 시스템입니다. 터치스크린(메인)에서 영상 버튼을 선택하면 대형 디스플레이(서브)에서 AVPro Video 기반의 **핑퐁 듀얼 버퍼 크로스페이드** 기법으로 자연스럽고 매끄럽게 영상이 전환됩니다. 전시, 키오스크, 미디어 아트 등 장기간 안정을 요구하는 프로덕션 환경에 맞춰 개발되었습니다.

---

## 🌟 주요 기능

- **듀얼 모니터 지원**: 메인 터치 UI (Display 0)와 영상 재생 화면 (Display 1)으로 완전 분리.
- **핑퐁 버퍼 크로스페이드**: 2개의 AVPro `MediaPlayer`를 교차(A↔B) 사용하며 DOTween으로 끊김 없이 투명도를 교차(Crossfade)하여 전환합니다. 연타 방지 상태 잠금이 내장되어 안정적입니다.
- **강력한 자가 복구 (Watchdog)**: AVPro 텍스처 프레임을 주기적으로 모니터링하여, 디코더 병목 등 재생기 프리징(단순 멈춤) 발생 시 자동으로 자체 재시도 후 대기 영상(00.mp4)으로 강제 복구시킵니다.
- **유연한 외부 환경 설정**: 빌드된 실행 파일 주변에 있는 `Settings.txt`로 해상도, 음소거, 전환 속도 딜레이, 화면 비율 고정 옵션(ScaleMode), 커서 숨김 처리를 재빌드 없이 현장에서 즉각 수정할 수 있습니다.
- **스마트 캐싱 및 에러 방어**: 앱 시작 시 `StreamingAssets` 내 지정된 비디오 파일의 누락이나 0바이트 손상을 스캔/감지하여 전용 Error UI를 띄워 관리자에게 즉각 알립니다.
- **스레드 안전 로깅 시스템**: `ConcurrentQueue` 기반의 커스텀 파일 로거를 탑재하여 멀티스레드 환경의 Unity Log 이벤트를 병목 없이 안전하게 텍스트로 기록보관합니다. (정상 작동 로그는 최소화, 에러/복구/타임아웃 이벤트에 특화)

---


## 📂 스크립트 구조 (Architecture)

프로젝트 내 커스텀 코드들은 모두 `Assets/Scripts/` 하위에 기능 단위를 모듈화하여 4개 폴더로 명확하게 분류되어 있습니다.

```text
Assets/Scripts/
 ├── Core/
 │    ├── PlaybackManager.cs       # 핵심: AVPro 핑퐁 로드 및 DOTween 알파 제어
 │    ├── MediaCacheManager.cs     # 초기 파일 스캔 및 유효성(0바이트 등) 사전 검증
 │    ├── VideoWatchdog.cs         # 프레임 프리징 감지 및 자가 복구(강제 리셋) 제어
 │    └── DisplayInitializer.cs    # 듀얼모니터 활성 및 Background 실행 강제화 처리
 ├── UI/
 │    ├── VideoInputController.cs  # 터치 버튼 클릭 수신, 전환 중첩 방지 잠금 관리
 │    └── ErrorDisplayUI.cs        # 손상 파일 또는 Runtime 로드 실패 시 UI 경고창
 ├── Data/
 │    └── MediaData.cs             # 미디어 파일의 순번, 절대경로, 손상 여부 보관(Struct)
 └── Utils/
      ├── Logger.cs                # 멀티스레드 안전, 날짜별 텍스트 파일 로거
      └── CSVReader.cs             # Settings.txt 외부 환경 변수 파서
```

---

## ⚙️ 외부 설정 파일 가이드 (`Settings.txt`)

실행 파일(`.exe`) 또는 에디터의 `Assets/` 루트에 위치한 `Settings.txt`를 통해 값을 런타임에 제어합니다. CSV 기반이므로 쉼표(,)를 기준으로 읽습니다.

```csv
# Key, Value, Comment
MainScreenWidth, 1920, 터치 모니터 가로 해상도 (Display 0)
MainScreenHeight, 1080, 터치 모니터 세로 해상도 (Display 0)
SubScreenWidth, 1920, 영상 모니터 가로 해상도 (Display 1)
SubScreenHeight, 1080, 영상 모니터 세로 해상도 (Display 1)
CrossfadeDuration, 1, 크로스페이드 전환 시간(초)
LoadTimeout, 5, 비디오 로드 타임아웃(초)
ShowErrorOnLoadFail, true, 비디오 로드 실패 시 터치 화면에 에러 문구 표시 여부 (false 시 무시)
WatchdogRetryCount, 3, 프리징 감지 시 시스템 자체 재시도(Play) 허용 횟수
WatchdogStallThreshold, 2.0, 프레임 정지 감지 임계값 시간(초)
IdleVideoIndex, 0, 기본 대기 영상 인덱스 번호 (기본: 00.mp4)
HideCursor, false, 마우스 커서 화면에서 숨김 및 창 밖 이탈 방지 여부 (true/false)
VideoMute, true, 모든 영상 음소거 여부 (true/false)
VideoScaleMode, ScaleToFit, 화면 맞춤 표시 방식 (StretchToFill / ScaleToFit / ScaleToFill)
SupportedExtensions, .mp4;.mov;.webm;.mkv;.avi, 지원할 비디오 확장자 파싱 목록 (; 기호로 구분)
```

---



## 📝 자동 에러 방어 및 텍스트 로깅 정보

장기간 무인으로 구동되는 전시 환경을 위해 두 가지 강력한 에러 자가 치유(Self-Healing) 체계와 그 로그 기록 기능이 내장되어 있습니다.

### 1. 초기 누락/손상 파일 전수 스캔 (MediaCacheManager)

- **에러 감지**: 앱 구동(`Awake`) 시 `StreamingAssets` 폴더 내에 모든 지정된 비디오 파일이 1개라도 누락되었거나, 0바이트로 용량이 파손되었는지 전수 스캔합니다.
- **에러 복구/핸들링**: 결함 발견 시 비활성화되어 있는 `ErrorDisplayUI` 컴포넌트까지 강제로 추적/활성화시켜 터치 모니터 중앙에 구체적인 에러 사유를 즉각 띄워 관리자에게 알리고 추가적인 비정상 구동(Crash)을 방지합니다.

### 2. 런타임 프레임 멈춤(Freezing) 감지 및 자가 힐링 (VideoWatchdog)

- **에러 감지**: 정상 재생 중이던 AVPro `MediaPlayer`의 텍스처 프레임 렌더링이 디스플레이 케이블 결함이나 코덱(디코더) 충돌 등으로 미처 꺼지지 못하고 그대로 멈추는 프리징 현상을 내부적으로 1초 간격으로 감지합니다.
- **복구 메커니즘**: 멈춤 확인 시 묻지도 따지지도 않고 그 자리에서 자체적으로 영상 재생(`Play()`)을 재시도합니다(기본 3회). 이마저 한계치에 다다르면 현재 재생기를 완전히 강제 리셋하고 무조건 **대기 영상(`00.mp4`)으로 비상 릴레이 복귀**하여 먹통 화면이 방치되는 치명적인 사고를 막습니다.

### 🗂️ 텍스트 로그 파일 저장 경로

실행 중 발생하는 에러, 경고, 복구 이력이 자동으로 기록됩니다.

- **Windows 기준 저장 폴더 위치:** `%USERPROFILE%\AppData\LocalLow\MetaDevs\ForestryHonerDigitalGallery\Logs\`

>
> * 앱 실행 시마다 `yyMMdd-HHmmss.log` 형식으로 새 파일이 생성됩니다.
> * 최대 1,000개까지 보관되며, 초과 시 가장 오래된 파일부터 자동 삭제됩니다.

---

## 📌 프로젝트 요구사항 (Dependencies)

- **Unity 버전**: 6000.3.9f1
- **필수 플러그인 1**: RenderHeads AVPro Video (v2.x~ / v3.x~) - *코어 미디어 재생 모듈*
- **필수 플러그인 2**: Demigiant DOTween (무료/Pro 무관) - *UI 패널 트랜지션 및 알파 페이드 애니메이션 구현*

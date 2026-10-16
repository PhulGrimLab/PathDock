# 01. 아키텍처 및 MVVM 구조

> 상위: [기획서](../PathDock_개발개요.md) 4절을 구체화한다.

## 1. 계층

```
PathDock.App (WPF, net10.0-windows)
 ├─ Views/            MainWindow, FavoritesBarView, NavButtonsView, SyncModeSelectorView, DrivePanelView,
 │                    AncestorColumnView, FileListView, TerminalPanelView, StatusBarView,
 │                    SettingsWindow, AboutWindow, InputDialog, ConfirmDialog (XAML, code-behind 최소; 상세 doc/05)
 ├─ Controls/         PanelCard, DriveCard, FavoriteChip, UsageMeter, AddressBar, TerminalControl(+TerminalRenderer, TerminalKeyMap), AlertHost(알림 막대 접근성)
 ├─ ViewModels/       MainViewModel(조립)+InfoBar/Usage/Drives/Favorites/SettingsViewModel, ExplorerViewModel(+FileActions), TerminalViewModel(+TerminalTab: 탭별 세션·상태),
 │                    FavoriteSlotViewModel, DriveItemViewModel, FileItemViewModel
 ├─ Localization/     LocSource ({loc:T} XAML 마크업 확장)
 ├─ Themes/           Generic.xaml (커스텀 컨트롤 스타일)
 └─ Services/         ThemeService, FontService, DialogService, FileClipboard, RecycleBinService, CalculatorLauncher,
                      ImagePreviewService/TextPreviewService/PreviewCache, AppInfo, SplashHost(시작 로딩 팝업, 순수 Win32)
PathDock.Core (net10.0, UI 비의존 → 단위 테스트 용이)
 ├─ Paths/            PathNormalizer
 ├─ Settings/         AppSettings, SettingsService (JSON)
 ├─ Explorer/         ExplorerService(목록 열거, 이력), DriveService, FolderSizeCalculator/Cache, FileSorter,
 │                    FileOperations(이름 변경·새 폴더·복사·이동·휴지통), FileCategory, SizeFormatter, TextPreview, ImagePreviewTypes
 ├─ Sync/             SyncMode, PathSyncService (중재, 루프 방지, 대기 정책)
 ├─ Terminal/         ConPtySession, VtParser, ScreenBuffer, TerminalSession(셸 통합·상태), TerminalShell, TerminalColorScheme
 ├─ Monitoring/       SystemMonitor
 ├─ Localization/     Loc (한국어/영어 문구 선택)
 └─ Logging/          AsyncFileLogger
```

- ViewModel은 `ObservableObject` + `[ObservableProperty]` / `[RelayCommand]`. View는 DataContext 바인딩만 하고
  code-behind에는 UI 전용 코드(포커스, 스크롤)만 둔다.
- ViewModel 간 통신은 직접 참조 대신 **PathSyncService**를 통한다 (Explorer ↔ Terminal이 서로를 모른다).
- 스레드: ConPTY 읽기 스레드 → `ScreenBuffer`(lock) → UI는 33ms 타이머로 변경 감지 시에만 다시 그린다.

## 2. 핵심 타입 (책임 한 줄)

| 타입 | 책임 |
|---|---|
| `PathNormalizer` | 전체 경로화, 후행 `\` 제거, 드라이브 문자 대문자화, UNC 보존, 비교용 키 |
| `FolderSizeCache` | 폴더 크기 결과 캐시(메모리+JSON, TTL·수정시각으로 Fresh/Stale 판정) |
| `SystemMonitor` | CPU/메모리/GPU 사용률 주기 측정(Timer 스레드, 이벤트) |
| `FolderSizeCalculator` | 폴더 재귀 크기/파일·폴더 수 집계(취소 가능, 링크 미추적, 접근 불가는 Incomplete) |
| `DriveService` | 드라이브 목록과 용량(총/여유) 조회. 준비 안 된 드라이브는 용량 0으로 표시 |
| `ExplorerService` | 폴더 열거(폴더 우선 정렬), 접근 불가 처리, 뒤로/앞으로/위로 이력 |
| `SettingsService` | 즐겨찾기(최대 10), 동기화 모드, 창 위치/크기, 분할 높이, 테마/글자 크기/터미널 셸·색/언어 JSON 저장/복원 (항목은 doc/00 4절) |
| `FileOperations` | 이름 검증·변경, 새 폴더, 복사/이동(이름 충돌 시 "- 복사"), 휴지통 삭제. 상태 없음 |
| `FileSorter` / `FileCategory` | 정렬(폴더 우선·자연 정렬) / 확장자 → 파일 종류 분류 |
| `TerminalTabTitle` | 탭 제목 규칙("셸 · 현재 폴더 이름"). `TerminalViewModel`/`TerminalTab`이 탭 목록·선택·세션 수명을 관리(doc/02 8절) |
| `TerminalThemes` | 터미널 테마 5종과 사용자 지정 색 해석(`Find`/`Resolve`) |
| `Loc` | 화면 문구 `Loc.T(한국어, English)`, 영어 단수/복수, 언어 변경 이벤트 |
| `ThemeService` (App) | 다크/라이트/고대비 적용, 주소 표시줄·드라이브 패널 배경색, 밝은 테마 보조 글자색 대비 보정 |
| `PathSyncService` | 모드 적용, 출처(Origin) 구분, 동일 경로 무시, 터미널 Busy 시 보류 |
| `ConPtySession` | `CreatePseudoConsole` + PowerShell 프로세스, 입출력 스트림, 크기 조절 |
| `VtParser` / `ScreenBuffer` | VT 시퀀스 해석(커서, 지우기, SGR 색, OSC) → 셀 격자 |
| `TerminalSession` | ConPTY+파서+버퍼 묶음, Idle/Busy·cwd 상태, 셸 통합(PowerShell은 prompt 훅, cmd는 `PROMPT` 문자열로 창 제목 `PD:…` 마커, doc/02 3절), 보류 이동 |

## 3. 컨트롤 적용 (WpfControlSamples 참조)

초기 계획에서 많이 바뀌었다. 현재 화면은 자체 커스텀 컨트롤(`PanelCard`, `DriveCard`, `FavoriteChip`, `UsageMeter`, `AddressBar`)과
뷰로 구성되며 상세는 [05_UI_Design.md](./05_UI_Design.md). 아래는 WPF-UI를 그대로 쓰는 부분이다.

| 용도 | 사용 컨트롤 | 출처 |
|---|---|---|
| 창 | `ui:FluentWindow` + `ui:TitleBar` | wpfui |
| 동기화 모드/설정 콤보 | `ComboBox` (wpfui 스타일 자동 적용) | wpfui |
| 알림 | `ui:InfoBar` (경로 없음/권한 없음/대기 중, 종류별 색, 성공은 자동 닫힘) | wpfui |
| 파일 목록 | `ListView` + `GridView` (이름/수정일/크기/하위 항목) | wpfui 스타일 |
| 터미널 | **자체 `TerminalControl`** (`FrameworkElement`, `OnRender`) | 자체 구현 |
| 분할 | `GridSplitter` | WPF |

## 4. 동기화 데이터 흐름

```
Explorer 이동(사용자) ──▶ PathSyncService.OnExplorerNavigated(path, Origin.User)
      모드∈{양방향, 탐색기→터미널} ──▶ TerminalService.RequestChangeDirectory(path)   (Idle일 때만 전송)
Terminal prompt 마커(cwd) ──▶ PathSyncService.OnTerminalCwdChanged(path)
      모드∈{양방향, 터미널→탐색기} ──▶ ExplorerViewModel.NavigateTo(path, Origin.Sync)
```
세부 규칙은 [02_Terminal_And_PathSync_Design.md](./02_Terminal_And_PathSync_Design.md).

## 5. 로그 (최소 항목)

| 이벤트 | 레벨 |
|---|---|
| 앱 시작/종료, 설정 로드/저장 실패 | Info/Warn |
| 탐색기 이동(경로, 출처) | Info |
| 터미널 세션 시작/종료(exit code) | Info |
| 동기화 판정(전파/무시 사유: 동일경로·모드·Busy 보류·경로 없음) | Info |
| 예외 | Error |

`AsyncFileLogger`: `Channel<string>` + 전용 Writer 스레드 1개. 호출 측(UI/읽기 스레드)은 큐에 넣기만 하고 파일을 기다리지 않는다.

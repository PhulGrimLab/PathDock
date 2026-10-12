# 01. 아키텍처 및 MVVM 구조

> 상위: [기획서](../PathDock_개발개요.md) 4절을 구체화한다.

## 1. 계층

```
PathDock.App (WPF, net10.0-windows)
 ├─ Views/            MainWindow, FavoritesBar, FileExplorerView, SettingsDialog (XAML, code-behind 최소)
 ├─ Controls/         TerminalControl (커스텀 컨트롤: 화면 버퍼 렌더링 + 키 입력)
 ├─ ViewModels/       MainViewModel, FavoritesViewModel, ExplorerViewModel, TerminalViewModel
 └─ Services/         UiDispatcher 어댑터, 폴더 선택 대화상자
PathDock.Core (net10.0, UI 비의존 → 단위 테스트 용이)
 ├─ Paths/            PathNormalizer
 ├─ Settings/         AppSettings, SettingsService (JSON)
 ├─ Explorer/         ExplorerService (목록 열거, 이력)
 ├─ Sync/             SyncMode, PathSyncService (중재, 루프 방지, 대기 정책)
 ├─ Terminal/         ConPtySession, VtParser, ScreenBuffer, TerminalSession(셸 통합·상태)
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
| `SettingsService` | 즐겨찾기(최대 10), 동기화 모드, 창 위치/크기, 분할 높이 JSON 저장/복원 |
| `PathSyncService` | 모드 적용, 출처(Origin) 구분, 동일 경로 무시, 터미널 Busy 시 보류 |
| `ConPtySession` | `CreatePseudoConsole` + PowerShell 프로세스, 입출력 스트림, 크기 조절 |
| `VtParser` / `ScreenBuffer` | VT 시퀀스 해석(커서, 지우기, SGR 색, OSC) → 셀 격자 |
| `TerminalSession` | ConPTY+파서+버퍼 묶음, Idle/Busy·cwd 상태, 셸 통합 스크립트(창 제목 `PD:n:경로` 마커, doc/02 3절), 보류 이동 |

## 3. 컨트롤 적용 계획 (WpfControlSamples 참조)

| 용도 | 사용 컨트롤 | 출처 |
|---|---|---|
| 창 | `ui:FluentWindow` + `ui:TitleBar` | wpfui |
| 즐겨찾기 버튼 | `ui:Button`(Icon + 짧은 이름), 컨텍스트 메뉴 | wpfui |
| 동기화 모드 | `ComboBox` (wpfui 스타일 자동 적용) | wpfui |
| 알림 | `ui:InfoBar` (경로 없음/권한 없음/대기 중) | wpfui |
| 드라이브 패널 | `ui:Button` 카드 + `ProgressBar`(사용량, 90% 이상 경고색), 현재 드라이브 강조 | wpfui |
| 파일 목록 | `ListView` + `GridView` (이름/수정일/크기) | wpfui 스타일 |
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

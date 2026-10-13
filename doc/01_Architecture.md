# 01. 아키텍처 및 MVVM 구조

> 상위: [기획서](../PathDock_개발개요.md) 4절을 구체화한다.

## 1. 계층

```
PathDock.App (WPF, net10.0-windows10.0.19041.0)
 ├─ Views/            MainWindow + 영역별 UserControl(FavoritesBar, NavButtons, SyncModeSelector, DrivePanel, AncestorColumn,
 │                    FileList, TerminalPanel, StatusBar), 설정/정보 창, 확인/입력 대화상자 (XAML, code-behind 최소)
 ├─ Controls/         커스텀 컨트롤: PanelCard, DriveCard, FavoriteChip, UsageMeter, AddressBar, TerminalControl(화면 버퍼 렌더링 + 키/마우스 입력)
 ├─ ViewModels/       MainViewModel(루트), ExplorerViewModel(+FileActions), TerminalViewModel, FileItemViewModel,
 │                    FavoriteSlotViewModel, DriveItemViewModel
 ├─ Services/         대화상자(IDialogService), 테마/글자 크기, 정보(AppInfo), 계산기 실행, 휴지통, 이미지/텍스트 미리보기와 캐시
 ├─ Localization/     {loc:T} 마크업 확장 + LocSource (XAML 문구의 한국어/영어 선택)
 └─ Themes/           Generic.xaml (커스텀 컨트롤 템플릿)
PathDock.Core (net10.0-windows, UI 비의존 → 단위 테스트 용이)
 ├─ Paths/            PathNormalizer
 ├─ Settings/         AppSettings, SettingsService (JSON)
 ├─ Explorer/         ExplorerService(목록 열거, 이력), FileOperations, FileSorter, FileCategory, DriveService,
 │                    FolderSizeCalculator/Cache, SizeFormatter, ImagePreviewTypes, TextPreview(TextPreviewer)
 ├─ Sync/             SyncMode, PathSyncService (중재, 루프 방지, 대기 정책)
 ├─ Terminal/         ConPtySession, VtParser, ScreenBuffer, TerminalSession(셸 통합·상태), TerminalShell, TerminalColorScheme(색 테마)
 ├─ Monitoring/       SystemMonitor (CPU/GPU/메모리)
 ├─ Localization/     Loc (한국어/English 문구 선택, 언어 변경 이벤트)
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
| `FileOperations` | 이름 검증/변경, 새 폴더, 복사·이동(이름 충돌은 "- 복사"), 휴지통 삭제. 오류 문구는 `Loc`로 언어를 따른다 |
| `ImagePreviewTypes` / `TextPreviewer` | 이미지·PDF 확장자 판정, `LaunchPolicy`(어떤 파일을 일반 권한으로 열지), 텍스트 파일 앞부분 읽기(64KB, 20줄, 바이너리·인코딩 판별). 순수 함수 |
| `Loc` | 현재 언어(한국어/English)와 `Loc.T(ko, en)`, 언어 변경 이벤트. 개발자용 로그는 번역 대상이 아님 |
| `SystemMonitor` | CPU/메모리/GPU 사용률 주기 측정(Timer 스레드, 이벤트) |
| `FolderSizeCalculator` | 폴더 재귀 크기/파일·폴더 수 집계(취소 가능, 링크 미추적, 접근 불가는 Incomplete) |
| `DriveService` | 드라이브 목록과 용량(총/여유) 조회. 준비 안 된 드라이브는 용량 0으로 표시 |
| `ExplorerService` | 폴더 열거(폴더 우선 정렬), 접근 불가 처리, 뒤로/앞으로/위로 이력 |
| `SettingsService` | 즐겨찾기(최대 10), 동기화 모드, 창 위치/크기, 분할 높이 JSON 저장/복원 |
| `PathSyncService` | 모드 적용, 출처(Origin) 구분, 동일 경로 무시, 터미널 Busy 시 보류 |
| `ConPtySession` | `CreatePseudoConsole` + 셸(PowerShell 또는 cmd) 프로세스, 입출력 스트림, 크기 조절 |
| `VtParser` / `ScreenBuffer` | VT 시퀀스 해석(커서, 지우기, SGR 색, OSC) → 셀 격자 |
| `TerminalSession` | ConPTY+파서+버퍼 묶음, Idle/Busy·cwd 상태, 셸 통합(PowerShell/cmd, 창 제목 `PD:토큰:…` 마커, doc/02 3절), 보류 이동, 화면 지우기 |

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

## 6. 다국어(한국어/English) 구조

- 언어 상태는 `PathDock.Core.Localization.Loc` 하나(`Current`, `LanguageChanged`). 코드의 문구는 `Loc.T("한국어", "English")`처럼 두 언어를 나란히 적는다
  (Core의 오류 문구와 App의 ViewModel 문구가 같은 방식). 로그 문구는 개발자용이라 번역하지 않는다.
- XAML은 `{loc:T '한국어', 'English'}` 마크업 확장(`PathDock.App.Localization.TExtension`)을 쓴다. 문구 쌍마다 번호를 매겨 `LocSource`(싱글톤,
  `INotifyPropertyChanged`)의 인덱서에 바인딩하므로, 언어가 바뀌면 모든 문구가 **앱 재시작 없이** 바뀐다.
- 코드에서 만든 문구는 언어가 바뀔 때 ViewModel이 다시 읽게 한다: `MainViewModel.OnLanguageChanged`가 `PropertyChanged("")`와
  각 하위 ViewModel(`Explorer`/`Terminal`/드라이브/즐겨찾기/파일 항목)의 `RefreshLanguage`를 호출한다. 콤보박스 항목(동기화 모드, 터미널 테마)은
  항목 객체를 유지한 채 이름만 갱신해 선택이 풀리지 않게 했다(`SyncModeOption`, `TerminalThemeOption`).
- 저장/복원: `settings.json`의 `Language`(`ko`/`en`). 값이 없으면 OS 표시 언어(한국어면 한국어, 그 밖에는 영어). 시작 시 `App.OnStartup`이 창 생성 전에 적용한다.
- 문구를 추가할 때: 한국어/영어를 함께 적는다. 한국어 문구를 바꾸면 영어 쪽도 같이 바꾼다(단위 테스트가 기본 언어를 한국어로 가정하므로 테스트가 한국어 문구를 직접 확인한다).

## 7. 파일 미리보기 툴팁 흐름

```
파일 이름 칸에 마우스(0.4초) ─▶ ToolTipOpening(FileListView)
   ├─ 이미지/텍스트가 아니면 e.Handled = true (툴팁 안 띄움)
   └─ FileItemViewModel.LoadPreviewAsync() ─▶ ImagePreviewService / TextPreviewService (스레드 풀, PreviewCache 40개)
        ├─ 이미지: 가로 240px로 줄여 디코딩(BitmapImage.DecodePixelWidth), 원본 해상도·크기를 함께 표시
        ├─ PDF: PdfPreviewService가 Windows.Data.Pdf로 첫 쪽을 가로 240px로 그림(흰 배경), 정보 줄에 쪽수·크기. 암호/손상 PDF는 생략
        └─ 텍스트: 앞 64KB만 읽어 20줄(120자 초과는 …), BOM 판별(UTF-8/16), NUL 바이트가 있으면 바이너리로 보고 생략
   읽기 실패/손상 파일은 툴팁을 닫고, 같은 항목은 다시 시도하지 않는다
```

판별과 읽기 로직(`ImagePreviewTypes`, `TextPreviewer`)은 Core의 순수 함수라 단위 테스트가 있고, 디코딩/캐시(`ImagePreviewService`, `PreviewCache<T>`)는 App에 있다.

## 8. 파일 열기와 권한

앱은 `requireAdministrator`로 실행되므로, 파일을 그대로 `ShellExecute`하면 연결 프로그램도 관리자 권한 프로세스가 된다. Adobe Acrobat/Reader 같은 일부 프로그램은 이 경우
**아무 반응 없이 열리지 않는다**(보호 모드/단일 인스턴스와 권한 불일치). 그래서 `ExplorerViewModel.OpenItem`은:

- `LaunchPolicy.OpensAsNormalUser(파일 이름)`이 true(문서·이미지 등)이고 앱이 관리자 권한이면 `UnelevatedLauncher`로 **탐색기(일반 권한)에게 ShellExecute를 맡긴다**
  (`ShellWindows.FindWindowSW`(데스크톱) → `Document.Application.ShellExecute`). 연결 프로그램이 평소 탐색기에서 더블클릭한 것과 똑같이 열린다.
- 실행 파일/스크립트(`.exe .com .bat .cmd .msi .msc .scr .cpl .ps1 .vbs .vbe .wsf .lnk`)는 기존처럼 앱의 권한으로 실행한다(관리자 권한이 필요한 작업을 위해).
- 탐색기와 통신하지 못하면 기존 방식(`Process.Start`)으로 되돌아간다.
- 확인: 임시 확장자 연결로 일반 권한에서 핸들러가 실행되는 것과(관리자 권한이 아님), 관리자 권한 `Start-Process`는 핸들러가 관리자 권한으로 실행되는 것을 확인했다. Adobe Reader 자체는 설치되어 있지 않아 직접 확인하지 못했다.
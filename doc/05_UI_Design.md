# 05. UI 구조와 커스텀 컨트롤

> 상위: [01_Architecture.md](./01_Architecture.md). 목표: 상용 프로그램 수준의 일관된 시각 언어와 재사용 가능한 컨트롤 구조.
> 참고: `WpfControlSamples/wpfui`(Fluent 스타일/리소스 키), `CodeAtlas_v0`(제목 표시줄 로고 → 정보 창).

## 1. 화면 구성

화면은 **색이 다른 세 개의 띠**와 상태 표시줄로 나뉜다(다크/라이트 공통).

| 띠 | 배경 | 내용 |
|---|---|---|
| 즐겨찾기 | 기본 배경 + 강조색 16% 덧칠(푸른 톤) | 번호 칩 10칸(왼쪽), 경로 동기화 모드(오른쪽) |
| 탐색기 | 기본 배경(`SolidBackgroundFillColorBase`) | 이동 버튼 + 주소 표시줄, 드라이브(**네이비 배경**, 6절) · 조부모 · 부모 · 현재 폴더 카드 |
| 터미널 | 보조 배경(`...Secondary`) | 터미널 카드 |
| 상태 표시줄 | 3차 배경 | 동기화 요약, CPU/GPU/메모리 미터 |

```
┌ TitleBar: [로고 PathDock ▸ 정보 창]                                  [테마] ─ □ × ┐
│ ① ② ③ … ⑩ 칩 …………………………………………………………………………………  경로 동기화 [양방향 ▾] │  즐겨찾기 띠
├────────────────────────────────────────────────────────────────────────────┤
│ [◀][▶][▲][⟳]  │ [ 주소 표시줄 ──────────────────────────────────────────── ]   │
│ ┌ 드라이브 ──┐ │ ┌ 조부모 ───┐ ┌ 부모 ────┐ ┌ 현재 폴더 ───────────────────┐ │  탐색기 띠
│ │ C: ▭▭▭     │ │ │ 폴더 열    │ │ 폴더 열   │ │ 이름 수정일 크기 하위 항목     │ │
│ └────────────┘ │ └───────────┘ └──────────┘ └───────────────────────────┘ │
├════════════════ 분할 막대 ═══════════════════════════════════════════════════┤
│ ┌ 터미널 - PowerShell ──────────────────────────────────── 셸 상태 ┐          │  터미널 띠
├────────────────────────────────────────────────────────────────────────────┤
│ 경로 동기화: 양방향                                   CPU ▭ GPU ▭ 메모리 ▭      │  상태 표시줄
└────────────────────────────────────────────────────────────────────────────┘
```

### 정렬 규칙 (탐색기 띠는 한 개의 격자를 공유)
- 이동 버튼 4개는 **드라이브 열 폭을 균등 분할**해 채운다(열 폭을 조절하면 버튼도 따라감).
- 주소 표시줄은 **조부모 폴더 열의 시작점**에서 시작해 오른쪽 끝까지 이어진다.
- 즐겨찾기 칩 왼쪽 끝과 카드 왼쪽 끝은 같은 여백(12px)을 쓴다.

### 모양 규칙
- **각진 모서리**: 카드/칩/드라이브 카드/주소 표시줄/목록 항목 모두 반경 2px. WPF-UI 컨트롤은 `App.xaml`에서 `ControlCornerRadius`/`OverlayCornerRadius`를 2로 덮어써 같이 각지게 했다. 창 모서리도 `DoNotRound`.
- **글자 크기**: 본문 12, 보조 11(미터/용량 문구), 주소 표시줄 13. 목록·콤보·머리글은 명시적으로 12로 맞췄다(WPF-UI 기본 14보다 한 단계 작게).
- **선명한 경계**: 카드 테두리는 `ControlStrongStrokeColorDefault`(강한 선), 카드 머리글은 색이 다른 띠 + 아래 구분선.
- **주소 표시줄**: 배경/테두리는 바깥 Border 하나만 그린다(안쪽 TextBox는 템플릿을 `ScrollViewer`로 줄여 이중 배경 제거).
- **즐겨찾기 배치**: 한 줄에 다 들어가면 한 줄, 아니면 5칸씩 두 줄. Tab은 즐겨찾기 줄을 한 번에 통과하고 방향키로 칸을 옮긴다.
- **알림 막대**: 탐색기 위에 겹쳐 표시(레이아웃이 밀리지 않음), 종류별 색, 성공은 5초 뒤 자동 닫힘.
- **화면 글자 크기/언어/고대비**: 글자 크기(`PdFontSize`, 설정 창 슬라이더)와 한국어/English는 즉시 반영, 고대비 모드는 시스템 색을 따른다. 글자가 커지면 최소 창 크기가 커진다.
- **미리보기 툴팁**: 이미지(0.4초 호버, 가로 240px 이하 + 해상도·크기)와 텍스트/소스 파일(앞 20줄)을 파일 목록에서 보여준다(`PreviewCache` 공용 캐시).

## 2. 커스텀 컨트롤 (`Controls/` + `Themes/Generic.xaml`)

템플릿 기반(`DefaultStyleKey` + ControlTemplate) 컨트롤. 모든 색은 WPF-UI 테마 리소스(`DynamicResource`)를 쓰므로 다크/라이트가 자동으로 따라온다.

| 컨트롤 | 기반 | 역할 |
|---|---|---|
| `PanelCard` | `HeaderedContentControl` | 아이콘+제목 머리글, 보조 콘텐츠(`HeaderTrailing`), 둥근 카드. 모든 영역의 공용 컨테이너 |
| `DriveCard` | `Button` | 드라이브 이름/사용량 막대/용량 문구, 현재 드라이브 강조(`IsCurrent`), 90% 이상 경고색(`IsLow`) |
| `FavoriteChip` | `Button` | 번호 배지 + 폴더 이름. 빈 칸(`IsEmpty`)은 "+ 등록" 모양 |
| `UsageMeter` | `Control` | "라벨 막대 값" 한 줄 미터. 임계값 이상이면 경고색, `IsAvailable=false`면 흐리게(GPU N/A) |
| `AddressBar` | `Control` | 아이콘 + 경로 입력, Enter로 명령 실행. UIA ID/이름은 안쪽 TextBox에 전달(`TextBoxAutomationId`) |
| `TerminalControl` | `FrameworkElement` | (기존) 화면 버퍼 렌더링/입력/UIA 값 노출 |

## 3. 뷰(UserControl, `Views/`)

각 뷰는 표시 전용이며 DataContext만 받는다(code-behind는 UI 이벤트 전달뿐).

| 뷰 | DataContext | 비고 |
|---|---|---|
| `FavoritesBarView` | MainViewModel.Favorites | 칩 줄(빈 칸은 "+ 등록"), 우클릭 메뉴(등록/비우기) |
| `NavButtonsView` | ExplorerViewModel | 이동 버튼 4개(놓인 열 폭을 채움, 높이 38·아이콘 22, 주소 표시줄과 같은 배경/테두리색) |
| `SyncModeSelectorView` | MainViewModel | 경로 동기화 콤보(즐겨찾기 띠 오른쪽) |
| `DrivePanelView` | MainViewModel.Drives | `DriveCard` 목록, 패널 배경은 `PdDrivePanelBackgroundBrush`(네이비) |
| `AncestorColumnView` | AncestorColumnViewModel | 조부모/부모 열이 **같은 뷰를 재사용**, 선택 항목 왼쪽 강조 막대 |
| `FileListView` | ExplorerViewModel | 종류별 아이콘/색, 정렬 머리글(▲▼), 이름 검색 입력칸(Ctrl+F), 요약 줄, 미리보기 툴팁, 단축키·우클릭 메뉴(이름 변경/삭제/새 폴더/복사/잘라내기/붙여넣기) |
| `TerminalPanelView` | TerminalViewModel | 셸 상태, 도움말·내용 저장·화면 지우기·접기 버튼을 머리글에 표시. 본문 위쪽에 **탭 줄**(탭 버튼 + 닫기 `×`, 오른쪽 `+`; 선택된 탭은 강조색 밑줄, 탭이 많으면 가로 스크롤). 탭 버튼은 포커스를 가져가지 않고 클릭 후 포커스를 터미널로 돌려준다 |
| `StatusBarView` | MainViewModel(SyncSummary) + Usage | 동기화 요약 + `UsageMeter` 3개 |
| `SettingsWindow` | SettingsViewModel(MainViewModel.Preferences) | 언어, 글자 크기(화면/터미널), 터미널 셸·테마·글자색/배경색(미리보기), 시작 시 마지막 경로, 즐겨찾기 초기화 |
| `AboutWindow` | (AppInfo) | 정보 창 |
| `InputDialog` / `ConfirmDialog` | - | 이름 변경/새 폴더 입력 / 자체 확인 창(삭제, 여러 줄 붙여넣기, 휴지통 비우기 등) |

제목 표시줄(`MainWindow`): 로고(정보 창) + 버전·빌드 날짜, 시계, 계산기(Ctrl+Alt+C, 이미 떠 있으면 앞으로), 휴지통 비우기(Ctrl+Alt+E, 항목 수/용량 확인 후), 설정, 테마 전환.

## 4. 정보 창 (`AboutWindow`)

- 제목 표시줄 왼쪽 **로고 버튼**(`AboutButton`)을 누르면 모달로 열린다(CodeAtlas와 동일한 진입점).
- 제품명/버전(어셈블리 `Version`), 한 줄 소개, 제작/문의(메일 링크)/사용 기술/데이터 폴더/로그 폴더, 빌드 시각·.NET·64비트·OS(레지스트리에서 읽음).
- **기능과 단축키** 카드(`AboutShortcuts`): 기능 한 줄 요약 + 단축키 표(파일 목록/제목 표시줄/터미널). 문구는 `AboutWindow.xaml.cs`의 `BuildShortcutRows()`에 있으며 언어 전환에 맞춰 창을 열 때마다 만든다. 단축키를 바꾸면 여기와 README를 함께 고친다.
- 내용이 길어 창 높이는 작업 영역 높이로 제한하고 안쪽을 스크롤한다(작은 화면 대비).
- **정보 복사**(문의용 요약을 클립보드로), **로그 폴더 열기**, 닫기. 표시 데이터는 `Services/AppInfo`.

## 4.1 시작 로딩 팝업 (`SplashHost`)

- 작고 테두리 없는 최상단 창(340x170 @96DPI): 앱 로고 + "PathDock" + "로딩 중..."/"Loading..."(설정 언어) + **"지금 하는 일" 한 줄** + 진행 막대. 테마/설정과 무관하게 항상 같은 모양(네이비 `#1B2433`, 색을 코드에서 직접 지정). 작업 표시줄에 나타나지 않고 포커스를 가져가지 않는다.
- **WPF 창이 아니라 순수 Win32 창**이다(`Services/SplashHost.cs`: 전용 스레드 + GDI + 타이머 16ms, 앱 아이콘은 실행 파일의 아이콘 리소스). 이유: 메인 창을 만드는 동안 WPF UI 스레드가 몇 초 막히므로, 진행 막대와 문구가 그동안에도 계속 갱신되려면 WPF 렌더링과 무관해야 한다.
  (WPF 창을 같은 스레드/별도 UI 스레드에 두는 방식은 막대가 멈추는 것으로 보여 이 방식으로 갈아탔다. 단 그 판정 중 일부는 DPI 측정 오류가 섞여 있어 WPF 방식 자체의 한계는 정확히 검증하지 못했다 — doc/03 4절 9번.)
- **진행 막대**: 밝은 조각이 1.2초 주기로 왼쪽 밖에서 오른쪽 밖까지 계속 흐른다. 타이머가 막대 영역만 다시 그리게 하고, 그릴 때 메모리 DC에 그려 한 번에 복사한다(깜빡임 방지).
- **"지금 하는 일" 문구**(`SetStatus`, 아무 스레드에서 호출 가능): `테마와 글꼴 적용 중...` → `폴더 크기 캐시 불러오는 중...` → `탐색기와 터미널 준비 중...` → `화면 구성 중...` → `폴더 목록 읽는 중...` → `터미널 시작 중...` → `준비 완료`. 호출 지점은 `App.OnStartup`, `MainViewModel` 생성자/`Initialize`의 `progress` 콜백. 단계가 너무 빨리 지나가면 일부 문구는 눈에 안 띌 수 있다.
- **흐름**(`App.OnStartup`): 설정을 읽어 언어를 정한 직후 팝업 시작(`Start`: 화면에 그려질 때까지 최대 5초 대기, 실패해도 앱 시작은 계속) → 위 단계 진행 → 메인 창 `ContentRendered`에서 "준비 완료"를 보이고 **1초 뒤** 닫는다(`SplashAfterMainShown`). 그 사이 메인 창이 닫히면(또는 앱 종료) 팝업도 함께 닫는다. 팝업을 못 띄운 이유는 `FailureReason`으로 로그에 남긴다.
- **한계**: 팝업은 관리 코드가 실행된 뒤에야 뜬다. 실측(Debug) 프로세스 시작 후 `OnStartup`까지 약 1.6초, 팝업 표시 약 1.8~2.0초, 메인 창 표시 완료 약 4.7초. 앞의 약 2초(런타임/WPF 리소스 로딩)는 덮지 못한다. 시작 단계별 시간은 로그의 "시작 시간:" 줄에 남는다.
- 창 제목은 "PathDock Loading"(고정, 번역 안 함) — UI 테스트가 메인 창("PathDock")과 구분하는 데 쓴다. 자식 STATIC 컨트롤 ID(UI Automation의 AutomationId): 제목 `1001`, "로딩 중" `1002`, "지금 하는 일" `1003`.

## 5. 아이콘

- 앱 아이콘: 파란 그라데이션 둥근 사각형 위에 폴더 윤곽 + `>_`. `tools/make_icon.ps1`이 `Assets/PathDock.ico`(16~256px PNG 내장)와 `PathDock.png`를 생성한다(산출물은 저장소에 포함, 재생성은 필요할 때만).
- 적용: 실행 파일(`ApplicationIcon`), 창/작업 표시줄(`Window.Icon`), 제목 표시줄 로고, 정보 창.
- UI 아이콘: WPF-UI `SymbolIcon`(Fluent). **주의**: 코드포인트가 0xFFFF를 넘는 심볼(예: `HardDrive24`)은 글리프가 깨져 보였다 → BMP 범위 심볼(`HardDrive20`)을 쓴다.
- 파일 목록 아이콘: `Core/FileCategorizer`(확장자 → 폴더/코드/문서/데이터/이미지/오디오/비디오/압축/실행/기타)와 앱의 아이콘·색 매핑(`FileItemViewModel.IconSymbol/IconBrush`).

## 6. 테마

- 다크(기본)/라이트. 제목 표시줄 오른쪽 버튼으로 전환, 선택은 `settings.json`의 `Theme`에 저장되어 다음 실행에 복원(`ThemeService`, 창 생성 전에 적용).
- 터미널 화면 색은 UI 테마와 별개로 터미널 테마 5종/사용자 지정 색으로 정한다(기본은 어두운 배경).
- 주소 표시줄(슬레이트 블루 `#1E2F45`/`#E6EFFA`)과 드라이브 패널(네이비 `#1B2433`/`#E8EEF7`)은 주변 카드와 구분되도록 테마별 고정색을 쓴다.
  `App.xaml`에 다크 기본값을 두고 `ThemeService`가 테마 전환·고대비 때 `PdAddressBar*`, `PdDrivePanelBackgroundBrush`를 갱신한다(고대비는 시스템 색).
- 밝은 테마에서는 보조 글자색(`TextFillColorSecondary/Tertiary`)을 대비 4.5:1 이상으로 덮어쓴다.

## 7. 접근성/자동화 식별자

기존 ID 유지(주소 표시줄 `PathBar`는 안쪽 TextBox가 가짐): `FavoritesBar`, `Favorite_1..10`, `PathBar`, `SyncModeCombo`, `FileList`, `Terminal`, `TerminalStatus`, `FolderSummary`, `Drive_<문자>`, `AncestorColumn_1/2`, `InfoBar`.
신규: `AboutButton`, `ThemeButton`, `Meter_Cpu/Gpu/Memory`(이름: "CPU 12%" 등), `SyncSummary`, 정보 창(`AboutVersionTextBlock`, `AboutEnvironmentTextBlock`, `AboutCopyButton`, `AboutLogsButton`, `AboutCloseButton`).
이후 추가: 제목 표시줄 `CalculatorButton`, `EmptyRecycleBinButton`, `SettingsButton`, `ClockText`, `VersionLabel` / 탐색 `BackButton`, `ForwardButton`, `UpButton`, `RefreshButton`, `FilterBox`, `ImagePreview`, `TextPreview` /
터미널 `TerminalClearButton`, `TerminalSaveButton`, `TerminalCollapseButton`, `TerminalHelp`, 탭 줄 `TerminalTabAdd`, `TerminalTab_<번호>`, `TerminalTabClose_<번호>`(번호는 1부터, 닫아도 재사용하지 않음; 탭이 하나면 닫기 버튼은 숨김=UIA 트리에 없음) / 설정 창(`LanguageCombo`, `UiFontSizeSlider`, `TerminalFontSizeSlider`, `TerminalShellCombo`, `TerminalThemeCombo`,
`TerminalForegroundBox`, `TerminalBackgroundBox`, `TerminalColorError`, `StartAtLastPathCheck`, `Reset*Button`, `SettingsCloseButton`) / 대화상자(`InputBox`, `InputOk`, `InputCancel`, `InputError`, `ConfirmMessage`, `ConfirmYes`, `ConfirmNo`).

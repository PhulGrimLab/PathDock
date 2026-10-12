# 05. UI 구조와 커스텀 컨트롤

> 상위: [01_Architecture.md](./01_Architecture.md). 목표: 상용 프로그램 수준의 일관된 시각 언어와 재사용 가능한 컨트롤 구조.
> 참고: `WpfControlSamples/wpfui`(Fluent 스타일/리소스 키), `CodeAtlas_v0`(제목 표시줄 로고 → 정보 창).

## 1. 화면 구성

화면은 **색이 다른 세 개의 띠**와 상태 표시줄로 나뉜다(다크/라이트 공통).

| 띠 | 배경 | 내용 |
|---|---|---|
| 즐겨찾기 | 기본 배경 + 강조색 16% 덧칠(푸른 톤) | 번호 칩 10칸(왼쪽), 경로 동기화 모드(오른쪽) |
| 탐색기 | 기본 배경(`SolidBackgroundFillColorBase`) | 이동 버튼 + 주소 표시줄, 드라이브 · 조부모 · 부모 · 현재 폴더 카드 |
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
| `FavoritesBarView` | MainViewModel | 칩 줄(빈 칸은 "+ 등록"), 우클릭 메뉴(등록/비우기) |
| `NavButtonsView` | ExplorerViewModel | 이동 버튼 4개(놓인 열 폭을 채움) |
| `SyncModeSelectorView` | MainViewModel | 경로 동기화 콤보(즐겨찾기 띠 오른쪽) |
| `DrivePanelView` | MainViewModel | `DriveCard` 목록 |
| `AncestorColumnView` | AncestorColumnViewModel | 조부모/부모 열이 **같은 뷰를 재사용**, 선택 항목 왼쪽 강조 막대 |
| `FileListView` | ExplorerViewModel | 종류별 아이콘/색, 정렬 머리글(▲▼), 요약 줄, 단축키·우클릭 메뉴 |
| `TerminalPanelView` | TerminalViewModel | 셸 상태를 머리글에 표시 |
| `StatusBarView` | MainViewModel | 동기화 요약 + `UsageMeter` 3개 |
| `AboutWindow` | (AppInfo) | 정보 창 |
| `InputDialog` | - | 이름 변경/새 폴더 입력 |

## 4. 정보 창 (`AboutWindow`)

- 제목 표시줄 왼쪽 **로고 버튼**(`AboutButton`)을 누르면 모달로 열린다(CodeAtlas와 동일한 진입점).
- 제품명/버전(어셈블리 `Version`), 한 줄 소개, 제작/문의(메일 링크)/사용 기술/데이터 폴더/로그 폴더, 빌드 시각·.NET·64비트·OS(레지스트리에서 읽음).
- **정보 복사**(문의용 요약을 클립보드로), **로그 폴더 열기**, 닫기. 표시 데이터는 `Services/AppInfo`.

## 5. 아이콘

- 앱 아이콘: 파란 그라데이션 둥근 사각형 위에 폴더 윤곽 + `>_`. `tools/make_icon.ps1`이 `Assets/PathDock.ico`(16~256px PNG 내장)와 `PathDock.png`를 생성한다(산출물은 저장소에 포함, 재생성은 필요할 때만).
- 적용: 실행 파일(`ApplicationIcon`), 창/작업 표시줄(`Window.Icon`), 제목 표시줄 로고, 정보 창.
- UI 아이콘: WPF-UI `SymbolIcon`(Fluent). **주의**: 코드포인트가 0xFFFF를 넘는 심볼(예: `HardDrive24`)은 글리프가 깨져 보였다 → BMP 범위 심볼(`HardDrive20`)을 쓴다.
- 파일 목록 아이콘: `Core/FileCategorizer`(확장자 → 폴더/코드/문서/데이터/이미지/오디오/비디오/압축/실행/기타)와 앱의 아이콘·색 매핑(`FileItemViewModel.IconSymbol/IconBrush`).

## 6. 테마

- 다크(기본)/라이트. 제목 표시줄 오른쪽 버튼으로 전환, 선택은 `settings.json`의 `Theme`에 저장되어 다음 실행에 복원(`ThemeService`, 창 생성 전에 적용).
- 터미널 화면은 셸 색상 체계 보존을 위해 두 테마 모두 어둡게 유지한다.

## 7. 접근성/자동화 식별자

기존 ID 유지(주소 표시줄 `PathBar`는 안쪽 TextBox가 가짐): `FavoritesBar`, `Favorite_1..10`, `PathBar`, `SyncModeCombo`, `FileList`, `Terminal`, `TerminalStatus`, `FolderSummary`, `Drive_<문자>`, `AncestorColumn_1/2`, `InfoBar`.
신규: `AboutButton`, `ThemeButton`, `Meter_Cpu/Gpu/Memory`(이름: "CPU 12%" 등), `SyncSummary`, 정보 창(`AboutVersionTextBlock`, `AboutEnvironmentTextBlock`, `AboutCopyButton`, `AboutLogsButton`, `AboutCloseButton`).

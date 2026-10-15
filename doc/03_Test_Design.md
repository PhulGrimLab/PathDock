# 03. 자동 테스트 설계

> CodeAtlas의 `doc/10_UiAutomation_Test_Design.md` 방식을 따른다 (로직=MSTest, 화면=자체 하네스).

## 1. PathDock.Tests (MSTest, net10.0-windows)

현재 **108개**(2026-10-15 전부 통과). 파일: `CoreLogicTests.cs`(핵심 로직 다수), `TerminalSessionTests.cs`(ConPTY 통합), `TerminalThemeTests.cs`,
`LocTests.cs`(언어), `TextPreviewerTests.cs`, `ImagePreviewTypesTests.cs`.

| 영역 | 주요 케이스 |
|---|---|
| 파일 조작 | 이름 검증/변경(대소문자만 변경 포함), 새 폴더 이름 제안, 복사/이동 이름 충돌("- 복사"), 휴지통 삭제 |
| 터미널 테마/언어/미리보기 | 테마 조회·사용자 색 덮어쓰기·잘못된 색 문자열, `Loc.T`/`Plural`/OS 언어 판정, 텍스트 미리보기(인코딩·바이너리 생략), 이미지 형식 판별 |
| 터미널 통합(추가) | 명령 프롬프트(cmd)에서 cwd 보고·요청/사용자 이동 추적, 토큰이 있는 셸 마커만 인정(위조 마커 무시), 로그의 제어 문자 제거, 스크롤백 포함 전체 텍스트 |
| PathNormalizer | 후행 `\`, 드라이브 대소문자, 공백/한글, UNC, 상대경로, `..` |
| ExplorerService | 폴더 우선 정렬, 접근 불가 폴더, 뒤로/앞으로/위로, 존재하지 않는 경로 |
| SettingsService | 저장/복원, 손상된 JSON → 기본값, 즐겨찾기 10개 초과 거부 |
| PathSyncService | 4개 모드 × 2방향 표, Origin.Sync 무시, 동일 경로 무시, Busy 보류→Idle 반영, 경로 없음 경고 |
| VtParser/ScreenBuffer | 텍스트/개행/CR, 커서 이동, 지우기, SGR, 스크롤, OSC 9;9·133 파싱, 분할된 바이트열(UTF-8·ESC 시퀀스) |
| ConPtySession(통합) | PowerShell 기동 → 프롬프트 수신, `Set-Location` 주입 후 cwd 마커 수신, 공백/작은따옴표 경로 |
| ViewModel | Favorites 클릭 → Explorer 이동, 모드 변경 반영 |

## 2. PathDock.UiAutomation.Tests (콘솔 하네스)

실제 `PathDock.App.exe`를 `PATHDOCK_DATA_DIR`(임시 폴더)로 격리해 띄우고 `System.Windows.Automation`으로 조작한다.
CodeAtlas에서 얻은 교훈 적용: `Invoke` 스퓨리어스 예외는 삼키고 상태 변화로 판정, 새 창/대화상자는 `EnumWindows`로 탐색.

| 테스트 (구현됨, 19개) | 검증 |
|---|---|
| Favorites_Click_NavigatesExplorer | 즐겨찾기 1·2 클릭 → 경로 표시줄이 해당 폴더로 |
| Drives_ShowSystemDriveUsage_AndClickNavigatesToRoot | 드라이브 패널에 시스템 드라이브가 표시되고 클릭하면 루트로 이동 |
| Favorites_EmptySlotClick_RegistersCurrentFolder | 빈 칸 클릭 → 현재 폴더 등록(이름에 `_` 보존 — AccessText 회귀) |
| Explorer_OpenFile_LaunchesAssociatedProgram_InItsFolder | 스크립트를 Enter로 열면 실행되고 작업 폴더가 그 파일의 폴더 |
| StatusBar_ShowsCpuGpuMemoryMeters | 하단 CPU/GPU/메모리 미터의 이름이 "CPU n%" 등으로 갱신 |
| FolderSizeCache_IsPersistedOnClose | 종료 시 캐시 파일 저장, 재시작 후에도 폴더 크기 표시 |
| Explorer_SortByHeader_Size_TogglesDirection | 크기 머리글 클릭 → 오름/내림, 이름 머리글 → 이름순 |
| Explorer_Rename_F2_RenamesAndSelects | F2 → 입력창 → 디스크 이름 변경 + 목록 반영 |
| Explorer_NewFolder_CtrlShiftN_CreatesFolder | Ctrl+Shift+N → 입력창 → 폴더 생성 |
| Explorer_Delete_AsksConfirm_NoKeeps_YesRecycles | Delete → 확인 창에서 아니오=유지, 예=삭제 |
| About_LogoClick_ShowsInfoWindow_AndCloses | 제목 표시줄 로고 → 정보 창(버전/환경 문구) → 닫기 |
| Theme_Toggle_SwitchesAndPersists | 테마 버튼 → 종료 시 설정에 Light 저장 |
| Explorer_ShowsAncestorColumns_AndFolderSizes | 부모/조부모 열 내용, 폴더 크기(3 KB)·요약 줄, 상위 열 클릭 이동 |
| Explorer_OpenFolderWithEnter_UpdatesPath | 목록에서 폴더 선택 + Enter → 하위 폴더 진입 |
| PathBar_InvalidPath_ShowsInfoBar_AndKeepsPath | 존재하지 않는 경로 입력 → 주소창이 입력값을 유지(고쳐 쓸 수 있도록. 4553801 이전에는 원래 경로로 복원했음) |
| SyncMode_ExplorerToTerminal_ChangesTerminalPrompt | 양방향에서 즐겨찾기 이동 → 터미널 프롬프트가 새 폴더로 |
| SyncMode_TerminalToExplorer_UpdatesPathBar | 터미널에 `cd` 입력 → 경로 표시줄 갱신 |
| SyncMode_Off_DoesNotPropagate | 동기화 끄기 후 탐색기 이동이 터미널에 전파되지 않음 |
| Settings_SyncMode_PersistsAcrossRestart | 모드 변경 → 정상 종료 → 재시작 시 복원 |

하네스 보조: `--dump [AutomationId]`로 UIA 트리(이름/종류/Id)를 출력해 요소를 진단한다. `SendKeys` 전에
`Native.BringToFront`로 앱을 포그라운드로 만든다(안 하면 간헐 실패 — doc/04 3절). 목록은 UIA에서 `DataGrid/DataItem`으로 노출된다.

앱은 UIA 식별을 위해 주요 요소에 `AutomationProperties.AutomationId`를 지정한다
(`PathBar`, `SyncModeCombo`, `Favorite_1..10`, `FileList`, `Terminal` 등, 전체 목록은 doc/05 7절).

**아직 UI 테스트가 없는 기능**(MSTest 또는 수동 확인만): 터미널 선택/복사, 셸 선택(cmd), 설정 창, 언어 전환, 이름 검색, 미리보기 툴팁, 복사/이동/붙여넣기, 계산기/휴지통 버튼.
터미널 텍스트는 `TerminalControl`이 UIA `ValuePattern`(읽기 전용 화면 텍스트)으로 노출한다.

## 3. 실행

`doc/run_tests.ps1`, `doc/run_ui_tests.ps1` (doc/00 3절). UI 테스트는 화면을 실제로 조작하므로 실행 중 입력 금지.

## 4. 하네스에서 겪은 문제 (2026-10-13)

1. **소유 창은 UIA에서 메인 창의 자식으로 보인다.** 모달 입력창/메시지 상자는 데스크톱 직속이 아니라 `app.Window`의 Children에서 찾는다(`FindDialog`).
2. **`Launch` 실패 시 프로세스 누수.** 창 대기 중 예외가 나면 호출 측 `using`이 아직 소유하기 전이라 앱이 남는다 → 시작 후 예외는 `Launch` 안에서 `Kill`한다. (실제로 실패 테스트마다 앱이 1개씩 쌓여 PC에 8개가 떠 있었다.)
3. **메인 창은 `Process.MainWindowHandle` → `AutomationElement.FromHandle`로 얻는다.** 데스크톱 루트를 UIA로 훑는 방식은 최상위 창이 많을 때 느려져(테스트당 10~17초, 20초 대기 초과) 간헐 실패했다. 변경 후 테스트당 약 3초.
4. 시작 직후 포커스는 터미널에 있으므로, 탐색기 단축키 테스트는 목록 요소에 먼저 `SetFocus` 한다.
5. 진단 도구: `--dump [AutomationId]`(UIA 트리), `--probe-f2 [키]`(키 입력 후 최상위/자식 창과 앱 로그 출력), `--shot-about 경로.png`(정보 창만 캡처).
6. **출력 경로 변경에 주의 (2026-10-15)**: 빌드 출력이 `bin\x64\Debug`인데 `run_ui_tests.ps1`과 `AppDriver.AppExePath`가 `bin\Debug`를 가리켜 19개가 모두 실패했다. 경로 규칙을 바꾸면 두 곳을 함께 고친다.
7. **UI 테스트 기대값은 동작 변경 때 같이 갱신한다.** 주소창 오류 처리(입력값 유지)를 바꾼 뒤 테스트가 옛 동작을 기대해 실패했다.

# 03. 자동 테스트 설계

> CodeAtlas의 `doc/10_UiAutomation_Test_Design.md` 방식을 따른다 (로직=MSTest, 화면=자체 하네스).

## 1. PathDock.Tests (MSTest, net10.0-windows)

현재 **206개**(2026-10-16 전부 통과, Core 줄 커버리지 91.8% / 분기 88.0%). 파일: `CoreLogicTests.cs`(핵심 로직 다수), `TerminalSessionTests.cs`(ConPTY 통합), `TerminalTabTitleTests.cs`(탭 제목 규칙), `TerminalSecurityTests.cs`·`HardeningTests.cs`(보안 회귀), `TerminalParserTests.cs`(VT 파서·화면 버퍼), `SettingsRobustnessTests.cs`(손상된 설정), `TerminalThemeTests.cs`,
`LocTests.cs`(언어), `TextPreviewerTests.cs`, `ImagePreviewTypesTests.cs`.

| 영역 | 주요 케이스 |
|---|---|
| 파일 조작 | 이름 검증/변경(대소문자만 변경 포함), 새 폴더 이름 제안, 복사/이동 이름 충돌("- 복사"), 휴지통 삭제 |
| 터미널 테마/언어/미리보기 | 테마 조회·사용자 색 덮어쓰기·잘못된 색 문자열, `Loc.T`/`Plural`/OS 언어 판정, 텍스트 미리보기(인코딩·바이너리 생략), 이미지 형식 판별 |
| 터미널 탭 제목 | 셸 이름만(경로 모름), 마지막 폴더 이름(후행 `\` 유무), 한글 경로, 드라이브 루트 `C:`, UNC 공유 이름 |
| 터미널 통합(추가) | 명령 프롬프트(cmd)에서 cwd 보고·요청/사용자 이동 추적, 토큰이 있는 셸 마커만 인정(위조 마커 무시), 로그의 제어 문자 제거, 스크롤백 포함 전체 텍스트 |
| 보안 회귀(TerminalSecurityTests) | 토큰 세션이 OSC 9;9/133 위조(UNC 경로·프롬프트)를 무시(파서 + 실제 셸), CSI 파라미터 32개 상한, 큰 스크롤 횟수 상한(시간), 붙여넣기 확인(끝 줄바꿈·제어 문자 제거·미리보기), 셸 통합이 한 줄 ASCII 명령이고 임시 스크립트 파일을 만들지 않음, cmd가 현재 폴더의 실행 파일을 먼저 실행하지 않음(환경 변수는 테스트 안에서 지워 환경과 무관) |
| 보안 회귀 2(HardeningTests) | 링크(정션/심볼릭 링크) 폴더·파일을 따라가지 않는 설정/캐시/로그 쓰기, 느린 UNC 경로 확인이 시간 제한 안에 돌아옴(실제로 응답 없는 주소 사용), 링크로 자기 안 복사 거부(실제 경로 비교)와 건너뛴 링크 알림, 예약 장치 이름(CON/NUL/COM1/con.txt 등) 거부와 비슷한 이름 허용, 이전 버전 임시 스크립트 정리(오래된 것만) |
| VT 파서·화면 버퍼(TerminalParserTests) | 커서 이동/저장·복원/탭/백스페이스, 역방향 이동, 화면·줄 지우기 모드, 글자·줄 삽입/삭제, 스크롤 영역과 스크롤백(2000줄 상한), 대체 화면 저장·복원, **크기 변경**(줄 줄이기 시 스크롤백으로 이동, 넓히기·좁히기, 0 크기, 대체 화면 중 변경), SGR(16/256/트루컬러/속성/잘못된 형식), 장치 질의 응답, 모드, DCS/APC/OSC(OSC 52 무시, 길이 상한), 셸 마커, 바이트 단위 입력 동일성, 퍼징(300 시드, 임의 시퀀스+크기 변경에도 예외 없음) |
| 손상된 설정(SettingsRobustnessTests) | `Favorites: null`/`[null]`/null 문자열/터무니없는 숫자/NaN이 있어도 시작이 막히지 않음(null은 기본값, 숫자는 범위 안으로), 정상 설정은 그대로 |
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

| 테스트 (구현됨, 33개) | 검증 |
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
| Startup_ShowsLoadingPopup_AndClosesAboutOneSecondAfterMainWindow | 시작하면 로딩 팝업("PathDock Loading")이 먼저 뜨고, 메인 창이 뜬 시점에도 아직 보이며, 메인 창 표시 후 0.7~5초 사이에 닫히고 메인 창은 남음 |
| Startup_LoadingPopup_BarKeepsMovingAndStatusLineChanges | 팝업이 떠 있는 동안(메인 창이 뜨기 전 초기화 구간) 실제 화면의 진행 막대가 계속 다른 그림이고(3종 이상), "지금 하는 일" 문구가 단계별로 3번 이상 바뀌며 마지막이 "준비 완료". 문구는 UI Automation으로 읽는다(Win32 STATIC `1003`). 막대 타이머를 느리게 하는 결함 주입에서 실패함을 확인 |
| Exit_WithRunningCommand_AsksConfirm_NoKeepsRunning_YesExits | 명령이 실행 중일 때 닫기를 요청하면 확인 창: 아니오=앱 유지, 예=종료 |
| Tabs_AddAndClose_LastTabCannotBeClosed | 탭 1개일 때 닫기 버튼 없음 → `+`로 2번째 탭 생성(셸 시작, 닫기 버튼 표시) → 닫으면 다시 1개 |
| Tabs_AreIndependent_AndSelectingTabFollowsInExplorer | 새 탭은 탐색기 폴더에서 시작, 둘째 탭 `cd`는 탐색기가 따라감, 첫 탭 선택 시 탐색기가 첫 탭 폴더로 복귀하고 첫 탭 화면에 둘째 탭 프롬프트가 없음, 탭 제목에 폴더 이름 |
| Tabs_KeyboardShortcuts_NewSwitchClose | Ctrl+Shift+T 새 탭, Ctrl+Tab / Ctrl+Shift+Tab 전환(처음↔끝 순환), Ctrl+Shift+W 닫기(마지막 탭은 유지) |
| Tabs_CloseBusyTab_AsksConfirm | 명령 실행 중인 탭을 닫으면 확인 창: 아니오=유지, 예=닫힘 |
| Settings_SyncMode_PersistsAcrossRestart | 모드 변경 → 정상 종료 → 재시작 시 복원 |

하네스 보조: `--dump [AutomationId]`로 UIA 트리(이름/종류/Id)를 출력해 요소를 진단한다. `SendKeys` 전에
`Native.BringToFront`로 앱을 포그라운드로 만든다(안 하면 간헐 실패 — doc/04 3절). 목록은 UIA에서 `DataGrid/DataItem`으로 노출된다.

앱은 UIA 식별을 위해 주요 요소에 `AutomationProperties.AutomationId`를 지정한다
(`PathBar`, `SyncModeCombo`, `Favorite_1..10`, `FileList`, `Terminal` 등, 전체 목록은 doc/05 7절).

**아직 UI 테스트가 없는 기능**(MSTest 또는 수동 확인만): 터미널 선택/복사, 셸 선택(cmd)과 셸 변경 시 선택된 탭 재시작, 최대 탭 수(8개) 제한 안내, 설정 창, 언어 전환, 이름 검색, 미리보기 툴팁, 복사/이동/붙여넣기, 계산기/휴지통 버튼.
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
8. **메인 창은 제목("PathDock")으로 찾는다 (2026-10-16).** 시작 중 로딩 팝업이 같은 프로세스의 먼저 뜬 최상위 창이라 `Process.MainWindowHandle`이 팝업을 가리킬 수 있다 → `Native.FindWindowByTitle`(EnumWindows)로 제목이 정확히 일치하는 창을 찾는다. 팝업 제목은 `AppDriver.SplashWindowTitle`("PathDock Loading").
7. **UI 테스트 기대값은 동작 변경 때 같이 갱신한다.** 주소창 오류 처리(입력값 유지)를 바꾼 뒤 테스트가 옛 동작을 기대해 실패했다.
9. **DPI 인식 없이는 창 좌표/캡처가 틀린다 (2026-10-16).** 배율이 100%가 아닌 PC(이 PC는 125%)에서 DPI 비인식 프로세스(PowerShell, 기본 .NET 콘솔)가 `GetWindowRect`/`PrintWindow`/`CopyFromScreen`을 쓰면 창이 축소된 크기로 읽혀 **창의 왼쪽 위 일부만** 캡처된다 - 아래쪽의 진행 막대가 통째로 빠져 "안 움직인다"는 잘못된 결론이 나왔다(실제로 앞선 WPF 팝업 판정이 이 영향을 받았다). 하네스는 시작할 때 `Native.EnableDpiAwareness()`를 부르고, 측정 스크립트도 `SetProcessDpiAwarenessContext(-4)`를 먼저 호출한다.
10. **스스로 타이머로 그리는 창의 애니메이션은 `PrintWindow`로 판정할 수 없다.** `PrintWindow`는 그 시점에 창을 다시 그리게 해서 타이머가 멈춰도 매번 다른 그림이 나온다(결함 주입이 통과해 발견). 실제 화면(`CopyFromScreen`, `Native.CaptureChecksum`)을 읽고, 검사 간격에 무작위 편차를 줘 애니메이션 주기와 겹치는 현상(aliasing)을 피한다. 판정 신뢰는 반드시 **결함 주입으로 실패하는지** 확인한다.
11. **`Process.CloseMainWindow`/`MainWindowHandle`은 시작 직후 로딩 팝업을 메인 창으로 오인한다.** 시작하자마자 닫는 테스트(`FolderSizeCache_*`, `Settings_*`)가 팝업만 닫아 "앱이 정상 종료하지 않음"으로 실패했다 → `AppDriver.CloseGracefully`는 제목으로 찾은 메인 창에 WM_CLOSE를 보낸다.
12. **변이 검사로 테스트의 실효를 확인한다 (2026-10-16).** 파서/버퍼 테스트는 코드를 일부러 망가뜨려도 실패하는지 확인했다(탭 폭, 줄 삭제 경계, 크기 변경 커서 보정, SGR 22, 커서 위치 보고, 지우기 모드 1, 스크롤 영역 검증, 스크롤백 상한, 역방향 인덱스 - 9개 모두 실패로 잡힘). 변이를 넣었다 되돌릴 때는 파일 인코딩(BOM)까지 원본과 바이트 단위로 같은지 확인한다(한 번 BOM이 붙어 해시가 달라진 적이 있다).
13. **간헐 실패: "앱 창을 포그라운드로 만들지 못함".** 다른 창이 활성이거나 Windows의 포그라운드 전환 제한에 걸리면 UI 테스트가 가끔 실패한다(코드 문제 아님, 다시 실행하면 통과). UI 테스트 중에는 다른 작업을 하지 않는다.
14. **렌더링 리팩터링은 기준 화면과 픽셀 비교한다 (2026-10-16).** `TerminalControl` 분리 전에 색 샘플을 출력한 화면을 기준 이미지로 저장하고, 분리 후 같은 화면과 비교해 0픽셀 차이를 확인했다. 터미널 렌더링을 건드리는 변경은 같은 방법으로 확인한다(DPI 인식 필수, 9번 참고).
15. **설정 창 UI 테스트 7개와 `TerminalSelectionTests` 9개 추가.** 구조 변경(`MainViewModel`→하위 ViewModel) 전에 안전망으로 먼저 작성. 알림 막대는 `AlertHost`로 UI Automation에 노출되며 `PathBar_InvalidPath_*`에서 확인한다.

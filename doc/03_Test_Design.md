# 03. 자동 테스트 설계

> CodeAtlas의 `doc/10_UiAutomation_Test_Design.md` 방식을 따른다 (로직=MSTest, 화면=자체 하네스).

## 1. PathDock.Tests (MSTest, net10.0-windows) — 109개, 모두 통과

`doc/run_tests.ps1`(또는 `dotnet test src/PathDock.Tests`). GitHub Actions의 릴리스 워크플로도 같은 테스트를 먼저 실행한다.

| 영역 | 주요 케이스 |
|---|---|
| PathNormalizer | 후행 `\`, 드라이브 대소문자, 공백/한글, UNC, 상대경로, `..`, 경로에 올 수 없는 문자 |
| ExplorerService | 폴더 우선 정렬, 접근 불가 폴더, 뒤로/앞으로/위로, 존재하지 않는 경로 |
| SettingsService | 저장/복원, 손상된 JSON → 기본값, 즐겨찾기 10개 초과 거부 |
| PathSyncService | 4개 모드 × 2방향 표, Origin.Sync 무시, 동일 경로 무시, Busy 보류→Idle 반영, 경로 없음 경고 |
| VtParser/ScreenBuffer | 텍스트/개행/CR, 커서 이동, 지우기, SGR, 스크롤, OSC 파싱, 분할된 바이트열(UTF-8·ESC 시퀀스), 스크롤백 포함 전체 텍스트, 밀린 줄 수 계산 |
| TerminalSession(통합) | PowerShell 기동 → 프롬프트·초기 cwd 수신, 대기 중 `Set-Location` 주입 후 cwd 마커 수신(요청으로 표시), 사용자 `cd`와 실행 중 보류 이동의 반영, **명령 프롬프트(cmd)** 에서도 cwd 추적 |
| 터미널 색 테마 | 색 문자열 파싱(`#RRGGBB`), 잘못된 값 무시, 테마 해석, 모든 테마가 16색을 갖고 기본 테마가 기본 팔레트와 같음 |
| 드라이브 / 폴더 크기 | 드라이브 목록, 재귀 집계·취소·접근 불가, 크기 캐시(Fresh/Stale/무효화/저장·복원), 크기 문구 형식 |
| 정렬 / 파일 조작 | 자연 정렬, 이름 검증, 이름 변경(대소문자만 변경 포함), 새 폴더, 복사·이동(이름 충돌, 자기 자신 안으로 금지) |
| 파일 분류 | 확장자 → 폴더/코드/문서/이미지 등 |
| 보안 수정 회귀 | PowerShell 작은따옴표 변형 문자 이스케이프, 토큰이 있는 파서의 위조 제목 마커 무시, 로그 제어문자 제거, 복사·이동의 이름 충돌/같은 폴더 처리, UNC 경로는 휴지통 아님 |
| 시스템 모니터 | 측정값 범위, 타이머 이벤트, GPU 엔진 종류 파싱 |
| 미리보기 | 이미지·PDF 확장자 판별, 텍스트 미리보기(줄 수·긴 줄·탭, UTF-8/UTF-16 BOM, 바이너리·빈 파일·큰 파일 경계) |
| 다국어(`Loc`) | 문구 선택, 언어 변경 이벤트(같은 언어는 알림 없음), 저장 코드 파싱(OS 언어 대체), 파일 조작 메시지가 언어를 따름 |

ViewModel/뷰는 단위 테스트가 없고 UI Automation 하네스(2절)로 확인한다. `Loc`는 프로세스 전역 상태라 테스트가 끝날 때마다 한국어로 되돌린다.
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
| PathBar_InvalidPath_ShowsInfoBar_AndKeepsPath | 존재하지 않는 경로 입력 → 현재 경로 유지 |
| SyncMode_ExplorerToTerminal_ChangesTerminalPrompt | 양방향에서 즐겨찾기 이동 → 터미널 프롬프트가 새 폴더로 |
| SyncMode_TerminalToExplorer_UpdatesPathBar | 터미널에 `cd` 입력 → 경로 표시줄 갱신 |
| SyncMode_Off_DoesNotPropagate | 동기화 끄기 후 탐색기 이동이 터미널에 전파되지 않음 |
| Settings_SyncMode_PersistsAcrossRestart | 모드 변경 → 정상 종료 → 재시작 시 복원 |

하네스 보조: `--dump [AutomationId]`로 UIA 트리(이름/종류/Id)를 출력해 요소를 진단한다. `SendKeys` 전에
`Native.BringToFront`로 앱을 포그라운드로 만든다(안 하면 간헐 실패 — doc/04 3절). 목록은 UIA에서 `DataGrid/DataItem`으로 노출된다.

앱은 UIA 식별을 위해 주요 요소에 `AutomationProperties.AutomationId`를 지정한다
(`PathBar`, `SyncModeCombo`, `Favorite_1..10`, `FileList`, `Terminal`).
터미널 텍스트는 `TerminalControl`이 UIA `ValuePattern`(읽기 전용 화면 텍스트)으로 노출한다.

## 3. 실행

`doc/run_tests.ps1`, `doc/run_ui_tests.ps1` (doc/00 3절). UI 테스트는 화면을 실제로 조작하므로 실행 중 입력 금지.

## 4. 하네스에서 겪은 문제 (2026-10-13)

1. **소유 창은 UIA에서 메인 창의 자식으로 보인다.** 모달 입력창/메시지 상자는 데스크톱 직속이 아니라 `app.Window`의 Children에서 찾는다(`FindDialog`).
2. **`Launch` 실패 시 프로세스 누수.** 창 대기 중 예외가 나면 호출 측 `using`이 아직 소유하기 전이라 앱이 남는다 → 시작 후 예외는 `Launch` 안에서 `Kill`한다. (실제로 실패 테스트마다 앱이 1개씩 쌓여 PC에 8개가 떠 있었다.)
3. **메인 창은 `Process.MainWindowHandle` → `AutomationElement.FromHandle`로 얻는다.** 데스크톱 루트를 UIA로 훑는 방식은 최상위 창이 많을 때 느려져(테스트당 10~17초, 20초 대기 초과) 간헐 실패했다. 변경 후 테스트당 약 3초.
4. 시작 직후 포커스는 터미널에 있으므로, 탐색기 단축키 테스트는 목록 요소에 먼저 `SetFocus` 한다.
5. 진단 도구: `--dump [AutomationId]`(UIA 트리), `--probe-f2 [키]`(키 입력 후 최상위/자식 창과 앱 로그 출력).

## 5. 현재 상태와 알려진 공백 (2026-10-13)

- MSTest 109개는 모두 통과한다.
- UI Automation 19개는 UI 정리 직후(2절 표 작성 시점)에는 모두 통과했지만, **이후 바뀐 화면 때문에 다시 돌려 보지 않았다.** 아래 변경은 테스트와 맞지 않을 수 있어 재검증이 필요하다:
  이름 필터 입력줄이 폴더 머리글 오른쪽의 상시 입력칸(`FilterBox`)으로 바뀜, 즐겨찾기 10칸이 5칸씩 두 줄로 배치될 수 있음, 파일 목록 열 폭 자동 조정,
  알림 막대가 탐색기 위에 겹쳐 표시됨, 새 기본 분할 비율(6:4), 언어 설정(기본은 OS 언어이므로 한국어 OS에서는 한국어 이름을 그대로 쓴다).
- 새 기능 중 단위 테스트가 없는 것(화면 확인만 함): PDF 썸네일 렌더링, 문서를 일반 권한으로 여는 경로(`UnelevatedLauncher`), 터미널 화면 지우기, 이름 검색 입력칸, 미리보기 툴팁의 화면 표시, 언어 전환의 화면 반영, 버전·빌드 날짜 표시.
- UI 테스트가 쓰는 한국어 요소 이름(`AutomationProperties.Name`)은 언어가 영어이면 영어로 나온다. 하네스는 한국어 설정(또는 OS 언어)을 전제로 한다.
- GitHub Actions 릴리스 워크플로는 수동 실행과 태그 푸시(`v1.0.0`)로 한 번씩 성공했다. SignPath 서명 단계는 실행해 본 적이 없다.

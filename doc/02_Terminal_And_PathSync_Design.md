# 02. 터미널 및 경로 동기화 설계

## 1. 기획서 7절 결정 사항 (초기 기본값 — 변경 가능)

| 질문 | 결정 |
|---|---|
| 단일 패널 vs 듀얼 | **단일 패널**로 시작. 듀얼은 5차 이후 |
| 즐겨찾기 개수 | **최대 10칸**. 빈 칸 허용, 현재 폴더를 칸에 등록/삭제 |
| 실행 중 명령 중 탐색기 이동 | **보류(pending)**: 마지막 요청 1건만 보관, 프롬프트가 다시 나오면 반영. InfoBar로 "대기 중" 표시 |
| 분할/창 크기 복원 | 지원(settings.json) |
| 기본 동기화 모드 | 양방향 |

## 2. ConPTY

- `CreatePseudoConsole(size, hInRead, hOutWrite, 0)` + `STARTUPINFOEX`의 `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`로
  `powershell.exe -NoLogo -NoExit -ExecutionPolicy Bypass -Command "<셸 통합 한 줄 명령>"`을 실행(`ConPtySession`). 스크립트 파일을 만들지 않는다(8.1절).
- 출력 읽기: 전용 스레드가 파이프를 읽어 `VtParser.Feed(bytes)` (UTF-8 디코더 상태 유지).
- 입력: 키 입력을 VT 시퀀스(방향키 `ESC[A` 등, Ctrl 조합 제어문자)로 변환해 파이프에 쓴다(`TerminalControl`).
- 크기: 컨트롤 크기 → 열/행 계산 → `ResizePseudoConsole`.
- 종료: 프로세스 종료 → 파이프 닫기 → `ClosePseudoConsole`(블록 가능성이 있어 별도 Task). 세션 소유자는 `TerminalViewModel`.

### 2.1 실측으로 확인한 함정 (구현에 반영됨)

1. **STARTF_USESTDHANDLES(핸들 0)를 반드시 지정한다.** 호스트 프로세스의 표준 출력이 리다이렉트되어 있으면(테스트 호스트,
   CI) 자식 PowerShell이 의사 콘솔 대신 호스트의 파이프로 출력하고 곧바로 종료한다. GUI 앱에서는 드러나지 않아 통합 테스트로 발견했다.
2. **`-EncodedCommand`를 쓰지 않는다.** PowerShell 5.1이 `#< CLIXML` 진행 정보를 화면에 쏟아낸다. 한때 임시 `.ps1`을 `-File`로 실행했으나, 관리자 권한 앱이 사용자 쓰기 가능한 임시 폴더의 파일을 실행하면 권한 상승에 악용될 수 있어 지금은 파일 없이 `-Command`로 직접 넘긴다(8.1절).
3. ConPTY는 시작 시 `ESC[?9001h`(win32-input-mode) 등을 보내지만 무시해도 동작한다. 응답이 필요한 질의(`ESC[6n`, `ESC[c`)는
   `VtParser.Reply`로 답한다.

## 3. 현재 경로 감지 (터미널 → 탐색기)

ConPTY는 cwd 변경 이벤트를 주지 않는다(기획서 4절 주의). 따라서 **PowerShell `prompt` 훅**을 쓴다.

**OSC 9;9 / 133을 직접 쓰지 못한 이유(실측)**: Windows PowerShell 5.1은 콘솔 VT 출력 모드가 꺼져 있어 `[Console]::Write`의
ESC 문자가 conhost에서 버려진다(`GetConsoleMode`도 실패). 그래서 **conhost가 항상 전달하는 창 제목(OSC 0)** 을 신호 채널로 쓴다.

```powershell
$global:__pdN = 0
$global:__pdOrigPrompt = $function:prompt
function global:prompt {
    $p = ''
    try { if ((Get-Location).Provider.Name -eq 'FileSystem') { $p = (Get-Location).ProviderPath } } catch {}
    $global:__pdN++
    $Host.UI.RawUI.WindowTitle = "PD:$($global:__pdN):$p"
    & $global:__pdOrigPrompt
}
```

- 제목 `PD:<번호>:<경로>` 를 `VtParser`가 가로채 `CwdReported(path)` → `PromptShown` 순서로 이벤트를 올린다(제목으로는 표시하지 않음).
- 번호가 프롬프트마다 증가하므로 경로가 같아도 제목이 바뀌어 conhost가 다시 전달한다 → "프롬프트 대기(Idle)" 신호로도 쓴다.
- 명령 출력 문자열을 파싱하지 않는다. 사용자 프로필이 prompt를 바꿔도 원본 함수를 호출해 보존한다.
- FileSystem 외 위치(레지스트리 등)는 경로가 비어 있어 cwd 보고 없이 Idle 신호만 보낸다.
- 한계: 실행 중 프로그램이 제목을 바꿔도 `PD:` 접두사가 없으면 무시한다. pwsh 7/WSL은 5차에서 별도 검증.

### 3.1 명령 프롬프트(cmd.exe) — 설정에서 셸 선택 (2026-10-13)

- 설정 `TerminalShell`(PowerShell/CommandPrompt)로 선택하며, 바꾸면 터미널을 즉시 다시 시작한다.
- cmd는 prompt 훅이 없어 **`PROMPT` 환경 문자열**로 같은 신호를 낸다: `cmd.exe /K prompt $E]0;PD:<토큰>:0:$P?$T$E\$P$G`
  (매 프롬프트마다 창 제목 `PD:토큰:0:경로?시각`을 내보내고 일반 프롬프트 `경로>`도 그대로 표시). 시각(`$T`)이 들어 있어 같은 경로라도 제목이 바뀌므로 Idle 신호가 된다.
- 이동 명령은 `cd /d "경로"`(UNC는 `cd`가 지원하지 않아 `pushd`) + `\r`, 화면 지우기는 `cls`. `%`가 들어간 경로는 따옴표 안에서도 확장되는 드문 한계가 있다.

## 4. Busy / Idle 상태 (`TerminalSession`)

- `PromptShown` 수신 → **Idle**.
- 사용자가 Enter(`\r`)를 보내거나 앱이 명령을 주입 → **Busy**. 다음 `PromptShown`까지 Busy.
- Busy 중 `RequestChangeDirectory`는 pending에 저장(마지막 1건만)하고, Idle이 되면 전송. 그 사이 같은 경로가 되었으면 버린다.
- 주입 명령: `Set-Location -LiteralPath '<경로, 작은따옴표는 두 번>'` + `\r` (공백·특수문자·한글·UNC 안전, 통합 테스트로 검증).
- 앱이 주입한 cd의 결과 cwd 보고는 `fromRequest=true`로 표시해 `ChangeOrigin.Sync`로 취급(재전파 금지).
- 한계: 사용자가 프롬프트에 타이핑하던 중이면 입력 줄 뒤에 붙는다. (ESC로 줄을 지우는 방안은 ConPTY가 `ESC`+문자를 Alt 조합으로
  해석할 위험이 있어 채택하지 않았다. 4차에서 PSReadLine `RevertLine` 연동을 검토.)

## 5. PathSyncService 규칙

```
SyncMode: Bidirectional(기본) | ExplorerToTerminal | TerminalToExplorer | Off
```
1. 입력 이벤트: `(path, source: Explorer|Terminal, origin: User|Sync)`.
2. `origin == Sync`이면 전파하지 않는다(루프 차단 1차).
3. 경로는 `PathNormalizer`로 정규화 후, **상대편의 현재 경로와 같으면 무시**(루프 차단 2차).
4. 모드가 해당 방향을 허용하지 않으면 무시(로그에 사유 기록).
5. 대상 폴더가 존재하지 않거나 접근 불가면 전파하지 않고 경고(`Warning` 이벤트 → InfoBar).
6. 터미널 대상 전파는 4절의 Busy 정책을 따른다.
7. 탐색기로의 전파는 `Origin.Sync`로 `NavigateTo`를 호출하므로 다시 터미널로 되돌아가지 않는다.

## 6. VT 지원 범위 (1차 구현)

CSI: `A B C D H f J K m G d L M P @ X S T r s u h l`(커서/지우기/스크롤/삽입삭제), SGR(16색·256색·트루컬러·굵게·밑줄·반전),
OSC 0/2(제목, 셸 통합 마커 `PD:n:경로` 포함), 9;9, 133;A(pwsh 7 대비). DEC 사설 모드 `?25`(커서 표시), `?1049`(대체 화면) 지원. 그 밖의 시퀀스는 무시(로그 Debug).
전체 에뮬레이터가 아니므로 vim 같은 풀스크린 앱은 5차에서 검증한다.

## 7. 터미널 컨트롤 기능 (`TerminalControl`, 2026-10-13)

- **선택/복사**: 마우스 드래그로 선택, 더블클릭은 단어 선택. 선택이 있으면 Ctrl+C는 복사(없으면 인터럽트), Ctrl+Shift+C는 항상 복사.
  우클릭은 선택이 있으면 복사, 없으면 붙여넣기. 선택 영역은 새 출력으로 화면이 스크롤되면 같이 따라 움직인다.
- **붙여넣기**: 줄바꿈(Enter)이 하나라도 있으면(끝의 줄바꿈 하나뿐인 한 줄도) 실행 전에 앞 5줄을 보여주는 확인 창을 띄운다. 탭과 Enter 외의 제어 문자(ESC 등)는 제거한다(`PasteGuard`).
- **색**: 테마 5종(`TerminalThemes`) + 사용자 지정 글자/배경색(`#RRGGBB`, 잘못된 형식은 테마 값 사용). 설정 창에서 미리보기와 함께 즉시 반영.
- **머리글 버튼**: 내용 저장(스크롤백 포함, 저장 창에서 경로/이름/확장자 선택), 화면 지우기(대기 중이면 셸의 `clear`/`cls`, 실행 중이면 직접 지움 — 스크롤백 포함), 사용법 도움말, 접기/펼치기.
- 한계: 마우스 입력을 터미널 안 프로그램(vim 등)에 전달하지 않는다. 리사이즈 시 줄바꿈 재배치는 하지 않는다.

## 8. 터미널 탭 (2026-10-15)

`TerminalViewModel`이 `Tabs`(`TerminalTab` 목록)를 관리한다. 화면의 `TerminalControl`은 하나뿐이고, 선택된 탭의 `Session`을 바인딩해 보여준다(탭 전환 = `Session` 교체, 셸 변경 때와 같은 경로).

- **탭 = 독립 세션**: 탭마다 `TerminalSession`(ConPTY 프로세스, 화면 버퍼)과 상태(Idle/Running/Exited/대기 중 경로)를 가진다. 숨은 탭의 셸도 계속 실행되고 출력은 그 탭 버퍼에 쌓인다. 다시 선택하면 크기를 맞춰(`ApplySize`) 보여준다.
- **동기화는 선택된 탭만**: `CwdChanged`와 `RequestChange`는 선택된 탭만 다룬다. 숨은 탭의 cwd 보고는 탭 제목(`TerminalTabTitle`)만 갱신하고 탐색기로 전파하지 않는다.
  탐색기 이동은 선택된 탭에만 전달되며, 그 탭이 Busy이면 그 탭에서 보류한다(상태 문구도 선택된 탭 기준).
- **탭 전환**: 선택이 바뀌면 그 탭의 현재 폴더를 "사용자가 터미널에서 이동한 것"(`fromRequest=false`)으로 알려 모드가 허용할 때 탐색기가 따라간다. 반대 방향(탐색기 → 새로 선택된 탭)은 하지 않는다.
- **새 탭**: 시작 폴더는 탐색기의 현재 폴더(`StartDirectoryProvider`), 셸은 설정의 셸(`DefaultShell`). 최대 8개(`MaxTabs`), 넘으면 알림.
- **닫기**: 마지막 탭은 닫지 않는다. 명령 실행 중(`Running`) 탭은 확인 창을 거친다. 선택된 탭을 닫으면 같은 위치(없으면 마지막)의 탭이 선택되고, 선택이 바뀌므로 위 전환 규칙대로 탐색기가 따라간다. 닫은 탭의 세션은 선택 교체 후 Dispose한다.
- **셸 변경(설정)**: 선택된 탭만 새 셸로 다시 시작하고 이후 새 탭의 기본 셸이 된다. 다른 탭은 그대로다.
- **세션 이벤트 필터**: 이벤트 핸들러는 탭과 세션을 함께 잡아 `ReferenceEquals(session, tab.Session)`일 때만 처리한다(교체되어 버려진 세션의 늦은 이벤트 무시).
- **단축키**: Ctrl+Shift+T/W, Ctrl+Tab/Ctrl+Shift+Tab. 터미널이 키를 셸로 보내기 전에 `MainWindow.OnPreviewKeyDown`에서 먼저 가로챈다.
- **제목**: `TerminalTabTitle.Build` = "PowerShell · 폴더" / "cmd · 폴더"(경로 모르면 셸 이름만, 루트는 `C:`).
- 한계: 탭은 저장하지 않는다(재시작하면 탭 1개). 탭 순서 바꾸기(드래그)와 탭 이름 직접 지정은 없다. `Save`/`Clear`/상태 표시는 선택된 탭에만 적용된다.

## 8.1 보안 설계 메모 (2026-10-16 리뷰 반영)

이 앱은 항상 관리자 권한으로 실행된다(테스트·실행에 필요 - 사용자 결정). 그래서 사용자 쓰기 가능한 위치나 터미널 출력(신뢰할 수 없는 입력)이 관리자 권한 동작으로 이어지지 않게 다음을 지킨다.

- **셸 신호는 토큰이 있는 창 제목 마커만 믿는다.** 토큰이 설정된 세션(PowerShell/cmd 모두)에서는 OSC 9;9(경로)와 OSC 133;A(프롬프트)를 무시한다. 이 둘은 토큰으로 인증할 수 없어, 화면에 출력된 내용(파일, 원격 출력)만으로 경로(UNC `\evil\share` 포함 - `Directory.Exists`만으로 NTLM 인증이 나간다)와 프롬프트 신호를 위조해 탐색기 이동이나 `Set-Location` 주입을 일으킬 수 있었다.
- **셸 통합을 파일로 두지 않는다.** `BuildShellIntegrationCommand`가 만든 한 줄(ASCII, 큰따옴표 없음)을 `-Command`로 넘긴다. 이전에는 `%TEMP%\PathDock\*.ps1`을 만들어 실행했는데, 같은 사용자의 일반 권한 프로세스가 그 파일을 바꿔치기할 수 있었다.
- **cmd는 현재 폴더에서 명령을 찾지 않는다.** 환경 변수 `NoDefaultCurrentDirectoryInExePath=1`을 cmd 자식 프로세스에만 덧붙인다(`ConPtySession`의 환경 블록, 이 앱의 환경은 안 바꿈). 탐색기에서 폴더를 열면 cmd가 자동으로 그 폴더로 이동하므로, 악성 폴더의 `git.exe` 같은 파일이 평범한 명령 대신 관리자 권한으로 실행되는 것을 막는다. `.\이름`으로 명시한 실행은 그대로 된다.
- **파서 자원 제한**: CSI 파라미터는 32개까지, 스크롤/역스크롤 횟수는 영역 높이까지(`ESC[65535S` 반복이 파서 락을 오래 잡아 UI를 멈추던 문제).
- **붙여넣기 확인**은 끝의 줄바꿈도 실행으로 본다(`PasteGuard`).

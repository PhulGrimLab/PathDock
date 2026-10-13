# 02. 터미널 및 경로 동기화 설계

## 1. 기획서 7절 결정 사항 (초기 기본값 — 변경 가능)

| 질문 | 결정 |
|---|---|
| 단일 패널 vs 듀얼 | **단일 패널**로 시작. 듀얼은 5차 이후 |
| 즐겨찾기 개수 | **최대 10칸**. 빈 칸 허용, 현재 폴더를 칸에 등록/삭제 |
| 실행 중 명령 중 탐색기 이동 | **보류(pending)**: 마지막 요청 1건만 보관, 프롬프트가 다시 나오면 반영. InfoBar로 "대기 중" 표시 |
| 분할/창 크기 복원 | 지원(settings.json) |
| 기본 동기화 모드 | 양방향 |
| 터미널 셸 | **PowerShell(기본)과 명령 프롬프트(cmd)** 중 설정에서 선택. 바꾸면 터미널이 같은 폴더에서 새 셸로 즉시 다시 시작(실행 중인 작업이 있으면 확인). WSL/pwsh 7은 미지원 |

## 2. ConPTY

- `CreatePseudoConsole(size, hInRead, hOutWrite, 0)` + `STARTUPINFOEX`의 `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`로
  `powershell.exe -NoLogo -NoExit -ExecutionPolicy Bypass -File <셸 통합 스크립트>` 또는 `cmd.exe /K prompt ...`(3.1절)를 실행(`ConPtySession`).
- 출력 읽기: 전용 스레드가 파이프를 읽어 `VtParser.Feed(bytes)` (UTF-8 디코더 상태 유지).
- 입력: 키 입력을 VT 시퀀스(방향키 `ESC[A` 등, Ctrl 조합 제어문자)로 변환해 파이프에 쓴다(`TerminalControl`).
- 크기: 컨트롤 크기 → 열/행 계산 → `ResizePseudoConsole`.
- 종료: 프로세스 종료 → 파이프 닫기 → `ClosePseudoConsole`(블록 가능성이 있어 별도 Task). 세션 소유자는 `TerminalViewModel`.

### 2.1 실측으로 확인한 함정 (구현에 반영됨)

1. **STARTF_USESTDHANDLES(핸들 0)를 반드시 지정한다.** 호스트 프로세스의 표준 출력이 리다이렉트되어 있으면(테스트 호스트,
   CI) 자식 PowerShell이 의사 콘솔 대신 호스트의 파이프로 출력하고 곧바로 종료한다. GUI 앱에서는 드러나지 않아 통합 테스트로 발견했다.
2. **`-EncodedCommand`를 쓰지 않는다.** PowerShell 5.1이 `#< CLIXML` 진행 정보를 화면에 쏟아낸다 → 임시 `.ps1`을 `-File`로 실행.
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

- 제목 `PD:<토큰>:<번호>:<경로>` 를 `VtParser`가 가로채 `CwdReported(path)` → `PromptShown` 순서로 이벤트를 올린다(제목으로는 표시하지 않음).
- 번호가 프롬프트마다 증가하므로 경로가 같아도 제목이 바뀌어 conhost가 다시 전달한다 → "프롬프트 대기(Idle)" 신호로도 쓴다.
- 명령 출력 문자열을 파싱하지 않는다. 사용자 프로필이 prompt를 바꿔도 원본 함수를 호출해 보존한다.
- FileSystem 외 위치(레지스트리 등)는 경로가 비어 있어 cwd 보고 없이 Idle 신호만 보낸다.
- **토큰**: 마커에는 실행마다 새로 만드는 토큰이 들어 있어, 셸에서 도는 다른 프로그램이 같은 제목을 출력해 경로/대기 상태를 속이는 것을 막는다(토큰이 다르면 무시).
- 한계: 실행 중 프로그램이 제목을 바꿔도 `PD:` 접두사가 없으면 무시한다. pwsh 7/WSL은 별도 검증이 필요하다.

### 3.1 명령 프롬프트(cmd)

`cmd.exe /K prompt $E]0;PD:<토큰>:0:$P?$T$E\$P$G` — `PROMPT`가 매 프롬프트마다 창 제목 `PD:<토큰>:0:<현재경로>?<시각>`을 내보내
PowerShell과 같은 파서·상태 머신(Idle/Busy, 보류 이동)을 쓴다. `$T`(시각, 1/100초)는 제목이 매번 달라지게 해 conhost가 다시 전달하도록 하고,
경로 해석에서는 `?` 뒤를 버린다(`?`는 경로에 올 수 없는 문자). 주입 명령은 `cd /d "<경로>"`(UNC는 `pushd`). `%`는 따옴표 안에서도 확장되는
cmd의 한계가 있다(드문 경우, 문서화된 한계).

## 4. Busy / Idle 상태 (`TerminalSession`)

- `PromptShown` 수신 → **Idle**.
- 사용자가 Enter(`\r`)를 보내거나 앱이 명령을 주입 → **Busy**. 다음 `PromptShown`까지 Busy.
- Busy 중 `RequestChangeDirectory`는 pending에 저장(마지막 1건만)하고, Idle이 되면 전송. 그 사이 같은 경로가 되었으면 버린다.
- 주입 명령: `Set-Location -LiteralPath '<경로, 작은따옴표는 두 번>'` + `\r` (공백·특수문자·한글·UNC 안전, 통합 테스트로 검증).
- 앱이 주입한 cd의 결과 cwd 보고는 `fromRequest=true`로 표시해 `ChangeOrigin.Sync`로 취급(재전파 금지).
- 한계: 사용자가 프롬프트에 타이핑하던 중이면 입력 줄 뒤에 붙는다. (ESC로 줄을 지우는 방안은 ConPTY가 `ESC`+문자를 Alt 조합으로
  해석할 위험이 있어 이 경로(탐색기 → 터미널 이동)에서는 채택하지 않았다. 4차에서 PSReadLine `RevertLine` 연동을 검토.)
- 예외: **화면 지우기 버튼**(7절)은 대기 중일 때 먼저 ESC로 입력 줄을 버린 뒤 지우기 명령을 보낸다. PowerShell에서는 동작을 확인했고, cmd에서는 화면이
  지워지는 것까지만 확인했다(지운 뒤 이어서 입력되는지는 미확인).

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

## 6. VT 지원 범위

CSI: `A B C D H f J K m G d L M P @ X S T r s u h l`(커서/지우기/스크롤/삽입삭제), SGR(16색·256색·트루컬러·굵게·밑줄·반전),
OSC 0/2(제목, 셸 통합 마커 `PD:토큰:…` 포함), 9;9, 133;A(pwsh 7 대비). DEC 사설 모드 `?25`(커서 표시), `?1049`(대체 화면) 지원. 그 밖의 시퀀스는 무시(로그 Debug).
전체 에뮬레이터가 아니므로 vim 같은 풀스크린 앱은 별도 검증이 필요하다. 스크롤백은 최대 2000줄.

## 7. 터미널 화면 기능 (UI 쪽)

- **마우스 선택·복사**: 드래그로 글자 선택, 더블클릭은 단어 선택. 선택은 새 출력으로 화면이 밀려도 같은 글자를 따라가고, 스크롤백 밖으로 밀려나면 풀린다.
  `Ctrl+C`는 선택이 있으면 복사, 없으면 실행 중단(SIGINT 해당), `Ctrl+Shift+C`는 항상 복사, 우클릭은 선택이 있으면 복사·없으면 붙여넣기.
- **붙여넣기**: `Ctrl+V`/우클릭. 여러 줄이면 줄마다 명령이 실행되므로 한 번 확인한다.
- **화면 지우기**(머리글 버튼): 대기 중이면 셸에 `Clear-Host`(cmd는 `cls`)를 보내(앞에 ESC로 입력 줄 버림) 셸이 프롬프트를 다시 그리게 하고, 명령 실행 중이면
  화면과 스크롤백만 직접 지운다(실행 중인 프로그램의 출력은 계속 이어진다).
- **내용 저장**(머리글 버튼): 스크롤백까지 포함한 전체 내용을 텍스트 파일로 저장(저장 대화상자).
- **색 테마**: 기본(어두움)·One Dark·Dracula·Solarized Dark·밝음 5종 + 글자색/배경색 `#RRGGBB` 직접 지정(잘못 입력하면 안내하고 테마 색 유지). 설정 창에서 미리보기.
- **글자 크기**: 설정에서 10~28. 터미널 영역은 접고 펼 수 있다(셸은 계속 실행).

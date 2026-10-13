# PathDock
개발자가 여러 프로젝트를 관리/전환 하는 도구입니다.

즐겨찾기 폴더 · 파일 탐색기 · 내장 터미널(PowerShell / 명령 프롬프트)을 한 화면에 두고, 탐색기와 터미널의 현재 경로를
양방향(또는 단방향/끄기)으로 동기화합니다. C# / .NET 10 / WPF / MVVM. 화면은 **한국어/English**를 설정에서 바꿀 수 있습니다.

> *PathDock puts favorite folders, a file explorer and a built-in terminal (PowerShell / Command Prompt) in one window and keeps the
> explorer and terminal folders in sync (both ways, one way, or off). Built with C# / .NET 10 / WPF. The UI language
> (Korean / English) can be switched in Settings.*

## 다운로드 (Download)

[**Releases**](https://github.com/PhulGrimLab/PathDock/releases)에서 `PathDock-v<버전>-win-x64.exe`(또는 `.zip`)를 받아 실행하면 됩니다.
.NET 런타임이 포함된 단일 파일이라 설치가 필요 없습니다(Windows 10 2004 이상 64비트. 개발과 확인은 Windows 11에서 했고, 다른 PC·Windows 10에서의 실행은 아직 확인하지 못했습니다).

- **관리자 권한으로 실행됩니다.** 실행할 때 UAC 확인 창이 뜹니다.
- 코드 서명이 없는 빌드는 처음 실행할 때 Windows SmartScreen이 "알 수 없는 게시자" 경고를 띄울 수 있습니다("추가 정보 → 실행").
- 같은 릴리스의 `SHA256SUMS.txt`로 내려받은 파일을 확인할 수 있습니다.

*Download `PathDock-v<version>-win-x64.exe` (or the `.zip`) from Releases. It is a self-contained single file, so nothing needs to be installed.
It runs as administrator (a UAC prompt appears). Unsigned builds may trigger a SmartScreen warning.*

## 주요 기능

**즐겨찾기** — 상단에 폴더 10칸. 클릭하면 이동, 빈 칸을 클릭하면 현재 폴더 등록. 우클릭으로 이름 바꾸기, 칸 이동(왼쪽/오른쪽), 비우기.

**파일 탐색기** — 드라이브(사용량 막대) · 조부모 폴더 · 부모 폴더 · 현재 폴더의 4열 구성(Mdir 스타일), 열 폭 조절.
- 폴더 크기와 하위 파일·폴더 수를 백그라운드로 계산하고 캐시합니다(5 GB 이상은 "5 GB 이상", 1000개 이상은 "1천개 이상"으로 표시).
- 이름/수정한 날짜/크기 정렬, 뒤로·앞으로·위로·새로 고침, 주소 표시줄에 경로 직접 입력.
- 복사·잘라내기·붙여넣기, 이름 변경, 삭제(휴지통), 새 폴더, 다중 선택. 폴더 머리글 오른쪽의 **이름 검색**(Ctrl+F)으로 이름에 포함된 항목만 보기.
- 이미지(png/jpg/gif/bmp/ico/tif/webp), **PDF**(첫 쪽, Windows 내장 렌더러), 텍스트·소스(txt/md/log/json/cs 등) 파일 위에 마우스를 올리면 **미리보기 툴팁**(썸네일 / 첫 쪽 / 앞 20줄).
- 문서·이미지 같은 파일은 **일반 사용자 권한(탐색기와 같은 방식)으로** 열립니다. 앱이 관리자 권한이라 그대로 열면 Adobe Reader 같은 프로그램이 반응 없이 열리지 않을 수 있기 때문입니다(exe/스크립트는 앱의 권한으로 실행).

**내장 터미널** — ConPTY 기반. PowerShell 또는 명령 프롬프트(설정에서 선택), 마우스 선택·복사·붙여넣기(여러 줄 붙여넣기는 확인), 화면 지우기,
내용을 파일로 저장, 글자 크기와 색 테마(5종 + 글자색/배경색 직접 지정).

**경로 동기화** — 양방향 / 탐색기 → 터미널 / 터미널 → 탐색기 / 끄기. 실행 중인 명령이 있으면 이동을 보류했다가 프롬프트가 돌아오면 반영합니다.

**그 밖에** — 다크/라이트 테마, 화면·터미널 글자 크기, 한국어/English, 제목 표시줄의 버전·빌드 날짜와 정보 창, 계산기·휴지통 비우기 버튼,
하단 CPU/GPU/메모리 사용량, 창 위치·크기·분할 비율과 마지막 폴더 복원.

### 단축키

| 위치 | 키 | 동작 |
|---|---|---|
| 파일 목록 | Enter / Backspace | 열기 / 상위 폴더 |
| | Ctrl+C / X / V | 복사 / 잘라내기 / 붙여넣기 |
| | F2 / Delete | 이름 변경 / 삭제(휴지통) |
| | Ctrl+Shift+N | 새 폴더 |
| | Ctrl+F | 이름 검색(Esc로 해제) |
| | F5 | 새로 고침(폴더 크기도 다시 계산) |
| 터미널 | 마우스 드래그 / 더블클릭 | 글자 선택 / 단어 선택 |
| | Ctrl+C | 선택이 있으면 복사, 없으면 실행 중단 |
| | Ctrl+Shift+C / Ctrl+V | 복사 / 붙여넣기 |
| | 우클릭 | 선택이 있으면 복사, 없으면 붙여넣기 |
| 어디서나 | Ctrl+Alt+C / Ctrl+Alt+E | 계산기 / 휴지통 비우기 |

## 빌드와 실행

필요: Windows, .NET SDK 10.

```
dotnet run --project src/PathDock.App          # 실행(Debug)
dotnet build src/PathDock.App -c Release       # Release 빌드: publish\Release\PathDock.App.exe(단일 파일)까지 자동 생성
doc\run_tests.ps1                              # 단위·통합 테스트(MSTest)
doc\run_ui_tests.ps1                           # UI Automation 테스트(실행 중 마우스/키보드 사용 금지)
```

설정과 로그는 `%APPDATA%\PathDock`에 저장됩니다(`PATHDOCK_DATA_DIR` 환경 변수로 위치 변경).

## 릴리스 (GitHub Actions)

`v`로 시작하는 태그를 푸시하면 [Release 워크플로](./.github/workflows/release.yml)가 테스트 → Release 빌드 → 패키징(exe, zip, SHA256SUMS) →
GitHub Release 생성을 자동으로 합니다. 태그 번호가 앱의 버전이 됩니다.

```
git tag v1.0.1
git push origin v1.0.1
```

Actions 탭의 **Run workflow**(수동 실행)는 릴리스를 만들지 않고 빌드·테스트만 확인합니다. 코드 서명(SignPath Foundation)은 저장소 변수
`SIGNPATH_ORGANIZATION_ID` 등을 설정하면 켜집니다. 자세한 내용은 [doc/00](./doc/00_Environment_And_Build.md).

## 문서

- 기획: [PathDock_개발개요.md](./PathDock_개발개요.md)
- 개발 문서: [doc/](./doc)
  - [00 환경·빌드·릴리스](./doc/00_Environment_And_Build.md) · [01 아키텍처](./doc/01_Architecture.md)
  - [02 터미널·경로 동기화 설계](./doc/02_Terminal_And_PathSync_Design.md) · [03 테스트 설계](./doc/03_Test_Design.md)
  - [04 진행 상황·다음 할 일](./doc/04_Progress_And_Next_Steps.md) · [05 UI 구조](./doc/05_UI_Design.md)

## 라이선스

[GPL-3.0](./LICENSE)

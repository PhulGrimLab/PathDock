# 00. 개발 환경 및 빌드/테스트 실행

> 기획 원문: [../PathDock_개발개요.md](../PathDock_개발개요.md)
> 참고한 개발 방식: `C:\Works_PhulGrimLab\CodeAtlas_v0` (문서 번호 체계, `doc/run_tests.ps1`, 자체 UI Automation 하네스)
> 참고한 컨트롤: `C:\Works_PhulGrimLab\WpfControlSamples\wpfui` (WPF-UI, Fluent)

## 1. 환경

| 항목 | 값 |
|---|---|
| SDK | .NET SDK 10.0.x (`dotnet --list-sdks`) |
| 타깃 | `net10.0-windows` (WPF) |
| 패턴 | MVVM — `CommunityToolkit.Mvvm` 8.4.x (Source Generator) |
| UI 컨트롤 | `WPF-UI` 4.3.0 (FluentWindow, Button, Card, InfoBar, SymbolIcon 등) + 자체 커스텀 컨트롤 |
| 테스트 | MSTest(로직, 112개) + 자체 콘솔 하네스(UI Automation 25개, 실제 프로세스 구동) |
| 솔루션 | `PathDock.slnx` (SDK-style 이므로 VS2019 제약 없음 — CodeAtlas와 다름) |
| 플랫폼 | x64 고정(`Directory.Build.props`). 출력은 `bin\x64\<구성>\net10.0-windows` |
| 실행 권한 | `app.manifest`의 `requireAdministrator` — 앱은 항상 관리자 권한으로 실행된다 |

## 2. 폴더 구조

```
PathDock/
├─ PathDock.slnx
├─ Directory.Build.props        공통 빌드 설정(x64, Nullable, ImplicitUsings)
├─ .github/workflows/release.yml  태그 푸시 시 테스트·빌드·GitHub Release (5절)
├─ tools/make_icon.ps1          앱 아이콘 생성
├─ doc/                         이 문서들 + 실행 스크립트
├─ src/
│  ├─ PathDock.Core/            UI 비의존 로직(경로 정규화, 동기화 중재, 설정, ConPTY, VT 파서)
│  ├─ PathDock.App/             WPF 앱(View/ViewModel/커스텀 컨트롤)
│  ├─ PathDock.Tests/           MSTest 단위/통합 테스트
│  └─ PathDock.UiAutomation.Tests/  실제 PathDock.App.exe를 띄워 조작하는 하네스
```

## 3. 실행 스크립트

```
PS> .\doc\run_tests.ps1             # 복원 + 빌드 + MSTest 전체
PS> .\doc\run_ui_tests.ps1          # 빌드 + UI Automation 하네스 (실행 중 마우스/키보드 금지)
PS> .\doc\run_ui_tests.ps1 -SkipBuild -Tests <이름...>
```

## 4. 로그와 설정 위치

- 설정: `%APPDATA%\PathDock\settings.json` (환경 변수 `PATHDOCK_DATA_DIR`로 재지정 — 테스트 격리용)
- 로그: `%APPDATA%\PathDock\logs\pathdock-yyyyMMdd.log` (비동기 큐 + 전용 Writer, doc/01 5절). 로그 문구는 번역하지 않는다.
- 폴더 크기 캐시: `%APPDATA%\PathDock\folder-size-cache.json` (doc/04 6절)

`settings.json` 항목: 즐겨찾기(최대 10), `SyncMode`, `LastPath`/`StartAtLastPath`, 창 위치·크기·최대화, `ExplorerRatio`(탐색기:터미널 비율, 기본 0.6),
`Theme`(Dark/Light), `UiFontSize`, `TerminalFontSize`, `TerminalTheme`/`TerminalForeground`/`TerminalBackground`, `TerminalShell`(PowerShell/CommandPrompt),
`TerminalCollapsed`, `Language`(`ko`/`en`, 비면 OS 표시 언어를 따름). 손상되면 기본값으로 시작한다.

## 5. 배포와 릴리스

- **단일 파일**: 앱은 자체 포함(self-contained, win-x64). Release 빌드가 끝나면 자동으로 publish까지 실행해 저장소 루트 `publish\Release\PathDock.App.exe` 하나를 만든다
  (끄려면 `-p:PublishOnBuild=false`, Debug 빌드는 건너뜀). 한국어/영어 리소스만 포함한다.
- **릴리스 워크플로**(`.github/workflows/release.yml`): `v*` 태그 푸시 → `dotnet test` → Release 빌드 → 패키징(exe/zip/SHA256SUMS) → GitHub Release 생성.
  태그 번호가 빌드 버전이며 `-`가 들어가면 프리릴리스. 수동 실행(workflow_dispatch)은 빌드·테스트만 확인한다.
  저장소 변수 `SIGNPATH_ORGANIZATION_ID`가 있을 때만 SignPath Foundation으로 서명한다(없으면 서명 없이 릴리스).
- UI 테스트는 데스크톱 세션이 필요해 워크플로에서 돌리지 않는다.

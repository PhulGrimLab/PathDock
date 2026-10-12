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
| UI 컨트롤 | `WPF-UI` 4.x (FluentWindow, Button, Card, InfoBar, SymbolIcon 등) + 자체 커스텀 컨트롤 |
| 테스트 | MSTest(로직) + 자체 콘솔 하네스(UI Automation, 실제 프로세스 구동) |
| 솔루션 | `PathDock.slnx` (SDK-style 이므로 VS2019 제약 없음 — CodeAtlas와 다름) |

## 2. 폴더 구조

```
PathDock/
├─ PathDock.slnx
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
- 로그: `%APPDATA%\PathDock\logs\pathdock-yyyyMMdd.log` (비동기 큐 + 전용 Writer, doc/01 5절)

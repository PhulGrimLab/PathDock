# PathDock
개발자가 여러 프로젝트를 관리/전환 하는 도구입니다.

즐겨찾기 폴더 · 파일 탐색기 · 내장 PowerShell 터미널을 한 화면에 두고, 탐색기와 터미널의 현재 경로를
양방향(또는 단방향/끄기)으로 동기화합니다. C# / .NET 10 / WPF / MVVM.

- 기획: [PathDock_개발개요.md](./PathDock_개발개요.md)
- 개발 문서: [doc/](./doc) (환경·아키텍처·터미널/동기화 설계·테스트·진행 상황)
- 빌드/실행: `dotnet run --project src/PathDock.App`
- 테스트: `doc\run_tests.ps1` (MSTest), `doc\run_ui_tests.ps1` (UI Automation)

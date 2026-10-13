# 00. 개발 환경 및 빌드/테스트/릴리스

> 기획 원문: [../PathDock_개발개요.md](../PathDock_개발개요.md)
> 참고한 개발 방식: `C:\Works_PhulGrimLab\CodeAtlas_v0` (문서 번호 체계, `doc/run_tests.ps1`, 자체 UI Automation 하네스)
> 참고한 컨트롤: `C:\Works_PhulGrimLab\WpfControlSamples\wpfui` (WPF-UI, Fluent)

## 1. 환경

| 항목 | 값 |
|---|---|
| SDK | .NET SDK 10.0.x (`dotnet --list-sdks`) |
| 타깃 | 앱 `net10.0-windows10.0.19041.0`(WPF, **Windows 10 2004 이상**), Core/Tests `net10.0-windows`. 플랫폼 x64 고정(`Directory.Build.props`) |
| 패턴 | MVVM — `CommunityToolkit.Mvvm` 8.4.x (Source Generator) |
| UI 컨트롤 | `WPF-UI` 4.x (FluentWindow, Button, InfoBar, SymbolIcon 등) + 자체 커스텀 컨트롤 |
| 테스트 | MSTest(로직, 108개) + 자체 콘솔 하네스(UI Automation, 실제 프로세스 구동) |
| 솔루션 | `PathDock.slnx` (SDK-style 이므로 VS2019 제약 없음 — CodeAtlas와 다름) |
| 라이선스 | GPL-3.0 (`LICENSE`) |

## 2. 폴더 구조

```
PathDock/
├─ PathDock.slnx
├─ Directory.Build.props        공통 빌드 설정(x64, Nullable, ImplicitUsings)
├─ .github/workflows/release.yml  태그 푸시 → 빌드·테스트·GitHub Release (이 문서 5절)
├─ doc/                         이 문서들 + 실행 스크립트
├─ tools/make_icon.ps1          앱 아이콘 생성기
├─ publish/                     Release 빌드의 단일 파일 exe(git에 올리지 않음)
├─ src/
│  ├─ PathDock.Core/            UI 비의존 로직(경로 정규화, 동기화 중재, 설정, ConPTY, VT 파서, 파일 조작, 미리보기, 언어)
│  ├─ PathDock.App/             WPF 앱(View/ViewModel/커스텀 컨트롤/서비스)
│  ├─ PathDock.Tests/           MSTest 단위/통합 테스트
│  └─ PathDock.UiAutomation.Tests/  실제 PathDock.App.exe를 띄워 조작하는 하네스
```

## 3. 빌드와 실행

```
dotnet run --project src/PathDock.App                 # 실행(Debug)
dotnet build src/PathDock.App -c Release              # Release: 끝나면 단일 파일 publish까지 자동 실행
dotnet build src/PathDock.App -c Release -p:PublishOnBuild=false   # publish 없이 컴파일만(실행 중인 exe가 잠겨 있을 때)
```

- **self-contained**: `RuntimeIdentifier=win-x64`, `SelfContained=true`라 .NET이 설치되지 않은 PC에서도 실행되도록 런타임을 출력 폴더에 함께 넣는다.
  다국어 리소스는 `ko;en`만 남긴다.
- **단일 파일 publish**: Release 빌드가 끝나면 `PathDock.App.csproj`의 `PublishSingleFileAfterBuild` 타깃이 `publish\Release\PathDock.App.exe`
  하나로 만든다(관리 DLL은 exe 안에 압축, WPF 네이티브 DLL은 첫 실행 때 임시 폴더로 풀림). Debug 빌드는 건너뛴다. 실행 중인 exe가
  잠겨 있으면 이 publish 단계가 실패하므로 앱을 닫고 다시 빌드한다(위의 `PublishOnBuild=false`로 컴파일만 확인할 수도 있다).
- **대상 프레임워크**: 앱은 PDF 미리보기에 Windows 내장 렌더러(`Windows.Data.Pdf`, WinRT)를 쓰므로 `net10.0-windows10.0.19041.0`를 대상으로 한다. 이 때문에 앱의 빌드 출력 폴더 이름이
  `bin\x64\<구성>\net10.0-windows10.0.19041.0`이다(UI 테스트 하네스의 `AppDriver`는 후보 폴더를 확인해 찾는다). 첫 빌드에는 Windows SDK 참조 패키지를 내려받는다(인터넷 필요).
- **관리자 권한**: `app.manifest`의 `requestedExecutionLevel`이 `requireAdministrator`라 실행하면 UAC 확인 창이 뜬다.
  개발 중 `dotnet run`도 관리자 권한 터미널이 필요하다.
- **버전**: 기본값은 `csproj`의 `<Version>`(현재 1.0.0). 릴리스 빌드는 태그 번호를 `-p:Version=`으로 넘겨 덮어쓴다(5절).
  앱의 제목 표시줄/정보 창에 보이는 버전과 빌드 날짜는 어셈블리 버전과 실행 파일의 수정 시각이다(`AppInfo`).

## 4. 테스트 실행 스크립트

```
PS> .\doc\run_tests.ps1             # 복원 + 빌드 + MSTest 전체
PS> .\doc\run_ui_tests.ps1          # 빌드 + UI Automation 하네스 (실행 중 마우스/키보드 금지)
PS> .\doc\run_ui_tests.ps1 -SkipBuild -Tests <이름...>
```

테스트 구성과 현황은 [03_Test_Design.md](./03_Test_Design.md).

## 5. 릴리스 (GitHub Actions)

`.github/workflows/release.yml` — GitHub의 Windows 러너에서 같은 방식으로 빌드해 릴리스를 만든다.

| 트리거 | 동작 |
|---|---|
| `v*` 태그 푸시 (예: `v1.0.1`) | 테스트 → Release 빌드 → (선택) 서명 → 패키징 → **GitHub Release 생성** |
| 수동 실행(Actions 탭의 Run workflow) | 테스트·빌드·패키징만 하고 결과물을 아티팩트로 남김(릴리스는 만들지 않음) |

- **버전**: 태그에서 앞의 `v`를 뗀 값이 버전이 된다. `1.2.3-beta1`처럼 `-`가 있으면 프리릴리스로 표시하고, 파일/어셈블리 버전에는 앞의 숫자 부분만 쓴다.
- **산출물**: `PathDock-v<버전>-win-x64.exe`, 같은 이름의 `.zip`, `SHA256SUMS.txt`(서명한 경우 서명 뒤의 파일 기준). 릴리스 노트는 자동 생성.
- **릴리스 절차**:
  ```
  git tag v1.0.1
  git push origin v1.0.1
  ```
  태그는 `main`의 원하는 커밋에 붙이고, 첫 릴리스(`v1.0.0`)는 이 방식으로 만들었다.
- **코드 서명(선택, SignPath Foundation)**: 저장소 변수 `SIGNPATH_ORGANIZATION_ID`가 있을 때만 서명 단계가 켜진다(없으면 서명 없이 릴리스).
  SignPath 승인 후 저장소 Settings → Secrets and variables → Actions에 변수 `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`,
  `SIGNPATH_POLICY_SLUG`와 시크릿 `SIGNPATH_API_TOKEN`을 추가한다. 서명 단계는 아직 실행해 본 적이 없다(미검증).
- 알려진 경고: 사용 중인 액션(`checkout`/`setup-dotnet`/`upload-artifact` v4)이 Node.js 20 대상이라 "Node.js 20 지원 중단" 경고가 뜬다(동작에는 문제 없음).

## 6. 로그와 설정 위치

- 설정: `%APPDATA%\PathDock\settings.json` (환경 변수 `PATHDOCK_DATA_DIR`로 재지정 — 테스트 격리용). 저장 항목: 즐겨찾기, 동기화 모드, 창 위치/크기/최대화,
  분할 비율, 마지막 경로·시작 방식, 테마, 화면/터미널 글자 크기, 터미널 색 테마·글자색·배경색, 터미널 셸, 터미널 접힘, **언어**(`Language`: `ko`/`en`, 비면 OS 표시 언어).
- 폴더 크기 캐시: `%APPDATA%\PathDock\folder-size-cache.json`
- 로그: `%APPDATA%\PathDock\logs\pathdock-yyyyMMdd.log` (비동기 큐 + 전용 Writer, doc/01 5절). 로그 문구는 번역하지 않는다(개발자용, 한국어).

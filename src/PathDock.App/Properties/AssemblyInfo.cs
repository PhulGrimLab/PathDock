using System.Runtime.InteropServices;
using System.Windows;

// 커스텀 컨트롤의 기본 스타일(Themes/Generic.xaml)을 이 어셈블리에서 찾도록 한다.
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

// 이 어셈블리의 모든 P/Invoke는 시스템 폴더(System32)에서만 DLL을 찾는다(관리자 권한 앱의 DLL 검색 순서 가로채기 방지, 분석기 CA5392).
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

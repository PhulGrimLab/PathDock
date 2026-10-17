using System.Runtime.InteropServices;

// 이 어셈블리의 모든 P/Invoke는 시스템 폴더(System32)에서만 DLL을 찾는다. 이 앱은 관리자 권한으로 실행되므로,
// 앱 폴더나 현재 폴더에 심어 둔 같은 이름의 DLL이 먼저 로드되는 일(DLL 검색 순서 가로채기)을 막는다(분석기 CA5392).
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

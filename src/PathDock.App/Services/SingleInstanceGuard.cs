using System.IO;
using System.Runtime.InteropServices;
using PathDock.Core.Settings;

namespace PathDock.App.Services;

/// <summary>
/// 같은 데이터 폴더로는 앱이 하나만 뜨게 한다(설정·캐시·로그 파일을 두 인스턴스가 함께 쓰면 나중에 닫은 쪽이 변경을 덮어쓴다).
/// 이름 있는 Mutex로 먼저 떠 있는 인스턴스를 찾고, 있으면 이름 있는 이벤트로 "창을 앞으로" 신호를 보낸 뒤 이 실행은 종료한다.
/// 이름은 데이터 폴더마다 다르므로(<see cref="SingleInstanceName"/>) 다른 데이터 폴더(UI 테스트, 개발용)는 따로 뜰 수 있다.
/// Owner: App, Lifetime: 프로세스 전체. <see cref="TryAcquire"/>와 <see cref="Dispose"/>는 같은 스레드(UI)에서 호출한다(Mutex 소유 스레드).
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private const int AllowSetForegroundAnyProcess = -1; // ASFW_ANY

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly ManualResetEvent _stopListening = new(false);
    private Thread? _listener;
    private bool _owned = true;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
    }

    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>
    /// 이 데이터 폴더의 첫 인스턴스면 true(보호 객체를 <paramref name="guard"/>로 돌려줌). 이미 떠 있으면 그 인스턴스에 "앞으로 나와라" 신호를 보내고 false
    /// (호출 측은 곧바로 종료해야 한다). 이름 있는 객체를 만들 수 없는 예외적인 경우에는 막지 않고 계속하도록 보호 객체 없이(null) true를 돌려준다.
    /// </summary>
    public static bool TryAcquire(string dataDir, out SingleInstanceGuard? guard)
    {
        guard = null;
        string suffix = SingleInstanceName.For(dataDir);
        try
        {
            var mutex = new Mutex(initiallyOwned: false, $@"Local\PathDock-{suffix}");
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                acquired = true; // 이전 인스턴스가 비정상 종료했다: 이어받는다.
            }
            // 이벤트는 첫 인스턴스가 만들고 자동 초기화로 둔다: 창이 뜨기 전에 온 신호도 기억했다가 창이 준비되면 한 번 처리한다.
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\PathDock-{suffix}-show");
            if (!acquired)
            {
                AllowSetForegroundWindow(AllowSetForegroundAnyProcess); // 첫 인스턴스가 포그라운드로 나올 수 있게 허용한다.
                showEvent.Set();
                showEvent.Dispose();
                mutex.Dispose();
                return false;
            }
            guard = new SingleInstanceGuard(mutex, showEvent);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            return true; // 보호할 수 없으면(권한 등) 막지 않고 계속한다.
        }
    }

    /// <summary>다른 실행이 신호를 보내면 <paramref name="onShowRequested"/>를 부른다(백그라운드 스레드에서 호출되므로 UI 작업은 호출 측이 디스패처로 옮긴다).</summary>
    public void StartListening(Action onShowRequested)
    {
        _listener = new Thread(() =>
        {
            WaitHandle[] handles = { _showEvent, _stopListening };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                onShowRequested();
            }
        })
        { Name = "SingleInstanceListener", IsBackground = true };
        _listener.Start();
    }

    public void Dispose()
    {
        _stopListening.Set();
        _listener?.Join(1000);
        if (_owned)
        {
            _owned = false;
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 소유 스레드가 아니면 해제할 수 없다. 프로세스가 끝나면 OS가 정리한다.
            }
        }
        _mutex.Dispose();
        _showEvent.Dispose();
        _stopListening.Dispose();
    }
}

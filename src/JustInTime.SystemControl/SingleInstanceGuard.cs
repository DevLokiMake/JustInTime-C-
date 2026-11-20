namespace JustInTime.SystemControl;

/// <summary>
/// Защита от второго экземпляра окна: имя мьютекса действует в пределах сеанса Windows текущего пользователя.
/// Второй запуск будит первый (событие) и закрывается. Фоновый режим --run-due этой защитой не пользуется.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private readonly CancellationTokenSource _stop = new();
    private bool _owned;
    private Thread? _listener;

    public SingleInstanceGuard(string name = "JustInTime.App")
    {
        _mutex = new Mutex(initiallyOwned: false, $@"Local\{name}.Instance");
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{name}.Activate");
    }

    /// <summary>True, если это первый экземпляр. Вызывать из потока, который потом вызовет Dispose (для WPF это главный поток).</summary>
    public bool TryAcquire()
    {
        try
        {
            _owned = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // Прошлый экземпляр упал, не освободив мьютекс: теперь он наш.
            _owned = true;
        }

        return _owned;
    }

    /// <summary>Просит уже запущенный экземпляр показать окно.</summary>
    public void SignalExisting() => _activate.Set();

    /// <summary>Первый экземпляр слушает просьбы показать окно. Обработчик вызывается в фоновом потоке.</summary>
    public void StartListening(Action onActivate)
    {
        ArgumentNullException.ThrowIfNull(onActivate);

        _listener = new Thread(() =>
        {
            var handles = new[] { _activate, _stop.Token.WaitHandle };

            while (WaitHandle.WaitAny(handles) == 0)
            {
                onActivate();
            }
        })
        {
            IsBackground = true,
            Name = "JustInTime.SingleInstance"
        };

        _listener.Start();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.Join(TimeSpan.FromSeconds(2));

        if (_owned)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Освобождаем не из того потока: при выходе процесса систему это не задерживает, мьютекс освободит Windows.
            }
        }

        _mutex.Dispose();
        _activate.Dispose();
        _stop.Dispose();
    }
}

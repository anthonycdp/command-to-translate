using System.Windows.Forms;
using CommandToTranslate.Core;
using CommandToTranslate.Hooks;
using CommandToTranslate.Native;
using CommandToTranslate.Services;
using Xunit;

namespace CommandToTranslate.Tests;

public class OnDemandTranslationCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_TranslatesSelection_AndRestoresClipboard()
    {
        var state = CreateState();
        var clipboard = new FakeClipboardService("keep me");
        var inputDispatcher = new FakeInputDispatcher();
        var adapter = new FakeAdapter("Generic")
        {
            CanHandleResult = true
        };
        adapter.OnCopySelectionAsync = () =>
        {
            clipboard.CurrentText = "eu quero dormir";
            return Task.CompletedTask;
        };
        adapter.OnReplaceSelectionAsync = () =>
        {
            adapter.ReplacedText = clipboard.CurrentText;
            return Task.CompletedTask;
        };
        var coordinator = CreateCoordinator(
            state,
            clipboard,
            inputDispatcher,
            new FakeTranslator("I want to sleep"),
            new FakeFocusContextService(new FocusedContext((IntPtr)42, "notepad", "Notepad", false)),
            adapter);

        var result = await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("I want to sleep", adapter.ReplacedText);
        Assert.Equal("keep me", clipboard.CurrentText);
        Assert.Equal(0, adapter.SelectSourceCalls);
        Assert.Equal(1, adapter.CopySelectionCalls);
        Assert.Equal(1, adapter.ReplaceSelectionCalls);
        Assert.True(inputDispatcher.CallCount >= 2);
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToCursorBackward_WhenNoSelectionExists()
    {
        var state = CreateState();
        var clipboard = new FakeClipboardService("keep me");
        var inputDispatcher = new FakeInputDispatcher();
        var adapter = new FakeAdapter("Generic")
        {
            CanHandleResult = true
        };
        adapter.OnCopySelectionAsync = () =>
        {
            if (adapter.CopySelectionCalls > 1)
                clipboard.CurrentText = "eu quero comer queijo";

            return Task.CompletedTask;
        };
        var coordinator = CreateCoordinator(
            state,
            clipboard,
            inputDispatcher,
            new FakeTranslator("I want to eat cheese"),
            new FakeFocusContextService(new FocusedContext((IntPtr)42, "notepad", "Notepad", false)),
            adapter);

        var result = await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("I want to eat cheese", adapter.ReplacedText);
        Assert.Equal(1, adapter.SelectSourceCalls);
        Assert.Equal(2, adapter.CopySelectionCalls);
    }

    [Fact]
    public async Task ExecuteAsync_TriesOtherAdapters_WhenPreferredAdapterCapturesNothing()
    {
        var state = CreateState();
        var clipboard = new FakeClipboardService("keep me");
        var inputDispatcher = new FakeInputDispatcher();
        var preferredAdapter = new FakeAdapter("Generic")
        {
            CanHandleResult = true
        };
        preferredAdapter.OnCopySelectionAsync = () => Task.CompletedTask;

        var fallbackAdapter = new FakeAdapter("WindowsTerminal")
        {
            CanHandleResult = false
        };
        fallbackAdapter.OnCopySelectionAsync = () =>
        {
            if (fallbackAdapter.CopySelectionCalls > 1)
                clipboard.CurrentText = "vamos dormir cedo";

            return Task.CompletedTask;
        };

        var coordinator = CreateCoordinator(
            state,
            clipboard,
            inputDispatcher,
            new FakeTranslator("let's sleep early"),
            new FakeFocusContextService(new FocusedContext((IntPtr)42, "powershell", "PseudoConsoleWindow", false)),
            preferredAdapter,
            fallbackAdapter);

        var result = await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, preferredAdapter.CopySelectionCalls);
        Assert.Equal(2, fallbackAdapter.CopySelectionCalls);
        Assert.Equal("let's sleep early", fallbackAdapter.ReplacedText);
    }

    [Fact]
    public async Task ExecuteAsync_UsesKeystrokeBufferForElectronTerminalHosts()
    {
        using var bufferManager = new BufferManager();
        foreach (var character in "ola mundo")
        {
            var eventType = character == ' ' ? KbEventType.Space : KbEventType.Char;
            char? value = eventType == KbEventType.Char ? character : null;
            bufferManager.ProcessEvent(new KbEvent(value, eventType, (IntPtr)42, false));
        }

        var adapter = new FakeAdapter("ElectronTerminal")
        {
            CanHandleResult = true,
            SkipPreSelectionCopy = true,
            UsesKeystrokeBuffer = true
        };

        var result = await CreateCoordinator(
            CreateState(),
            new FakeClipboardService("keep me"),
            new FakeInputDispatcher(),
            new FakeTranslator("hello world"),
            new FakeFocusContextService(new FocusedContext((IntPtr)42, "code", "Chrome_WidgetWin_1", false)),
            bufferManager,
            adapter)
            .ExecuteAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("hello world", adapter.ReplacedText);
        Assert.Equal(0, adapter.CopySelectionCalls);
        Assert.Equal(0, adapter.SelectSourceCalls);
        Assert.Equal(string.Empty, bufferManager.CurrentPhrase);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenApplicationIsDisabled()
    {
        var state = CreateState();
        state.IsPaused = true;
        var translator = new FakeTranslator("unused");
        var coordinator = CreateCoordinator(
            state,
            new FakeClipboardService("keep me"),
            new FakeInputDispatcher(),
            translator,
            new FakeFocusContextService(new FocusedContext((IntPtr)42, "notepad", "Notepad", false)),
            new FakeAdapter("Generic") { CanHandleResult = true });

        var result = await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("disabled", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, translator.TranslateCalls);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_ForPasswordField()
    {
        var coordinator = CreateCoordinator(
            CreateState(),
            new FakeClipboardService("keep me"),
            new FakeInputDispatcher(),
            new FakeTranslator("unused"),
            new FakeFocusContextService(new FocusedContext((IntPtr)42, "notepad", "Edit", true)),
            new FakeAdapter("Generic") { CanHandleResult = true });

        var result = await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("protected", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_ForUnsupportedHost()
    {
        var coordinator = CreateCoordinator(
            CreateState(),
            new FakeClipboardService("keep me"),
            new FakeInputDispatcher(),
            new FakeTranslator("unused"),
            new FakeFocusContextService(new FocusedContext((IntPtr)42, "custom-app", "CustomClass", false)),
            new FakeAdapter("Console") { CanHandleResult = false });

        var result = await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("No text", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenCopyProducesNoText()
    {
        var clipboard = new FakeClipboardService("keep me");
        var adapter = new FakeAdapter("Generic")
        {
            CanHandleResult = true,
            OnCopySelectionAsync = () => Task.CompletedTask
        };
        var translator = new FakeTranslator("unused");
        var coordinator = CreateCoordinator(
            CreateState(),
            clipboard,
            new FakeInputDispatcher(),
            translator,
            new FakeFocusContextService(new FocusedContext((IntPtr)42, "notepad", "Notepad", false)),
            adapter);

        var result = await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("No text", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, translator.TranslateCalls);
        Assert.Equal("keep me", clipboard.CurrentText);
        Assert.Equal(2, adapter.CopySelectionCalls);
    }

    [Fact]
    public void ProcessMessage_RaisesEvent_WithoutTogglingApplicationState()
    {
        var state = CreateState();
        state.IsPaused = false;

        using var manager = new HotkeyManager(state, IntPtr.Zero);
        var eventCount = 0;
        manager.HotkeyPressed += (_, _) => eventCount++;

        var handled = manager.ProcessMessage(Win32.WM_HOTKEY, (IntPtr)1);

        Assert.True(handled);
        Assert.Equal(1, eventCount);
        Assert.False(state.IsPaused);
    }

    private static OnDemandTranslationCoordinator CreateCoordinator(
        AppState state,
        FakeClipboardService clipboard,
        FakeInputDispatcher inputDispatcher,
        FakeTranslator translator,
        FakeFocusContextService focusContextService,
        BufferManager? bufferManager,
        params FakeAdapter[] adapters)
    {
        return new OnDemandTranslationCoordinator(
            state,
            translator,
            focusContextService,
            clipboard,
            inputDispatcher,
            adapters,
            bufferManager);
    }

    private static OnDemandTranslationCoordinator CreateCoordinator(
        AppState state,
        FakeClipboardService clipboard,
        FakeInputDispatcher inputDispatcher,
        FakeTranslator translator,
        FakeFocusContextService focusContextService,
        params FakeAdapter[] adapters)
    {
        return CreateCoordinator(
            state,
            clipboard,
            inputDispatcher,
            translator,
            focusContextService,
            bufferManager: null,
            adapters);
    }

    private static AppState CreateState()
    {
        return new AppState
        {
            Config = new AppConfig
            {
                Behavior = new BehaviorConfig
                {
                    ShortcutStepDelayMs = 0,
                    ClipboardTimeoutMs = 100,
                    HostSettleDelayMs = 0
                }
            }
        };
    }

    private sealed class FakeTranslator : ITextTranslator
    {
        private readonly string? _translatedText;

        public FakeTranslator(string? translatedText)
        {
            _translatedText = translatedText;
        }

        public int TranslateCalls { get; private set; }

        public Task<string?> TranslateAsync(string text, CancellationToken ct)
        {
            TranslateCalls++;
            return Task.FromResult(_translatedText);
        }

        public Task<(bool IsHealthy, string? ErrorMessage)> CheckHealthAsync()
        {
            return Task.FromResult<(bool, string?)>((true, null));
        }
    }

    private sealed class FakeFocusContextService : IFocusContextService
    {
        private readonly FocusedContext _context;

        public FakeFocusContextService(FocusedContext context)
        {
            _context = context;
        }

        public FocusedContext GetFocusedContext() => _context;
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public FakeClipboardService(string? initialText)
        {
            CurrentText = initialText;
        }

        public string? CurrentText { get; set; }

        public ClipboardSnapshot CaptureSnapshot()
        {
            IDataObject? dataObject = CurrentText is null ? null : new DataObject(DataFormats.Text, CurrentText);
            return new ClipboardSnapshot(dataObject, CurrentText is not null);
        }

        public string? GetText() => CurrentText;

        public void SetText(string text)
        {
            CurrentText = text;
        }

        public Task<string?> WaitForCopiedTextAsync(string? previousText, TimeSpan timeout, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(CurrentText) &&
                !string.Equals(CurrentText, previousText, StringComparison.Ordinal))
            {
                return Task.FromResult<string?>(CurrentText);
            }

            return Task.FromResult<string?>(null);
        }

        public void Restore(ClipboardSnapshot snapshot)
        {
            CurrentText = snapshot.HadData && snapshot.DataObject?.GetData(DataFormats.Text) is string text
                ? text
                : null;
        }
    }

    private sealed class FakeInputDispatcher : IInputDispatcher
    {
        public int CallCount { get; private set; }

        public Task SendKeyAsync(ushort virtualKey, CancellationToken ct)
        {
            CallCount++;
            return Task.CompletedTask;
        }

        public Task SendChordAsync(IReadOnlyList<ushort> virtualKeys, CancellationToken ct)
        {
            CallCount++;
            return Task.CompletedTask;
        }

        public Task SendRepeatedKeyAsync(ushort virtualKey, int count, CancellationToken ct)
        {
            CallCount++;
            return Task.CompletedTask;
        }

        public Task TypeTextAsync(string text, CancellationToken ct)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAdapter : ITranslationTargetAdapter
    {
        public FakeAdapter(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public bool CanHandleResult { get; set; }
        public bool SkipPreSelectionCopy { get; set; }
        public bool UsesKeystrokeBuffer { get; set; }
        public int SelectSourceCalls { get; private set; }
        public int CopySelectionCalls { get; private set; }
        public int ReplaceSelectionCalls { get; private set; }
        public string? ReplacedText { get; set; }
        public Func<Task>? OnCopySelectionAsync { get; set; }
        public Func<Task>? OnReplaceSelectionAsync { get; set; }

        public bool CanHandle(FocusedContext context) => CanHandleResult;

        public Task SelectSourceAsync(IInputDispatcher inputDispatcher, CancellationToken ct)
        {
            SelectSourceCalls++;
            return inputDispatcher.SendChordAsync([0x11, 0x10, 0x24], ct);
        }

        public async Task CopySelectionAsync(IInputDispatcher inputDispatcher, CancellationToken ct)
        {
            CopySelectionCalls++;
            await inputDispatcher.SendChordAsync([0x11, 0x43], ct);

            if (OnCopySelectionAsync is not null)
                await OnCopySelectionAsync();
        }

        public async Task ReplaceSelectionAsync(
            IInputDispatcher inputDispatcher,
            string sourceText,
            string translatedText,
            bool usedCursorFallback,
            CancellationToken ct)
        {
            ReplaceSelectionCalls++;
            ReplacedText = translatedText;
            await inputDispatcher.TypeTextAsync(translatedText, ct);

            if (OnReplaceSelectionAsync is not null)
                await OnReplaceSelectionAsync();
        }
    }
}

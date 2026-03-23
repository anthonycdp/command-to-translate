using System.Windows.Forms;
using CommandToTranslate.Core;
using CommandToTranslate.Services;
using Xunit;

namespace CommandToTranslate.Tests;

public class HotkeyBindingTests
{
    [Fact]
    public void TryCreate_NormalizesBinding_AndFormatsLabel()
    {
        var created = HotkeyBindingParser.TryCreate(
            ["control", "shift", "control"],
            "f2",
            out var binding);

        Assert.True(created);
        Assert.Equal(["Ctrl", "Shift"], binding.Modifiers);
        Assert.Equal("F2", binding.Key);
        Assert.Equal("Ctrl + Shift + F2", binding.Label);
    }

    [Fact]
    public void TryCreate_RejectsBindingWithoutModifier()
    {
        var created = HotkeyBindingParser.TryCreate([], "T", out var binding);

        Assert.False(created);
        Assert.Equal(HotkeyBindingParser.DefaultBinding, binding);
    }

    [Fact]
    public void TryCreateFromKeyEvent_CapturesModifierAndKey()
    {
        var created = HotkeyBindingParser.TryCreateFromKeyEvent(
            new KeyEventArgs(Keys.Control | Keys.Alt | Keys.D1),
            out var binding);

        Assert.True(created);
        Assert.Equal(["Ctrl", "Alt"], binding.Modifiers);
        Assert.Equal("1", binding.Key);
    }

    [Fact]
    public void HotkeyConfig_Normalize_FallsBackToDefault_WhenInvalid()
    {
        var config = new HotkeyConfig
        {
            Modifiers = [],
            Key = "T"
        };

        config.Normalize();

        Assert.Equal(["Ctrl", "Shift"], config.Modifiers);
        Assert.Equal("T", config.Key);
    }

    [Fact]
    public void HotkeyRebindingService_RestoresPreviousBinding_WhenRegistrationFails()
    {
        var state = new AppState
        {
            Config = new AppConfig()
        };

        var registrar = new FakeHotkeyRegistrar(false, true);
        var service = new HotkeyRebindingService(state, registrar);
        var newBinding = new HotkeyBinding(["Ctrl", "Alt"], "F2");

        var result = service.Apply(newBinding);

        Assert.False(result.Success);
        Assert.Equal(2, registrar.RegisterCalls);
        Assert.Equal(1, registrar.UnregisterCalls);
        Assert.Equal("Ctrl + Shift + T", state.ActiveHotkeyBinding.Label);
        Assert.Equal(state.ActiveHotkeyBinding, result.ActiveBinding);
    }

    private sealed class FakeHotkeyRegistrar : IHotkeyRegistrar
    {
        private readonly Queue<bool> _registerResults;

        public FakeHotkeyRegistrar(params bool[] registerResults)
        {
            _registerResults = new Queue<bool>(registerResults);
        }

        public int RegisterCalls { get; private set; }
        public int UnregisterCalls { get; private set; }

        public bool Register()
        {
            RegisterCalls++;
            return _registerResults.Count == 0 || _registerResults.Dequeue();
        }

        public void Unregister()
        {
            UnregisterCalls++;
        }
    }
}

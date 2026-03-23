using CommandToTranslate.Core;
using CommandToTranslate.Hooks;

namespace CommandToTranslate.Services;

public sealed record HotkeyRebindingResult(bool Success, string Message, HotkeyBinding ActiveBinding);

public interface IHotkeyRegistrar
{
    bool Register();
    void Unregister();
}

/// <summary>
/// Applies a hotkey change with rollback if Windows rejects the new registration.
/// </summary>
public sealed class HotkeyRebindingService
{
    private readonly AppState _state;
    private readonly IHotkeyRegistrar _hotkeyRegistrar;

    public HotkeyRebindingService(AppState state, IHotkeyRegistrar hotkeyRegistrar)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _hotkeyRegistrar = hotkeyRegistrar ?? throw new ArgumentNullException(nameof(hotkeyRegistrar));
    }

    public HotkeyRebindingResult Apply(HotkeyBinding newBinding)
    {
        ArgumentNullException.ThrowIfNull(newBinding);

        var previousBinding = _state.ActiveHotkeyBinding;
        if (previousBinding == newBinding)
        {
            return new HotkeyRebindingResult(
                true,
                $"Hotkey unchanged: {newBinding.Label}",
                previousBinding);
        }

        _hotkeyRegistrar.Unregister();
        _state.SetActiveHotkeyBinding(newBinding);

        if (_hotkeyRegistrar.Register())
        {
            return new HotkeyRebindingResult(
                true,
                $"Hotkey updated to {newBinding.Label}",
                newBinding);
        }

        _state.SetActiveHotkeyBinding(previousBinding);
        var restored = _hotkeyRegistrar.Register();
        var message = restored
            ? $"Failed to register {newBinding.Label}. Restored {previousBinding.Label}."
            : $"Failed to register {newBinding.Label}, and the previous hotkey could not be restored automatically.";

        return new HotkeyRebindingResult(false, message, _state.ActiveHotkeyBinding);
    }
}

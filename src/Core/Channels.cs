// src/Core/Channels.cs
namespace RealTranslate.Core;

/// <summary>Types of keyboard events we care about</summary>
public enum KbEventType
{
    Char,
    Space,
    Punctuation,
    Enter,
    Backspace
}

/// <summary>Represents a single keyboard event</summary>
public record KbEvent(
    char? Character,        // null for non-printable keys
    KbEventType Type,
    IntPtr WindowHandle,    // for context detection
    bool IsPasswordField    // true if focused field is password
);

/// <summary>Translation mode determines how the text is processed</summary>
public enum TranslationMode
{
    WordOnly,           // Single word, immediate
    PhraseWithContext   // Full phrase for contextual refinement
}

/// <summary>Task sent to translation service</summary>
public record TranslationTask(
    string Text,
    TranslationMode Mode,
    int CharactersToDelete,
    CancellationToken CancellationToken
);

/// <summary>Result from translation, sent to injector</summary>
public record InjectionTask(
    string TranslatedText,
    int CharactersToDelete,
    bool IsRefinement
);

/// <summary>Channel provider for inter-component communication</summary>
public static class AppChannels
{
    public static System.Threading.Channels.Channel<KbEvent> KeyboardEvents { get; }
        = System.Threading.Channels.Channel.CreateUnbounded<KbEvent>();

    public static System.Threading.Channels.Channel<TranslationTask> TranslationTasks { get; }
        = System.Threading.Channels.Channel.CreateUnbounded<TranslationTask>();

    public static System.Threading.Channels.Channel<InjectionTask> InjectionTasks { get; }
        = System.Threading.Channels.Channel.CreateUnbounded<InjectionTask>();
}

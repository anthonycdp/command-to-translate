using CommandToTranslate.Native;
using CommandToTranslate.Services;
using Xunit;

namespace CommandToTranslate.Tests;

public class InputDispatcherTests
{
    [Fact]
    public void BuildTextInputSequence_ConvertsAllLineBreakStylesToEnter()
    {
        var steps = InputDispatcher.BuildTextInputSequence("Line 1\r\nLine 2\nLine 3\rLine 4");

        Assert.Collection(
            steps,
            step => AssertCharacter(step, 'L'),
            step => AssertCharacter(step, 'i'),
            step => AssertCharacter(step, 'n'),
            step => AssertCharacter(step, 'e'),
            step => AssertCharacter(step, ' '),
            step => AssertCharacter(step, '1'),
            step => AssertEnter(step),
            step => AssertCharacter(step, 'L'),
            step => AssertCharacter(step, 'i'),
            step => AssertCharacter(step, 'n'),
            step => AssertCharacter(step, 'e'),
            step => AssertCharacter(step, ' '),
            step => AssertCharacter(step, '2'),
            step => AssertEnter(step),
            step => AssertCharacter(step, 'L'),
            step => AssertCharacter(step, 'i'),
            step => AssertCharacter(step, 'n'),
            step => AssertCharacter(step, 'e'),
            step => AssertCharacter(step, ' '),
            step => AssertCharacter(step, '3'),
            step => AssertEnter(step),
            step => AssertCharacter(step, 'L'),
            step => AssertCharacter(step, 'i'),
            step => AssertCharacter(step, 'n'),
            step => AssertCharacter(step, 'e'),
            step => AssertCharacter(step, ' '),
            step => AssertCharacter(step, '4'));
    }

    [Fact]
    public void BuildTextInputSequence_PreservesRegularCharacters()
    {
        var steps = InputDispatcher.BuildTextInputSequence("A B.");

        Assert.Collection(
            steps,
            step => AssertCharacter(step, 'A'),
            step => AssertCharacter(step, ' '),
            step => AssertCharacter(step, 'B'),
            step => AssertCharacter(step, '.'));
    }

    private static void AssertCharacter(TextInputStep step, char expectedCharacter)
    {
        Assert.Equal(TextInputKind.UnicodeCharacter, step.Kind);
        Assert.Equal(expectedCharacter, step.Character);
        Assert.Equal((ushort)0, step.VirtualKey);
    }

    private static void AssertEnter(TextInputStep step)
    {
        Assert.Equal(TextInputKind.VirtualKey, step.Kind);
        Assert.Equal(Win32.VK_RETURN, step.VirtualKey);
        Assert.Equal('\0', step.Character);
    }
}

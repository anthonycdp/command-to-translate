using System.Windows.Forms;

namespace CommandToTranslate.Services;

public interface IClipboardService
{
    ClipboardSnapshot CaptureSnapshot();
    string? GetText();
    void SetText(string text);
    Task<string?> WaitForCopiedTextAsync(string? previousText, TimeSpan timeout, CancellationToken ct);
    void Restore(ClipboardSnapshot snapshot);
}

public sealed record ClipboardSnapshot(IDataObject? DataObject, bool HadData);

public sealed class ClipboardService : IClipboardService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    public ClipboardSnapshot CaptureSnapshot()
    {
        try
        {
            if (!Clipboard.ContainsData(DataFormats.Text) && Clipboard.GetDataObject() is null)
                return new ClipboardSnapshot(null, false);

            return new ClipboardSnapshot(Clipboard.GetDataObject(), true);
        }
        catch
        {
            return new ClipboardSnapshot(null, false);
        }
    }

    public string? GetText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            return null;
        }
    }

    public void SetText(string text)
    {
        Clipboard.SetText(text ?? string.Empty);
    }

    public async Task<string?> WaitForCopiedTextAsync(string? previousText, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var currentText = GetText();
            if (!string.IsNullOrWhiteSpace(currentText) &&
                !string.Equals(currentText, previousText, StringComparison.Ordinal))
            {
                return currentText;
            }

            await Task.Delay(PollInterval, ct);
        }

        var fallbackText = GetText();
        if (string.IsNullOrWhiteSpace(fallbackText) ||
            string.Equals(fallbackText, previousText, StringComparison.Ordinal))
        {
            return null;
        }

        return fallbackText;
    }

    public void Restore(ClipboardSnapshot snapshot)
    {
        try
        {
            if (!snapshot.HadData)
            {
                Clipboard.Clear();
                return;
            }

            if (snapshot.DataObject != null)
                Clipboard.SetDataObject(snapshot.DataObject, true);
        }
        catch
        {
        }
    }
}

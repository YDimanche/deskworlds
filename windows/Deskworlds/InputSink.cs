namespace Deskworlds;

/// <summary>
/// A hidden top-level window that raw input is delivered to: the host watches mouse
/// button presses without touching the input stream, so the shell keeps every click.
/// </summary>
internal sealed class InputSink : NativeWindow
{
    private readonly DeskworldsApp app;

    public InputSink(DeskworldsApp app)
    {
        this.app = app;
        CreateHandle(new CreateParams { Caption = "DeskworldsInputSink" });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_INPUT) app.OnRawInput(m.LParam);
        base.WndProc(ref m);
    }
}

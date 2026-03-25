namespace Clipt.Services;

public sealed class SecondInstanceActivateEventArgs : EventArgs
{
    public int ModeWParam { get; }

    public SecondInstanceActivateEventArgs(int modeWParam) => ModeWParam = modeWParam;
}

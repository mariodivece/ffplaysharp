namespace Unosquare.FFplaySharp
{
    public enum ThreeState
    {
        Off = 0,
        On = 1,
        Auto = -1
    }

    public enum FlowResult
    {
        Next,
        LoopBreak,
        LoopContinue,
        Fail
    }
}

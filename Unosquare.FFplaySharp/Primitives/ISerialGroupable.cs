namespace Unosquare.FFplaySharp.Primitives;

public interface ISerialGroupable
{
    /// <summary>
    /// In ffplay.c, this is referred to as a field called serial,
    /// which is just a group id or index by which clocks and packets are
    /// designated as part of a contiguous set. For example, when seeking or
    /// switching streams, the group index must match so that packets being
    /// decoded and frames being presented, all belong to the same group.
    /// Otherwise, it does not make sense to keep processing packets and frames
    /// that belong to an now outdated group.
    /// </summary>
    public int GroupIndex { get; }
}

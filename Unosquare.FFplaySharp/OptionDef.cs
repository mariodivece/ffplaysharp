namespace Unosquare.FFplaySharp;

public class OptionDef<T>
{
    public OptionDef(string name, OptionUsage flags, Action<T, string> apply, string help, string argumentName)
    {
        Name = name;
        Flags = flags;
        Apply = apply;
        Help = help;
        ArgumentName = argumentName;
    }

    public OptionDef(string name, OptionUsage flags, Action<T, string> apply, string help)
        : this(name, flags, apply, help, name)
    {
        // placeholder
    }

    public string Name { get; set; }

    public OptionUsage Flags { get; set; }

    public Action<T, string> Apply { get; set; }

    public string Help { get; set; }

    public string ArgumentName { get; set; }
}

/// <summary>
/// Defines the usage attribute for <see cref="OptionDef{T}"/>
/// </summary>
[Flags]
public enum OptionUsage
{
    /// <summary>
    /// Port of HAS_ARG
    /// </summary>
    NoParameters = 0x0001,

    /// <summary>
    /// Prot of OPT_BOOL
    /// </summary>
    IsBoolean = 0x0002,

    /// <summary>
    /// Port of OPT_EXPERT
    /// </summary>
    ForExpertMode = 0x0004,

    /// <summary>
    /// Port of OPT_STRING
    /// </summary>
    IsString = 0x0008,

    /// <summary>
    /// Port of OPT_VIDEO
    /// </summary>
    ForVideoStream = 0x0010,

    /// <summary>
    /// Port of OPT_AUDIO
    /// </summary>
    ForAudioStream = 0x0020,

    /// <summary>
    /// Port of OPT_INT
    /// </summary>
    IsInt = 0x0080,

    /// <summary>
    /// Port of OPT_FLOAT
    /// </summary>
    IsFloat = 0x0100,

    /// <summary>
    /// Port of OPT_SUBTITLE
    /// </summary>
    ForSubtitleStream = 0x0200,

    /// <summary>
    /// Port of OPT_INT64
    /// </summary>
    IsLong = 0x0400,

    /// <summary>
    /// Port of OPT_EXIT
    /// </summary>
    ExitMode = 0x0800,

    /// <summary>
    /// Port of OPT_DATA
    /// </summary>
    ForDataStream = 0x1000,

    /// <summary>
    /// Port of OPT_PERFILE
    /// </summary>
    PerFile = 0x2000,

    /// <summary>
    /// Port of OPT_OFFSET
    /// </summary>
    IsOffset = 0x4000,

    /// <summary>
    /// Port of OPT_SPEC
    /// </summary>
    IsSpec = 0x8000,

    /// <summary>
    /// Port of OPT_TIME
    /// </summary>
    IsTime = 0x10000,

    /// <summary>
    /// Port of OPT_DOUBLE
    /// </summary>
    IsDouble = 0x20000,

    /// <summary>
    /// Port of OPT_INPUT
    /// </summary>
    ForInputStream = 0x40000,

    /// <summary>
    /// Port of OPT_OUTPUT
    /// </summary>
    ForOutputStream = 0x80000,
}

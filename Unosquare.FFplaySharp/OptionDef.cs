namespace Unosquare.FFplaySharp;

public class OptionDef<T>
{
    public OptionDef(string name, OptionFlags flags, Action<T, string> apply, string help, string argumentName)
    {
        Name = name;
        Flags = flags;
        Apply = apply;
        Help = help;
        ArgumentName = argumentName;
    }

    public OptionDef(string name, OptionFlags flags, Action<T, string> apply, string help)
        : this(name, flags, apply, help, name)
    {
        // placeholder
    }

    public string Name { get; set; }

    public OptionFlags Flags { get; set; }

    public Action<T, string> Apply { get; set; }

    public string Help { get; set; }

    public string ArgumentName { get; set; }
}

[Flags]
public enum OptionFlags
{
    HAS_ARG = 0x0001,
    OPT_BOOL = 0x0002,
    OPT_EXPERT = 0x0004,
    OPT_STRING = 0x0008,
    OPT_VIDEO = 0x0010,
    OPT_AUDIO = 0x0020,
    OPT_INT = 0x0080,
    OPT_FLOAT = 0x0100,
    OPT_SUBTITLE = 0x0200,
    OPT_INT64 = 0x0400,
    OPT_EXIT = 0x0800,
    OPT_DATA = 0x1000,
    OPT_PERFILE = 0x2000,
    OPT_OFFSET = 0x4000,
    OPT_SPEC = 0x8000,
    OPT_TIME = 0x10000,
    OPT_DOUBLE = 0x20000,
    OPT_INPUT = 0x40000,
    OPT_OUTPUT = 0x80000,
}

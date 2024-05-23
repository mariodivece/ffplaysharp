﻿namespace Unosquare.FFplaySharp;

public unsafe class ProgramOptions
{
    private static readonly IReadOnlyList<OptionDef<ProgramOptions>> Definitions = new List<OptionDef<ProgramOptions>>
    {
        Option("x", true, "force displayed width", "width", (t, a) =>
            t.WindowWidth = int.TryParse(a, out var v) ? v : t.WindowWidth),
        Option("y", true, "force displayed height", "height", (t, a) =>
            t.WindowHeight = int.TryParse(a, out var v) ? v : t.WindowHeight),
        Option("s", true, "set frame size (WxH or abbreviation)", "size", (t, a) =>
        {
            ("Option -s is deprecated, use -video_size.").LogWarning();
            t.ApplyDefaultOption("video_size", a);
        }),
        Option("fs", false, "force full screen", (t, a) =>
            t.IsFullScreen = true),
        Option("an", false, "disable audio", (t, a) =>
            t.IsAudioDisabled = true),
        Option("vn", false, "disable video", (t, a) =>
            t.IsVideoDisabled = true),
        Option("sn", false, "disable subtitling", (t, a) =>
            t.IsSubtitleDisabled = true),
        Option("ast", true, "select desired audio stream", "stream_specifier", (t, a) =>
            t.WantedStreams[AVMediaType.AVMEDIA_TYPE_AUDIO] = a),
        Option("vst", true, "select desired video stream", "stream_specifier", (t, a) =>
            t.WantedStreams[AVMediaType.AVMEDIA_TYPE_VIDEO] = a),
        Option("sst", true, "select desired subtitle stream", "stream_specifier", (t, a) =>
            t.WantedStreams[AVMediaType.AVMEDIA_TYPE_SUBTITLE] = a),
        Option("ss", true, "seek to a given position in seconds", "pos", (t, a) =>
            t.StartOffset = a.ParseTime()),
        Option("t", true, "play  \"duration\" seconds of audio/video", "duration", (t, a) =>
            t.Duration = a.ParseTime()),
        Option("bytes", true, "seek by bytes 0=off 1=on -1=auto", "val", (t, a) =>
            t.IsByteSeekingEnabled = int.TryParse(a, out var v) ? v.ToThreeState() : t.IsByteSeekingEnabled),
        Option("seek_interval", true, "set seek interval for left/right keys, in seconds", "seconds", (t, a) =>
            t.SeekInterval = int.TryParse(a, out var v) ? v : t.SeekInterval),
        Option("nodisp", false, "disable graphical display", (t, a) =>
            t.IsDisplayDisabled = true),
        Option("noborder", false, "borderless window", (t, a) =>
            t.IsWindowBorderless = true),
        Option("alwaysontop", false, "window always on top", (t, a) =>
            t.IsWindowAlwaysOnTop = true),
        Option("volume", true, "set startup volume 0=min 100=max", "volume", (t, a) =>
            t.StartupVolume = int.TryParse(a, out var v) ? v : t.StartupVolume),
        Option("f", true, "force format", "fmt", (t, a) =>
            t.InputFormat = FFInputFormat.Find(a)),
        Option("pix_fmt", true, "set pixel format", "format", (t, a) =>
        {
            ("Option -pix_fmt is deprecated, use -pixel_format.").LogWarning();
            t.ApplyDefaultOption("pixel_format", a);
        }),
        Option("stats", false, "show status", (t, a) =>
            t.ShowStatus = ThreeState.On),
        Option("nostats", false, "show status", (t, a) =>
            t.ShowStatus = ThreeState.Off),
        Option("fast", false, "non spec compliant optimizations", (t, a) =>
            t.IsFastDecodingEnabled = ThreeState.On),
        Option("genpts", false, "generate pts", (t, a) =>
            t.GeneratePts = true),
        Option("drp", true, "let decoder reorder pts 0=off 1=on -1=auto", (t, a) =>
            t.IsPtsReorderingEnabled = int.TryParse(a, out var v) ? v.ToThreeState() : t.IsPtsReorderingEnabled),
        Option("lowres", true, "set low resolution scaler. Higher 2 means half the resolution. 4 means a quarter", (t, a) =>
            t.LowResolution = int.TryParse(a, out var v) ? v : t.LowResolution),
        Option("sync", true, "set audio-video sync. type (type=audio/video/ext)", "type", (t, a) =>
            t.ClockSyncType = a == "audio" ? ClockSource.Audio : a == "video" ? ClockSource.Video : ClockSource.External),
        Option("autoexit", false, "exit at the end", (t, a) =>
            t.ExitOnFinish = true),
        Option("exitonkeydown", false, "exit on key down", (t, a) =>
            t.ExitOnKeyDown = true),
        Option("exitonmousedown", false, "exit on mouse down", (t, a) =>
            t.ExitOnMouseDown = true),
        Option("loop", true, "set number of times the playback shall be looped", (t, a) =>
            t.LoopCount = int.TryParse(a, out var v) ? v : t.LoopCount),
        Option("framedrop", false, "drop frames when cpu is too slow", (t, a) =>
            t.IsFrameDropEnabled = ThreeState.On),
        Option("noframedrop", false, "do not drop frames when cpu is too slow", (t, a) =>
            t.IsFrameDropEnabled = ThreeState.Off),
        Option("infbuf", false, "don't limit the input buffer size (useful with realtime streams)", (t, a) =>
            t.IsInfiniteBufferEnabled = ThreeState.On),
        Option("noinfbuf", false, "limit the input buffer size (useful with realtime streams)", (t, a) =>
            t.IsInfiniteBufferEnabled = ThreeState.Off),
        Option("window_title", true, "set window title", (t, a) =>
            t.WindowTitle = a),
        Option("left", true, "set the x position for the left of the window", (t, a) =>
            t.WindowLeft = int.TryParse(a, out var v) ? v : t.WindowLeft),
        Option("top", true, "set the y position for the top of the window", (t, a) =>
            t.WindowTop = int.TryParse(a, out var v) ? v : t.WindowTop),
        Option("vf", true, "set video filters", (t, a) =>
            t.VideoFilterGraphs.Add(a)),
        Option("af", true, "set audio filters", (t, a) =>
            t.AudioFilterGraphs = a),
        Option("codec:a", true, "force audio decoder", "acodec", (t, a) =>
            t.AudioForcedCodecName = a),
        Option("codec:v", true, "force video decoder", "scodec", (t, a) =>
            t.VideoForcedCodecName = a),
        Option("codec:s", true, "force subtitle decoder", "vcodec", (t, a) =>
            t.SubtitleForcedCodecName = a),
        Option("autorotate", false, "automatically rotate video", (t, a) =>
            t.IsAutorotateEnabled = true),
        Option("autorotate", false, "prevent automatic video rotation", (t, a) =>
            t.IsAutorotateEnabled = false),
        Option("find_stream_info", false, "read and decode the streams to fill missing information with heuristics", (t, a) =>
            t.IsStreamInfoEnabled = true),
        Option("filter_threads", false, "number of filter threads per graph", (t, a) =>
            t.FilteringThreadCount = int.TryParse(a, out var v) ? v : t.FilteringThreadCount),
        Option("i", true, "read specified file", "input_file", (t, a) =>
            t.InputFileName = a)
    };

    public ProgramOptions()
    {
        // placeholder
    }
    public FFInputFormat? InputFormat { get; set; }

    public string? InputFileName { get; set; }

    public string? WindowTitle { get; set; }

    public int WindowWidth { get; set; }

    public int WindowHeight { get; set; }

    public int? WindowLeft { get; set; }

    public int? WindowTop { get; set; }

    public bool IsFullScreen { get; set; }

    public bool IsAudioDisabled { get; set; }

    public bool IsVideoDisabled { get; set; }

    public bool IsSubtitleDisabled { get; set; }

    public MediaTypeDictionary<string> WantedStreams { get; } = new();

    public ThreeState IsByteSeekingEnabled { get; set; } = ThreeState.Auto;

    public double SeekInterval { get; set; } = 1;

    public bool IsDisplayDisabled { get; set; }

    public bool IsWindowBorderless { get; set; }

    public bool IsWindowAlwaysOnTop { get; set; }

    public int StartupVolume { get; set; } = 100;

    public ThreeState ShowStatus { get; set; } = ThreeState.Auto;

    public ClockSource ClockSyncType { get; set; } = ClockSource.Audio;

    public long StartOffset { get; set; } = ffmpeg.AV_NOPTS_VALUE;

    public long Duration { get; set; } = ffmpeg.AV_NOPTS_VALUE;

    public ThreeState IsFastDecodingEnabled { get; set; } = ThreeState.Off;

    public bool GeneratePts { get; set; }

    public int LowResolution { get; set; }

    public ThreeState IsPtsReorderingEnabled { get; set; } = ThreeState.Auto;

    public bool ExitOnFinish { get; set; }

    public bool ExitOnKeyDown { get; set; }

    public bool ExitOnMouseDown { get; set; }

    public int LoopCount { get; set; } = 1;

    public ThreeState IsFrameDropEnabled { get; set; } = ThreeState.Auto;

    public ThreeState IsInfiniteBufferEnabled { get; set; } = ThreeState.Auto;

    public ShowMode ShowMode { get; set; } = ShowMode.None;

    public string? AudioForcedCodecName { get; set; }

    public string? SubtitleForcedCodecName { get; set; }

    public string? VideoForcedCodecName { get; set; }

    public IList<string> VideoFilterGraphs { get; } = new List<string>(32);

    public string? AudioFilterGraphs { get; set; }

    public bool IsAutorotateEnabled { get; set; } = true;

    public bool IsStreamInfoEnabled { get; set; } = true;

    public int FilteringThreadCount { get; set; }

    public int VideoMaxPixelWidth { get; set; } = -1;

    public int VideoMaxPixelHeight { get; set; } = -1;

    // Internal option dictionaries
    public StringDictionary ScalerOptions { get; } = new();

    public StringDictionary ResamplerOptions { get; } = new();

    public StringDictionary FormatOptions { get; } = new();

    public StringDictionary CodecOptions { get; } = new();

    public static ProgramOptions FromCommandLineArguments(string[] args)
    {
        // TODO: cmdutils.c
        // https://github.com/FFmpeg/FFmpeg/blob/master/fftools/cmdutils.c

        var options = new ProgramOptions();
        var arguments = args.ToList();

        for (var i = 0; i < arguments.Count; i++)
        {
            var argumentName = arguments[i];

            // Handle first argument as being the input file name by default
            if (i == 0)
            {
                if (argumentName == "-")
                {
                    options.InputFileName = "-";
                    continue;
                }
                else if (!argumentName.StartsWith('-'))
                {
                    options.InputFileName = argumentName;
                    continue;
                }
            }

            if (argumentName.StartsWith('-'))
                argumentName = argumentName.TrimStart('-').ToLowerInvariant();
            else
                continue;

            var argumentValue = string.Empty;
            var nextItem = i + 1 < arguments.Count ? arguments[i + 1] : null;

            var definition = Definitions.Where(d => d.Name == argumentName || d.ArgumentName == argumentName).FirstOrDefault();

            // handle catch-all option
            if (definition is null)
            {
                if (nextItem is not null)
                {
                    options.ApplyDefaultOption(argumentName, nextItem);
                    i++;
                }

                continue;
            }

            if (definition.Flags.HasFlag(OptionUsage.NoParameters))
            {
                argumentValue = nextItem;
                i++;
            }

            definition.Apply(options, argumentValue);
        }

        if (options.InputFileName == "-")
            options.InputFileName = "pipe:";

        return options;
    }

    private static OptionDef<ProgramOptions> Option(string name, OptionUsage flags, string help, string argName, Action<ProgramOptions, string> apply)
        => new(name, flags, apply, help, argName);

    private static OptionDef<ProgramOptions> Option(string name, bool hasArgument, string help, string argName, Action<ProgramOptions, string> apply)
        => new(name, hasArgument ? OptionUsage.NoParameters : OptionUsage.IsBoolean, apply, help, argName);

    private static OptionDef<ProgramOptions> Option(string name, OptionUsage flags, string help, Action<ProgramOptions, string> apply)
        => new(name, flags, apply, help);

    private static OptionDef<ProgramOptions> Option(string name, bool hasArgument, string help, Action<ProgramOptions, string> apply)
        => new(name, hasArgument ? OptionUsage.NoParameters : OptionUsage.IsBoolean, apply, help);

    /// <summary>
    /// Port of opt_default
    /// </summary>
    /// <param name="optionName"></param>
    /// <param name="optionValue"></param>
    /// <returns></returns>
    private unsafe int ApplyDefaultOption(string optionName, string optionValue)
    {
        const char Semicolon = ':';
        const int SearchFlags = ffmpeg.AV_OPT_SEARCH_CHILDREN | ffmpeg.AV_OPT_SEARCH_FAKE_OBJ;

        if (optionName == "debug" || optionName == "fdebug")
            FFLog.Level = ffmpeg.AV_LOG_DEBUG;

        // Example: codec:a:1 ac3
        var strippedOptionName = optionName.Contains(Semicolon, StringComparison.Ordinal)
            ? optionName[..optionName.IndexOf(Semicolon, StringComparison.Ordinal)]
            : optionName;

        FFOption? o = default;
        bool isConsumed = false;

        if ((o = FFMediaClass.Codec.FindOption(strippedOptionName, default, SearchFlags)).IsValid() || (
            (optionName.StartsWith('a') || optionName.StartsWith('v') || optionName.StartsWith('s')) &&
            (o = FFMediaClass.Codec.FindOption(optionName[1..])).IsValid()))
        {
            CodecOptions.Set(o, optionName, optionValue);
            isConsumed = true;
        }

        if ((o = FFMediaClass.Format.FindOption(optionName, SearchFlags)).IsValid())
        {
            FormatOptions.Set(o!, optionName, optionValue);

            if (isConsumed)
                ($"Routing option {optionName} to both codec and muxer layer.").LogVerbose();

            isConsumed = true;
        }

        if (!isConsumed && (o = FFMediaClass.Format.FindOption(optionName, 0, SearchFlags)).IsValid())
        {

            var dummyScaler = new RescalerContext();
            var setResult = dummyScaler.SetOption(optionName, optionValue);
            dummyScaler.Dispose();

            var invalidOptions = new[] { "srcw", "srch", "dstw", "dsth", "src_format", "dst_format" };

            if (invalidOptions.Contains(optionName))
            {
                ("Directly using swscale dimensions/format options is not supported, please use the -s or -pix_fmt options.").LogError();
                return ffmpeg.AVERROR(ffmpeg.EINVAL);
            }

            if (setResult < 0)
            {
                ($"Error setting option {optionName}.").LogError();
                return setResult;
            }

            ScalerOptions.Set(o, optionName, optionValue);
            isConsumed = true;
        }

        if (!isConsumed && (o = FFMediaClass.Resampler.FindOption(optionName, 0, SearchFlags)).IsValid())
        {
            var dummyResampler = new ResamplerContext();
            var setResult = dummyResampler.SetOption(optionName, optionValue);
            dummyResampler.Dispose();

            if (setResult < 0)
            {
                ($"Error setting option {optionName}.").LogError();
                return setResult;
            }

            ResamplerOptions.Set(o, optionName, optionValue);
            isConsumed = true;
        }

        if (isConsumed)
            return 0;

        return ffmpeg.AVERROR_OPTION_NOT_FOUND;
    }
}

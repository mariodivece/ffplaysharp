﻿using FFmpegBindings = FFmpeg.AutoGen.Bindings.DynamicallyLoaded.DynamicallyLoadedBindings;

FFmpegBindings.LibrariesPath = @"C:\ffmpeg\x64";
FFmpegBindings.Initialize();

FFLog.Flags = ffmpeg.AV_LOG_SKIP_REPEATED;
FFLog.Level = ffmpeg.AV_LOG_VERBOSE;

// register all codecs, demux and protocols
ffmpeg.avdevice_register_all();
ffmpeg.avformat_network_init();

var o = ProgramOptions.FromCommandLineArguments(args);

//init_opts();

//signal(SIGINT, sigterm_handler); // Interrupt (ANSI).
//signal(SIGTERM, sigterm_handler); // Termination (ANSI).

if (string.IsNullOrWhiteSpace(o.InputFileName))
    Environment.Exit(1);

var presenter = new SdlPresenter();
var container = MediaContainer.Open(o, presenter);

if (container is null)
{
    ("Failed to initialize Video State!").LogFatal();
    presenter.Stop();
}

presenter.Start();
namespace Unosquare.FFplaySharp
{
    using FFmpeg.AutoGen;
    using System;
    using Unosquare.FFplaySharp.Primitives;
    using Unosquare.FFplaySharp.Rendering;

    class Program
    {
        // TODO: cmdutils.c
        // https://github.com/FFmpeg/FFmpeg/blob/master/fftools/cmdutils.c

        static void Main(string[] args)
        {

            Helpers.LoadNativeLibraries();
            ffmpeg.av_log_set_flags(ffmpeg.AV_LOG_SKIP_REPEATED);
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);

            /* register all codecs, demux and protocols */
            ffmpeg.avdevice_register_all();
            ffmpeg.avformat_network_init();

            var o = ProgramOptions.FromCommandLineArguments(args);

            //init_opts();

            //signal(SIGINT, sigterm_handler); /* Interrupt (ANSI).    */
            //signal(SIGTERM, sigterm_handler); /* Termination (ANSI).  */

            if (string.IsNullOrWhiteSpace(o.InputFileName))
                Environment.Exit(1);

            var presenter = new SdlPresenter();
            var container = MediaContainer.Open(o, presenter);

            if (container == null)
            {
                Helpers.LogFatal("Failed to initialize VideoState!\n");
                presenter.Stop();
            }

            presenter.Start();
        }
    }
}

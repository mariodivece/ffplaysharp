namespace Unosquare.FFplaySharp.DirectPort
{
    using FFmpeg.AutoGen;
    using SDL2;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;

    public static unsafe class PortedProgram
    {
        // TODO: cmdutils.c
        // https://github.com/FFmpeg/FFmpeg/blob/master/fftools/cmdutils.c

        public enum ShowMode
        {
            SHOW_MODE_NONE = -1,
            SHOW_MODE_VIDEO = 0,
            SHOW_MODE_WAVES,
            SHOW_MODE_RDFT,
            SHOW_MODE_NB
        }

        public class VideoState
        {
            public Thread read_tid;
            public AVInputFormat* iformat;
            public bool abort_request;
            public bool force_refresh;
            public bool paused;
            public bool last_paused;
            public bool queue_attachments_req;
            public bool seek_req;
            public int seek_flags;
            public long seek_pos;
            public long seek_rel;
            public int read_pause_return;
            public AVFormatContext* ic;
            public bool realtime;

            public Clock audclk;
            public Clock vidclk;
            public Clock extclk;

            public FrameQueue pictq;
            public FrameQueue subpq;
            public FrameQueue sampq;

            public MediaDecoder auddec;
            public MediaDecoder viddec;
            public MediaDecoder subdec;

            public int audio_stream;

            public ClockSync av_sync_type;

            public double audio_clock;
            public int audio_clock_serial;
            public double audio_diff_cum; /* used for AV difference average computation */
            public double audio_diff_avg_coef;
            public double audio_diff_threshold;
            public int audio_diff_avg_count;
            public AVStream* audio_st;
            public PacketQueue audioq { get; } = new();
            public int audio_hw_buf_size;
            public byte* audio_buf;
            public byte* audio_buf1;
            public uint audio_buf_size; /* in bytes */
            public uint audio_buf1_size;
            public int audio_buf_index; /* in bytes */
            public int audio_write_buf_size;
            public int audio_volume;
            public bool muted;
            public AudioParams audio_src = new();
            public AudioParams audio_filter_src = new();
            public AudioParams audio_tgt = new();
            public SwrContext* swr_ctx;
            public int frame_drops_early;
            public int frame_drops_late;

            public ShowMode show_mode;

            public short[] sample_array = new short[Constants.SAMPLE_ARRAY_SIZE];
            public int sample_array_index;
            public int last_i_start;
            // RDFTContext* rdft;
            // int rdft_bits;
            // FFTSample* rdft_data;
            public int xpos;
            public double last_vis_time;
            public IntPtr vis_texture;
            public IntPtr sub_texture;
            public IntPtr vid_texture;

            public int subtitle_stream;
            public AVStream* subtitle_st;
            public PacketQueue subtitleq { get; } = new();

            public double frame_timer;
            public double frame_last_returned_time;
            public double frame_last_filter_delay;
            public int video_stream;
            public AVStream* video_st;
            public PacketQueue videoq { get; } = new();
            public double max_frame_duration;      // maximum duration of a frame - above this, we consider the jump a timestamp discontinuity
            public SwsContext* img_convert_ctx;
            public SwsContext* sub_convert_ctx;
            public bool eof;

            public string filename;
            public int width = default_width;
            public int height = default_height;
            public int xleft;
            public int ytop;
            public int step;

            public int vfilter_idx;
            public AVFilterContext* in_video_filter;   // the first filter in the video chain
            public AVFilterContext* out_video_filter;  // the last filter in the video chain
            public AVFilterContext* in_audio_filter;   // the first filter in the audio chain
            public AVFilterContext* out_audio_filter;  // the last filter in the audio chain
            public AVFilterGraph* agraph;              // audio filter graph

            public int last_video_stream;
            public int last_audio_stream;
            public int last_subtitle_stream;

            public AutoResetEvent continue_read_thread = new(false);
        }

        /* options specified by the user */
        static AVInputFormat* file_iformat;
        static string input_filename;
        static string window_title;
        static int default_width = 640;
        static int default_height = 480;
        static int screen_width = 0;
        static int screen_height = 0;
        static int screen_left = SDL.SDL_WINDOWPOS_CENTERED;
        static int screen_top = SDL.SDL_WINDOWPOS_CENTERED;
        static bool audio_disable;
        static bool video_disable;
        static bool subtitle_disable;
        static string[] wanted_stream_spec = new string[(int)AVMediaType.AVMEDIA_TYPE_NB];
        static int seek_by_bytes = -1;
        static float seek_interval = 10;
        static bool display_disable;
        static bool borderless;
        static bool alwaysontop;
        static int startup_volume = 100;
        static int show_status = -1;
        static ClockSync av_sync_type = ClockSync.AV_SYNC_AUDIO_MASTER;
        static long start_time = ffmpeg.AV_NOPTS_VALUE;
        static long duration = ffmpeg.AV_NOPTS_VALUE;
        static int fast = 0;
        static bool genpts = false;
        static int lowres = 0;
        static int decoder_reorder_pts = -1;
        static bool autoexit;
        static bool exit_on_keydown;
        static bool exit_on_mousedown;
        static int loop = 1;
        static int framedrop = -1;
        static int infinite_buffer = -1;
        static ShowMode show_mode = ShowMode.SHOW_MODE_NONE;
        static string audio_codec_name;
        static string subtitle_codec_name;
        static string video_codec_name;
        static double rdftspeed = 0.02;
        static long cursor_last_shown;
        static bool cursor_hidden = false;

        static List<string> vfilters_list = new(32);
        static int nb_vfilters = 0;
        static string afilters;

        static bool autorotate = true;
        static bool find_stream_info = true;
        static int filter_nbthreads = 0;

        /* current context */
        static bool is_full_screen;
        static long audio_callback_time;
        static long last_mouse_left_click;

        /* From cmdutils.c */
        static AVDictionary* sws_dict;
        static AVDictionary* swr_opts;
        static AVDictionary* format_opts;
        static AVDictionary* codec_opts;
        static AVDictionary* resample_opts;

        // inlined static variables
        static long last_time_status = 0;
        static double last_audio_clock = 0;

        const int FF_QUIT_EVENT = (int)SDL.SDL_EventType.SDL_USEREVENT + 2;

        static IntPtr window;
        static IntPtr renderer;
        static SDL.SDL_RendererInfo renderer_info;
        static uint audio_dev;

        static VideoState GlobalVideoState;

        static Dictionary<AVPixelFormat, uint> sdl_texture_map = new()
        {
            { AVPixelFormat.AV_PIX_FMT_RGB8, SDL.SDL_PIXELFORMAT_RGB332 },
            { AVPixelFormat.AV_PIX_FMT_RGB444LE, SDL.SDL_PIXELFORMAT_RGB444 },
            { AVPixelFormat.AV_PIX_FMT_RGB555LE, SDL.SDL_PIXELFORMAT_RGB555 },
            { AVPixelFormat.AV_PIX_FMT_BGR555LE, SDL.SDL_PIXELFORMAT_BGR555 },
            { AVPixelFormat.AV_PIX_FMT_RGB565LE, SDL.SDL_PIXELFORMAT_RGB565 },
            { AVPixelFormat.AV_PIX_FMT_BGR565LE, SDL.SDL_PIXELFORMAT_BGR565 },
            { AVPixelFormat.AV_PIX_FMT_RGB24, SDL.SDL_PIXELFORMAT_RGB24 },
            { AVPixelFormat.AV_PIX_FMT_BGR24, SDL.SDL_PIXELFORMAT_BGR24 },
            { AVPixelFormat.AV_PIX_FMT_0RGB, SDL.SDL_PIXELFORMAT_RGB888 },
            { AVPixelFormat.AV_PIX_FMT_0BGR, SDL.SDL_PIXELFORMAT_BGR888 },
            { AVPixelFormat.AV_PIX_FMT_RGB0, SDL.SDL_PIXELFORMAT_RGBX8888 },
            { AVPixelFormat.AV_PIX_FMT_BGR0, SDL.SDL_PIXELFORMAT_BGRX8888 },
            { AVPixelFormat.AV_PIX_FMT_ARGB, SDL.SDL_PIXELFORMAT_ARGB8888 },
            { AVPixelFormat.AV_PIX_FMT_RGBA, SDL.SDL_PIXELFORMAT_RGBA8888 },
            { AVPixelFormat.AV_PIX_FMT_ABGR, SDL.SDL_PIXELFORMAT_ABGR8888 },
            { AVPixelFormat.AV_PIX_FMT_BGRA, SDL.SDL_PIXELFORMAT_BGRA8888 },
            { AVPixelFormat.AV_PIX_FMT_YUV420P, SDL.SDL_PIXELFORMAT_IYUV },
            { AVPixelFormat.AV_PIX_FMT_YUYV422, SDL.SDL_PIXELFORMAT_YUY2 },
            { AVPixelFormat.AV_PIX_FMT_UYVY422, SDL.SDL_PIXELFORMAT_UYVY },
            { AVPixelFormat.AV_PIX_FMT_NONE, SDL.SDL_PIXELFORMAT_UNKNOWN },
        };

        static void uninit_opts()
        {
            fixed (AVDictionary** r_swr_opts = &swr_opts,
                r_sws_dict = &sws_dict,
                r_format_opts = &format_opts,
                r_codec_opts = &codec_opts,
                r_resample_opts = &resample_opts)
            {
                ffmpeg.av_dict_free(r_swr_opts);
                ffmpeg.av_dict_free(r_sws_dict);
                ffmpeg.av_dict_free(r_format_opts);
                ffmpeg.av_dict_free(r_codec_opts);
                ffmpeg.av_dict_free(r_resample_opts);
            }
        }

        static int opt_add_vfilter(void* optctx, string opt, string arg)
        {
            vfilters_list.Add(arg);
            return 0;
        }

        static bool cmp_audio_fmts(AVSampleFormat fmt1, long channel_count1, AVSampleFormat fmt2, long channel_count2)
        {
            /* If channel count == 1, planar and non-planar formats are the same */
            if (channel_count1 == 1 && channel_count2 == 1)
                return ffmpeg.av_get_packed_sample_fmt(fmt1) != ffmpeg.av_get_packed_sample_fmt(fmt2);
            else
                return channel_count1 != channel_count2 || fmt1 != fmt2;
        }

        static ulong get_valid_channel_layout(ulong channel_layout, int channels)
        {
            if (channel_layout != 0 && ffmpeg.av_get_channel_layout_nb_channels(channel_layout) == channels)
                return channel_layout;
            else
                return 0;
        }



        static void fill_rectangle(int x, int y, int w, int h)
        {
            SDL.SDL_Rect rect;
            rect.x = x;
            rect.y = y;
            rect.w = w;
            rect.h = h;
            if (w > 0 && h > 0)
                _ = SDL.SDL_RenderFillRect(renderer, ref rect);
        }

        static int realloc_texture(ref IntPtr texture, uint new_format, int new_width, int new_height, SDL.SDL_BlendMode blendmode, bool init_texture)
        {
            if (texture == IntPtr.Zero || SDL.SDL_QueryTexture(texture, out var format, out var _, out var w, out var h) < 0 || new_width != w || new_height != h || new_format != format)
            {
                if (texture != IntPtr.Zero)
                    SDL.SDL_DestroyTexture(texture);

                if (IntPtr.Zero == (texture = SDL.SDL_CreateTexture(renderer, new_format, (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, new_width, new_height)))
                    return -1;

                if (SDL.SDL_SetTextureBlendMode(texture, blendmode) < 0)
                    return -1;

                if (init_texture)
                {
                    SDL.SDL_Rect rect = new() { w = new_width, h = new_height, x = 0, y = 0 };
                    rect.w = new_width;
                    rect.h = new_height;
                    if (SDL.SDL_LockTexture(texture, ref rect, out var pixels, out var pitch) < 0)
                        return -1;

                    var ptr = (byte*)pixels;
                    for (var i = 0; i < pitch * new_height; i++)
                    {
                        ptr[i] = byte.MinValue;
                    }

                    SDL.SDL_UnlockTexture(texture);
                }

                ffmpeg.av_log(null, ffmpeg.AV_LOG_VERBOSE, $"Created {new_width}x{new_height} texture with {SDL.SDL_GetPixelFormatName(new_format)}.\n");
            }
            return 0;
        }

        static void calculate_display_rect(ref SDL.SDL_Rect rect,
                                   int scr_xleft, int scr_ytop, int scr_width, int scr_height,
                                   int pic_width, int pic_height, AVRational pic_sar)
        {
            AVRational aspect_ratio = pic_sar;
            long width, height, x, y;

            if (ffmpeg.av_cmp_q(aspect_ratio, ffmpeg.av_make_q(0, 1)) <= 0)
                aspect_ratio = ffmpeg.av_make_q(1, 1);

            aspect_ratio = ffmpeg.av_mul_q(aspect_ratio, ffmpeg.av_make_q(pic_width, pic_height));

            /* XXX: we suppose the screen has a 1.0 pixel ratio */
            height = scr_height;
            width = ffmpeg.av_rescale(height, aspect_ratio.num, aspect_ratio.den) & ~1;
            if (width > scr_width)
            {
                width = scr_width;
                height = ffmpeg.av_rescale(width, aspect_ratio.den, aspect_ratio.num) & ~1;
            }
            x = (scr_width - width) / 2;
            y = (scr_height - height) / 2;
            rect.x = scr_xleft + (int)x;
            rect.y = scr_ytop + (int)y;
            rect.w = Math.Max((int)width, 1);
            rect.h = Math.Max((int)height, 1);
        }

        static void get_sdl_pix_fmt_and_blendmode(AVPixelFormat format, out uint sdl_pix_fmt, out SDL.SDL_BlendMode sdl_blendmode)
        {
            sdl_blendmode = SDL.SDL_BlendMode.SDL_BLENDMODE_NONE;
            sdl_pix_fmt = SDL.SDL_PIXELFORMAT_UNKNOWN;
            if (format == AVPixelFormat.AV_PIX_FMT_RGBA ||
                format == AVPixelFormat.AV_PIX_FMT_ARGB ||
                format == AVPixelFormat.AV_PIX_FMT_BGRA ||
                format == AVPixelFormat.AV_PIX_FMT_ABGR)
                sdl_blendmode = SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND;

            if (sdl_texture_map.ContainsKey(format))
                sdl_pix_fmt = sdl_texture_map[format];
        }

        static int upload_texture(ref IntPtr tex, AVFrame* frame, ref SwsContext* img_convert_ctx)
        {
            int ret = 0;
            get_sdl_pix_fmt_and_blendmode((AVPixelFormat)frame->format, out var sdl_pix_fmt, out var sdl_blendmode);
            if (realloc_texture(ref tex,
                sdl_pix_fmt == SDL.SDL_PIXELFORMAT_UNKNOWN
                    ? SDL.SDL_PIXELFORMAT_ARGB8888
                    : sdl_pix_fmt,
                frame->width,
                frame->height,
                sdl_blendmode,
                false) < 0)
            {
                return -1;
            }

            SDL.SDL_Rect rect = new() { w = frame->width, h = frame->height, x = 0, y = 0 };

            if (sdl_pix_fmt == SDL.SDL_PIXELFORMAT_UNKNOWN)
            {
                /* This should only happen if we are not using avfilter... */
                img_convert_ctx = ffmpeg.sws_getCachedContext(img_convert_ctx,
                    frame->width, frame->height, (AVPixelFormat)frame->format, frame->width, frame->height,
                    AVPixelFormat.AV_PIX_FMT_BGRA, Constants.sws_flags, null, null, null);

                if (img_convert_ctx != null)
                {
                    if (SDL.SDL_LockTexture(tex, ref rect, out var pixels, out var pitch) == 0)
                    {
                        var targetStride = new[] { pitch };
                        var targetScan = default(byte_ptrArray8);
                        targetScan[0] = (byte*)pixels;

                        ffmpeg.sws_scale(img_convert_ctx, frame->data, frame->linesize,
                              0, frame->height, targetScan, targetStride);
                        SDL.SDL_UnlockTexture(tex);
                    }
                }
                else
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, "Cannot initialize the conversion context\n");
                    ret = -1;
                }
            }
            else if (sdl_pix_fmt == SDL.SDL_PIXELFORMAT_IYUV)
            {
                if (frame->linesize[0] > 0 && frame->linesize[1] > 0 && frame->linesize[2] > 0)
                {
                    ret = SDL.SDL_UpdateYUVTexture(tex, ref rect, (IntPtr)frame->data[0], frame->linesize[0],
                                                           (IntPtr)frame->data[1], frame->linesize[1],
                                                           (IntPtr)frame->data[2], frame->linesize[2]);
                }
                else if (frame->linesize[0] < 0 && frame->linesize[1] < 0 && frame->linesize[2] < 0)
                {
                    ret = SDL.SDL_UpdateYUVTexture(tex, ref rect, (IntPtr)frame->data[0] + frame->linesize[0] * (frame->height - 1), -frame->linesize[0],
                                                           (IntPtr)frame->data[1] + frame->linesize[1] * (Helpers.AV_CEIL_RSHIFT(frame->height, 1) - 1), -frame->linesize[1],
                                                           (IntPtr)frame->data[2] + frame->linesize[2] * (Helpers.AV_CEIL_RSHIFT(frame->height, 1) - 1), -frame->linesize[2]);
                }
                else
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "Mixed negative and positive linesizes are not supported.\n");
                    return -1;
                }
            }
            else
            {
                if (frame->linesize[0] < 0)
                {
                    ret = SDL.SDL_UpdateTexture(tex, ref rect, (IntPtr)frame->data[0] + frame->linesize[0] * (frame->height - 1), -frame->linesize[0]);
                }
                else
                {
                    ret = SDL.SDL_UpdateTexture(tex, ref rect, (IntPtr)frame->data[0], frame->linesize[0]);
                }
            }

            return ret;
        }

        static void set_sdl_yuv_conversion_mode(AVFrame* frame)
        {
        }

        static void video_image_display(VideoState @is)
        {
            FrameHolder vp;
            FrameHolder sp = null;
            SDL.SDL_Rect rect = new();

            vp = @is.pictq.PeekLast();
            if (@is.subtitle_st != null)
            {
                if (@is.subpq.PendingCount > 0)
                {
                    sp = @is.subpq.Peek();

                    if (vp.Pts >= sp.Pts + ((float)sp.SubtitlePtr->start_display_time / 1000))
                    {
                        if (!sp.uploaded)
                        {
                            if (sp.Width <= 0 || sp.Height <= 0)
                            {
                                sp.Width = vp.Width;
                                sp.Height = vp.Height;
                            }

                            if (realloc_texture(ref @is.sub_texture, SDL.SDL_PIXELFORMAT_ARGB8888, sp.Width, sp.Height, SDL.SDL_BlendMode.SDL_BLENDMODE_BLEND, true) < 0)
                                return;

                            for (var i = 0; i < sp.SubtitlePtr->num_rects; i++)
                            {
                                SDL.SDL_Rect sub_rect = new()
                                {
                                    x = sp.SubtitlePtr->rects[i]->x,
                                    y = sp.SubtitlePtr->rects[i]->y,
                                    w = sp.SubtitlePtr->rects[i]->w,
                                    h = sp.SubtitlePtr->rects[i]->h
                                };

                                sub_rect.x = Helpers.av_clip(sub_rect.x, 0, sp.Width);
                                sub_rect.y = Helpers.av_clip(sub_rect.y, 0, sp.Height);
                                sub_rect.w = Helpers.av_clip(sub_rect.w, 0, sp.Width - sub_rect.x);
                                sub_rect.h = Helpers.av_clip(sub_rect.h, 0, sp.Height - sub_rect.y);

                                @is.sub_convert_ctx = ffmpeg.sws_getCachedContext(@is.sub_convert_ctx,
                                    sub_rect.w, sub_rect.h, AVPixelFormat.AV_PIX_FMT_PAL8,
                                    sub_rect.w, sub_rect.h, AVPixelFormat.AV_PIX_FMT_BGRA,
                                    0, null, null, null);
                                if (@is.sub_convert_ctx == null)
                                {
                                    ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, "Cannot initialize the conversion context\n");
                                    return;
                                }
                                if (SDL.SDL_LockTexture(@is.sub_texture, ref sub_rect, out var pixels, out var pitch) == 0)
                                {
                                    var targetStride = new[] { pitch };
                                    var targetScan = default(byte_ptrArray8);
                                    targetScan[0] = (byte*)pixels;

                                    ffmpeg.sws_scale(@is.sub_convert_ctx, sp.SubtitlePtr->rects[i]->data, sp.SubtitlePtr->rects[i]->linesize,
                                      0, sp.SubtitlePtr->rects[i]->h, targetScan, targetStride);

                                    SDL.SDL_UnlockTexture(@is.sub_texture);
                                }
                            }
                            sp.uploaded = true;
                        }
                    }
                    else
                    {
                        sp = null;
                    }
                }
            }

            calculate_display_rect(ref rect, @is.xleft, @is.ytop, @is.width, @is.height, vp.Width, vp.Height, vp.Sar);

            if (!vp.uploaded)
            {
                if (upload_texture(ref @is.vid_texture, vp.FramePtr, ref @is.img_convert_ctx) < 0)
                    return;
                vp.uploaded = true;
                vp.FlipVertical = vp.FramePtr->linesize[0] < 0;
            }

            var point = new SDL.SDL_Point();

            set_sdl_yuv_conversion_mode(vp.FramePtr);
            SDL.SDL_RenderCopyEx(renderer, @is.vid_texture, ref rect, ref rect, 0, ref point, vp.FlipVertical ? SDL.SDL_RendererFlip.SDL_FLIP_VERTICAL : SDL.SDL_RendererFlip.SDL_FLIP_NONE);
            set_sdl_yuv_conversion_mode(null);

            if (sp != null)
            {
                int i;
                double xratio = (double)rect.w / (double)sp.Width;
                double yratio = (double)rect.h / (double)sp.Height;
                for (i = 0; i < sp.SubtitlePtr->num_rects; i++)
                {
                    SDL.SDL_Rect sub_rect = new()
                    {
                        x = sp.SubtitlePtr->rects[i]->x,
                        y = sp.SubtitlePtr->rects[i]->y,
                        w = sp.SubtitlePtr->rects[i]->w,
                        h = sp.SubtitlePtr->rects[i]->h,
                    };

                    SDL.SDL_Rect target = new()
                    {
                        x = (int)(rect.x + sub_rect.x * xratio),
                        y = (int)(rect.y + sub_rect.y * yratio),
                        w = (int)(sub_rect.w * xratio),
                        h = (int)(sub_rect.h * yratio)
                    };

                    SDL.SDL_RenderCopy(renderer, @is.sub_texture, ref sub_rect, ref target);
                }
            }
        }

        static int compute_mod(int a, int b)
        {
            return a < 0 ? a % b + b : a % b;
        }

        static void video_audio_display(VideoState s)
        {
        }

        static void stream_component_close(VideoState @is, int stream_index)
        {
            AVFormatContext* ic = @is.ic;
            AVCodecParameters* codecpar;

            if (stream_index < 0 || stream_index >= ic->nb_streams)
                return;
            codecpar = ic->streams[stream_index]->codecpar;

            switch (codecpar->codec_type)
            {
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    @is.auddec.Abort(@is.sampq);
                    SDL.SDL_CloseAudioDevice(audio_dev);
                    @is.auddec.Dispose();
                    fixed (SwrContext** swr_ctx = &@is.swr_ctx)
                        ffmpeg.swr_free(swr_ctx);

                    if (@is.audio_buf1 != null)
                        ffmpeg.av_free(@is.audio_buf1);

                    @is.audio_buf1 = null;
                    @is.audio_buf1_size = 0;
                    @is.audio_buf = null;
                    break;
                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    @is.viddec.Abort(@is.pictq);
                    @is.viddec.Dispose();
                    break;
                case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                    @is.subdec.Abort(@is.subpq);
                    @is.subdec.Dispose();
                    break;
                default:
                    break;
            }

            ic->streams[stream_index]->discard = AVDiscard.AVDISCARD_ALL;
            switch (codecpar->codec_type)
            {
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    @is.audio_st = null;
                    @is.audio_stream = -1;
                    break;
                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    @is.video_st = null;
                    @is.video_stream = -1;
                    break;
                case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                    @is.subtitle_st = null;
                    @is.subtitle_stream = -1;
                    break;
                default:
                    break;
            }
        }

        static void stream_close(VideoState @is)
        {
            /* XXX: use a special url_shutdown call to abort parse cleanly */
            @is.abort_request = true;
            @is.read_tid.Join();

            /* close each stream */
            if (@is.audio_stream >= 0)
                stream_component_close(@is, @is.audio_stream);
            if (@is.video_stream >= 0)
                stream_component_close(@is, @is.video_stream);
            if (@is.subtitle_stream >= 0)
                stream_component_close(@is, @is.subtitle_stream);

            fixed (AVFormatContext** ic = &@is.ic)
                ffmpeg.avformat_close_input(ic);

            @is.videoq.Dispose();
            @is.audioq.Dispose();
            @is.subtitleq.Dispose();

            /* free all pictures */
            @is.pictq?.Dispose();
            @is.sampq?.Dispose();
            @is.subpq?.Dispose();
            @is.continue_read_thread.Dispose();
            ffmpeg.sws_freeContext(@is.img_convert_ctx);
            ffmpeg.sws_freeContext(@is.sub_convert_ctx);

            if (@is.vis_texture != IntPtr.Zero)
                SDL.SDL_DestroyTexture(@is.vis_texture);
            if (@is.vid_texture != IntPtr.Zero)
                SDL.SDL_DestroyTexture(@is.vid_texture);
            if (@is.sub_texture != IntPtr.Zero)
                SDL.SDL_DestroyTexture(@is.sub_texture);
        }

        static void do_exit(VideoState @is)
        {
            if (@is != null)
            {
                stream_close(@is);
            }
            if (renderer != IntPtr.Zero)
                SDL.SDL_DestroyRenderer(renderer);
            if (window != IntPtr.Zero)
                SDL.SDL_DestroyWindow(window);

            uninit_opts();

            vfilters_list.Clear();
            ffmpeg.avformat_network_deinit();
            if (show_status != 0)
                Console.WriteLine();
            SDL.SDL_Quit();
            ffmpeg.av_log(null, ffmpeg.AV_LOG_QUIET, "");
            Environment.Exit(0);
        }

        static void sigterm_handler(int sig)
        {
            Environment.Exit(123);
        }

        static void set_default_window_size(int width, int height, AVRational sar)
        {
            SDL.SDL_Rect rect = new();
            int max_width = screen_width != 0 ? screen_width : int.MaxValue;
            int max_height = screen_height != 0 ? screen_height : int.MaxValue;
            if (max_width == int.MaxValue && max_height == int.MaxValue)
                max_height = height;
            calculate_display_rect(ref rect, 0, 0, max_width, max_height, width, height, sar);
            default_width = rect.w;
            default_height = rect.h;
        }

        static int video_open(VideoState @is)
        {
            int w, h;

            w = screen_width != 0 ? screen_width : default_width;
            h = screen_height != 0 ? screen_height : default_height;

            if (string.IsNullOrWhiteSpace(window_title))
                window_title = input_filename;
            SDL.SDL_SetWindowTitle(window, window_title);

            SDL.SDL_SetWindowSize(window, w, h);
            SDL.SDL_SetWindowPosition(window, screen_left, screen_top);
            if (is_full_screen)
                SDL.SDL_SetWindowFullscreen(window, (uint)SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP);

            SDL.SDL_ShowWindow(window);

            @is.width = w;
            @is.height = h;

            return 0;
        }

        static void video_display(VideoState @is)
        {
            if (@is.width != 0)
                video_open(@is);

            SDL.SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
            SDL.SDL_RenderClear(renderer);
            if (@is.audio_st != null && @is.show_mode != ShowMode.SHOW_MODE_VIDEO)
                video_audio_display(@is);
            else if (@is.video_st != null)
                video_image_display(@is);
            SDL.SDL_RenderPresent(renderer);
        }

        static ClockSync get_master_sync_type(VideoState @is)
        {
            if (@is.av_sync_type == ClockSync.AV_SYNC_VIDEO_MASTER)
            {
                if (@is.video_st != null)
                    return ClockSync.AV_SYNC_VIDEO_MASTER;
                else
                    return ClockSync.AV_SYNC_AUDIO_MASTER;
            }
            else if (@is.av_sync_type == ClockSync.AV_SYNC_AUDIO_MASTER)
            {
                if (@is.audio_st != null)
                    return ClockSync.AV_SYNC_AUDIO_MASTER;
                else
                    return ClockSync.AV_SYNC_EXTERNAL_CLOCK;
            }
            else
            {
                return ClockSync.AV_SYNC_EXTERNAL_CLOCK;
            }
        }

        /* get the current master clock value */
        static double get_master_clock(VideoState @is)
        {
            double val;

            switch (get_master_sync_type(@is))
            {
                case ClockSync.AV_SYNC_VIDEO_MASTER:
                    val = @is.vidclk.Get();
                    break;
                case ClockSync.AV_SYNC_AUDIO_MASTER:
                    val = @is.audclk.Get();
                    break;
                default:
                    val = @is.extclk.Get();
                    break;
            }
            return val;
        }

        static void check_external_clock_speed(VideoState @is)
        {
            if (@is.video_stream >= 0 && @is.videoq.Count <= Constants.EXTERNAL_CLOCK_MIN_FRAMES ||

                @is.audio_stream >= 0 && @is.audioq.Count <= Constants.EXTERNAL_CLOCK_MIN_FRAMES)
            {
                @is.extclk.SetSpeed(Math.Max(Constants.EXTERNAL_CLOCK_SPEED_MIN, @is.extclk.SpeedRatio - Constants.EXTERNAL_CLOCK_SPEED_STEP));
            }
            else if ((@is.video_stream < 0 || @is.videoq.Count > Constants.EXTERNAL_CLOCK_MAX_FRAMES) &&
                     (@is.audio_stream < 0 || @is.audioq.Count > Constants.EXTERNAL_CLOCK_MAX_FRAMES))
            {
                @is.extclk.SetSpeed(Math.Min(Constants.EXTERNAL_CLOCK_SPEED_MAX, @is.extclk.SpeedRatio + Constants.EXTERNAL_CLOCK_SPEED_STEP));
            }
            else
            {
                double speed = @is.extclk.SpeedRatio;
                if (speed != 1.0)
                    @is.extclk.SetSpeed(speed + Constants.EXTERNAL_CLOCK_SPEED_STEP * (1.0 - speed) / Math.Abs(1.0 - speed));
            }
        }

        /* seek in the stream */
        static void stream_seek(VideoState @is, long pos, long rel, int seek_by_bytes)
        {
            if (!@is.seek_req)
            {
                @is.seek_pos = pos;
                @is.seek_rel = rel;
                @is.seek_flags &= ~ffmpeg.AVSEEK_FLAG_BYTE;
                if (seek_by_bytes != 0)
                    @is.seek_flags |= ffmpeg.AVSEEK_FLAG_BYTE;
                @is.seek_req = true;
                @is.continue_read_thread.Set();
            }
        }

        /* pause or resume the video */
        static void stream_toggle_pause(VideoState @is)
        {
            if (@is.paused)
            {
                @is.frame_timer += ffmpeg.av_gettime_relative() / 1000000.0 - @is.vidclk.LastUpdated;
                if (@is.read_pause_return != ffmpeg.AVERROR(38))
                {
                    @is.vidclk.IsPaused = false;
                }
                @is.vidclk.Set(@is.vidclk.Get(), @is.vidclk.Serial);
            }

            @is.extclk.Set(@is.extclk.Get(), @is.extclk.Serial);
            @is.paused = @is.audclk.IsPaused = @is.vidclk.IsPaused = @is.extclk.IsPaused = !@is.paused;
        }

        static void toggle_pause(VideoState @is)
        {
            stream_toggle_pause(@is);
            @is.step = 0;
        }

        static void toggle_mute(VideoState @is)
        {
            @is.muted = !@is.muted;
        }

        static void update_volume(VideoState @is, int sign, double step)
        {
            var volume_level = @is.audio_volume > 0 ? (20 * Math.Log(@is.audio_volume / (double)SDL.SDL_MIX_MAXVOLUME) / Math.Log(10)) : -1000.0;
            var new_volume = (int)Math.Round(SDL.SDL_MIX_MAXVOLUME * Math.Pow(10.0, (volume_level + sign * step) / 20.0), 0);
            @is.audio_volume = Helpers.av_clip(@is.audio_volume == new_volume ? (@is.audio_volume + sign) : new_volume, 0, SDL.SDL_MIX_MAXVOLUME);
        }

        static void step_to_next_frame(VideoState @is)
        {
            /* if the stream is paused unpause it, then step */
            if (@is.paused)
                stream_toggle_pause(@is);
            @is.step = 1;
        }

        static double compute_target_delay(double delay, VideoState @is)
        {
            double sync_threshold, diff = 0;

            /* update delay to follow master synchronisation source */
            if (get_master_sync_type(@is) != ClockSync.AV_SYNC_VIDEO_MASTER)
            {
                /* if video is slave, we try to correct big delays by
                   duplicating or deleting a frame */
                diff = @is.vidclk.Get() - get_master_clock(@is);

                /* skip or repeat frame. We take into account the
                   delay to compute the threshold. I still don't know
                   if it is the best guess */
                sync_threshold = Math.Max(Constants.AV_SYNC_THRESHOLD_MIN, Math.Min(Constants.AV_SYNC_THRESHOLD_MAX, delay));
                if (!double.IsNaN(diff) && Math.Abs(diff) < @is.max_frame_duration)
                {
                    if (diff <= -sync_threshold)
                        delay = Math.Max(0, delay + diff);
                    else if (diff >= sync_threshold && delay > Constants.AV_SYNC_FRAMEDUP_THRESHOLD)
                        delay += diff;
                    else if (diff >= sync_threshold)
                        delay = 2 * delay;
                }
            }

            ffmpeg.av_log(null, ffmpeg.AV_LOG_TRACE, $"video: delay={delay,-8:0.####} A-V={-diff,-8:0.####}\n");

            return delay;
        }

        static double vp_duration(VideoState @is, FrameHolder vp, FrameHolder nextvp)
        {
            if (vp.Serial == nextvp.Serial)
            {
                double duration = nextvp.Pts - vp.Pts;
                if (double.IsNaN(duration) || duration <= 0 || duration > @is.max_frame_duration)
                    return vp.Duration;
                else
                    return duration;
            }
            else
            {
                return 0.0;
            }
        }

        static void update_video_pts(VideoState @is, double pts, long pos, int serial)
        {
            /* update current video pts */
            @is.vidclk.Set(pts, serial);
            @is.extclk.SyncToSlave(@is.vidclk);
        }

        /* called to display each frame */
        static void video_refresh(VideoState @is, ref double remaining_time)
        {
            double time;

            FrameHolder sp, sp2;

            if (!@is.paused && get_master_sync_type(@is) == ClockSync.AV_SYNC_EXTERNAL_CLOCK && @is.realtime)
                check_external_clock_speed(@is);

            if (!display_disable && @is.show_mode != ShowMode.SHOW_MODE_VIDEO && @is.audio_st != null)
            {
                time = ffmpeg.av_gettime_relative() / 1000000.0;
                if (@is.force_refresh || @is.last_vis_time + rdftspeed < time)
                {
                    video_display(@is);
                    @is.last_vis_time = time;
                }
                remaining_time = Math.Min(remaining_time, @is.last_vis_time + rdftspeed - time);
            }

            if (@is.video_st != null)
            {
            retry:
                if (@is.pictq.PendingCount == 0)
                {
                    // nothing to do, no picture to display in the queue
                }
                else
                {
                    double last_duration, duration, delay;

                    /* dequeue the picture */
                    var lastvp = @is.pictq.PeekLast();
                    var vp = @is.pictq.Peek();

                    if (vp.Serial != @is.videoq.Serial)
                    {
                        @is.pictq.Next();
                        goto retry;
                    }

                    if (lastvp.Serial != vp.Serial)
                        @is.frame_timer = ffmpeg.av_gettime_relative() / 1000000.0;

                    if (@is.paused)
                        goto display;

                    /* compute nominal last_duration */
                    last_duration = vp_duration(@is, lastvp, vp);
                    delay = compute_target_delay(last_duration, @is);

                    time = ffmpeg.av_gettime_relative() / 1000000.0;
                    if (time < @is.frame_timer + delay)
                    {
                        remaining_time = Math.Min(@is.frame_timer + delay - time, remaining_time);
                        goto display;
                    }

                    @is.frame_timer += delay;
                    if (delay > 0 && time - @is.frame_timer > Constants.AV_SYNC_THRESHOLD_MAX)
                        @is.frame_timer = time;

                    if (!double.IsNaN(vp.Pts))
                        update_video_pts(@is, vp.Pts, vp.Position, vp.Serial);

                    if (@is.pictq.PendingCount > 1)
                    {
                        var nextvp = @is.pictq.PeekNext();
                        duration = vp_duration(@is, vp, nextvp);
                        if (@is.step == 0 && (framedrop > 0 || (framedrop != 0 && get_master_sync_type(@is) != ClockSync.AV_SYNC_VIDEO_MASTER)) && time > @is.frame_timer + duration)
                        {
                            @is.frame_drops_late++;
                            @is.pictq.Next();
                            goto retry;
                        }
                    }

                    if (@is.subtitle_st != null)
                    {
                        while (@is.subpq.PendingCount > 0)
                        {
                            sp = @is.subpq.Peek();

                            if (@is.subpq.PendingCount > 1)
                                sp2 = @is.subpq.PeekNext();
                            else
                                sp2 = null;

                            if (sp.Serial != @is.subtitleq.Serial
                                    || (@is.vidclk.Pts > (sp.Pts + ((float)sp.SubtitlePtr->end_display_time / 1000)))
                                    || (sp2 != null && @is.vidclk.Pts > (sp2.Pts + ((float)sp2.SubtitlePtr->start_display_time / 1000))))
                            {
                                if (sp.uploaded)
                                {
                                    for (var i = 0; i < sp.SubtitlePtr->num_rects; i++)
                                    {
                                        var sub_rect = new SDL.SDL_Rect()
                                        {
                                            x = sp.SubtitlePtr->rects[i]->x,
                                            y = sp.SubtitlePtr->rects[i]->y,
                                            w = sp.SubtitlePtr->rects[i]->w,
                                            h = sp.SubtitlePtr->rects[i]->h,
                                        };

                                        if (SDL.SDL_LockTexture(@is.sub_texture, ref sub_rect, out var pixels, out var pitch) == 0)
                                        {
                                            var ptr = (byte*)pixels;
                                            for (var j = 0; j < sub_rect.h; j++, ptr += pitch)
                                            {
                                                for (var b = 0; b < sub_rect.w << 2; b++)
                                                {
                                                    ptr[b] = byte.MinValue;
                                                }
                                            }

                                            SDL.SDL_UnlockTexture(@is.sub_texture);
                                        }
                                    }
                                }
                                @is.subpq.Next();
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                    @is.pictq.Next();
                    @is.force_refresh = true;

                    if (@is.step != 0 && !@is.paused)
                        stream_toggle_pause(@is);
                }
            display:
                /* display picture */
                if (!display_disable && @is.force_refresh && @is.show_mode == ShowMode.SHOW_MODE_VIDEO && @is.pictq.ReadIndexShown)
                    video_display(@is);
            }

            @is.force_refresh = false;
            if (show_status != 0)
            {

                long cur_time;
                int aqsize, vqsize, sqsize;
                double av_diff;

                cur_time = ffmpeg.av_gettime_relative();
                if (last_time_status == 0 || (cur_time - last_time_status) >= 30000)
                {
                    aqsize = 0;
                    vqsize = 0;
                    sqsize = 0;
                    if (@is.audio_st != null)
                        aqsize = @is.audioq.Size;
                    if (@is.video_st != null)
                        vqsize = @is.videoq.Size;
                    if (@is.subtitle_st != null)
                        sqsize = @is.subtitleq.Size;
                    av_diff = 0;
                    if (@is.audio_st != null && @is.video_st != null)
                        av_diff = @is.audclk.Get() - @is.vidclk.Get();
                    else if (@is.video_st != null)
                        av_diff = get_master_clock(@is) - @is.vidclk.Get();
                    else if (@is.audio_st != null)
                        av_diff = get_master_clock(@is) - @is.audclk.Get();

                    var buf = new StringBuilder();
                    buf.Append($"{get_master_clock(@is),-8:0.####} ");
                    buf.Append((@is.audio_st != null && @is.video_st != null) ? "A-V" : (@is.video_st != null ? "M-V" : (@is.audio_st != null ? "M-A" : "   ")));
                    buf.Append($":{av_diff,-8:0.####} ");
                    buf.Append($"fd={(@is.frame_drops_early + @is.frame_drops_late)} ");
                    buf.Append($"aq={(aqsize / 1024)}KB ");
                    buf.Append($"vq={(vqsize / 1024)}KB ");
                    buf.Append($"sq={(sqsize)}B ");
                    buf.Append($" f={(@is.video_st != null ? @is.viddec.CodecContext->pts_correction_num_faulty_dts : 0)} / ");
                    buf.Append($"{(@is.video_st != null ? @is.viddec.CodecContext->pts_correction_num_faulty_pts : 0)}");

                    if (show_status == 1 && ffmpeg.AV_LOG_INFO > ffmpeg.av_log_get_level())
                        Console.WriteLine(buf.ToString());
                    else
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_INFO, $"{buf}\n");

                    last_time_status = cur_time;
                }
            }
        }

        static int queue_picture(VideoState @is, AVFrame* src_frame, double pts, double duration, long pos, int serial)
        {
            var vp = @is.pictq.PeekWriteable();

            if (vp == null) return -1;

            vp.Sar = src_frame->sample_aspect_ratio;
            vp.uploaded = false;

            vp.Width = src_frame->width;
            vp.Height = src_frame->height;
            vp.Format = src_frame->format;

            vp.Pts = pts;
            vp.Duration = duration;
            vp.Position = pos;
            vp.Serial = serial;

            set_default_window_size(vp.Width, vp.Height, vp.Sar);

            ffmpeg.av_frame_move_ref(vp.FramePtr, src_frame);
            @is.pictq.Push();
            return 0;
        }

        static int get_video_frame(VideoState @is, out AVFrame* frame)
        {
            frame = null;
            int got_picture;

            if ((got_picture = @is.viddec.DecodeFrame(out frame, out _)) < 0)
                return -1;

            if (got_picture != 0)
            {
                double dpts = double.NaN;

                if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                    dpts = ffmpeg.av_q2d(@is.video_st->time_base) * frame->pts;

                frame->sample_aspect_ratio = ffmpeg.av_guess_sample_aspect_ratio(@is.ic, @is.video_st, frame);

                if (framedrop > 0 || (framedrop != 0 && get_master_sync_type(@is) != ClockSync.AV_SYNC_VIDEO_MASTER))
                {
                    if (frame->pts != ffmpeg.AV_NOPTS_VALUE)
                    {
                        double diff = dpts - get_master_clock(@is);
                        if (!double.IsNaN(diff) && Math.Abs(diff) < Constants.AV_NOSYNC_THRESHOLD &&
                            diff - @is.frame_last_filter_delay < 0 &&
                            @is.viddec.PacketSerial == @is.vidclk.Serial &&
                            @is.videoq.Count != 0)
                        {
                            @is.frame_drops_early++;
                            ffmpeg.av_frame_unref(frame);
                            got_picture = 0;
                        }
                    }
                }
            }

            return got_picture;
        }

        static int configure_filtergraph(AVFilterGraph* graph, string filtergraph,
                                 AVFilterContext* source_ctx, AVFilterContext* sink_ctx)
        {
            int ret;
            var nb_filters = graph->nb_filters;
            AVFilterInOut* outputs = null, inputs = null;

            if (!string.IsNullOrWhiteSpace(filtergraph))
            {
                outputs = ffmpeg.avfilter_inout_alloc();
                inputs = ffmpeg.avfilter_inout_alloc();
                if (outputs == null || inputs == null)
                {
                    ret = ffmpeg.AVERROR(ffmpeg.ENOMEM);
                    goto fail;
                }

                outputs->name = ffmpeg.av_strdup("in");
                outputs->filter_ctx = source_ctx;
                outputs->pad_idx = 0;
                outputs->next = null;

                inputs->name = ffmpeg.av_strdup("out");
                inputs->filter_ctx = sink_ctx;
                inputs->pad_idx = 0;
                inputs->next = null;

                if ((ret = ffmpeg.avfilter_graph_parse_ptr(graph, filtergraph, &inputs, &outputs, null)) < 0)
                    goto fail;
            }
            else
            {
                if ((ret = ffmpeg.avfilter_link(source_ctx, 0, sink_ctx, 0)) < 0)
                    goto fail;
            }

            /* Reorder the filters to ensure that inputs of the custom filters are merged first */
            for (var i = 0; i < graph->nb_filters - nb_filters; i++)
                Helpers.FFSWAP(ref graph->filters, i, i + (int)nb_filters);

            ret = ffmpeg.avfilter_graph_config(graph, null);
        fail:
            ffmpeg.avfilter_inout_free(&outputs);
            ffmpeg.avfilter_inout_free(&inputs);
            return ret;
        }

        static int configure_video_filters(AVFilterGraph* graph, VideoState @is, string vfilters, AVFrame* frame)
        {
            // enum AVPixelFormat pix_fmts[FF_ARRAY_ELEMS(sdl_texture_format_map)];
            var pix_fmts = new List<int>(sdl_texture_map.Count);
            string sws_flags_str = string.Empty;
            string buffersrc_args = string.Empty;
            int ret;
            AVFilterContext* filt_src = null, filt_out = null, last_filter = null;
            AVCodecParameters* codecpar = @is.video_st->codecpar;
            AVRational fr = ffmpeg.av_guess_frame_rate(@is.ic, @is.video_st, null);
            AVDictionaryEntry* e = null;

            for (var i = 0; i < renderer_info.num_texture_formats; i++)
            {
                foreach (var kvp in sdl_texture_map)
                {
                    if (kvp.Value == renderer_info.texture_formats[i])
                    {
                        pix_fmts.Add((int)kvp.Key);
                    }
                }
            }

            //pix_fmts.Add(AVPixelFormat.AV_PIX_FMT_NONE);

            while ((e = ffmpeg.av_dict_get(sws_dict, "", e, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Marshal.PtrToStringUTF8((IntPtr)e->key);
                var value = Marshal.PtrToStringUTF8((IntPtr)e->value);
                if (key == "sws_flags")
                    sws_flags_str = $"flags={value}:{sws_flags_str}";
                else
                    sws_flags_str = $"{key}={value}:{sws_flags_str}";
            }

            if (string.IsNullOrWhiteSpace(sws_flags_str))
                sws_flags_str = null;

            graph->scale_sws_opts = sws_flags_str != null ? ffmpeg.av_strdup(sws_flags_str) : null;
            buffersrc_args = $"video_size={frame->width}x{frame->height}:pix_fmt={frame->format}:time_base={@is.video_st->time_base.num}/{@is.video_st->time_base.den}:pixel_aspect={codecpar->sample_aspect_ratio.num}/{Math.Max(codecpar->sample_aspect_ratio.den, 1)}";

            if (fr.num != 0 && fr.den != 0)
                buffersrc_args = $"{buffersrc_args}:frame_rate={fr.num}/{fr.den}";

            if ((ret = ffmpeg.avfilter_graph_create_filter(&filt_src,
                                                    ffmpeg.avfilter_get_by_name("buffer"),
                                                    "ffplay_buffer", buffersrc_args, null,
                                                    graph)) < 0)
                goto fail;

            ret = ffmpeg.avfilter_graph_create_filter(&filt_out,
                                               ffmpeg.avfilter_get_by_name("buffersink"),
                                               "ffplay_buffersink", null, null, graph);
            if (ret < 0)
                goto fail;

            if ((ret = Helpers.av_opt_set_int_list(filt_out, "pix_fmts", pix_fmts.ToArray(), ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                goto fail;

            last_filter = filt_out;
            if (autorotate)
            {
                double theta = Helpers.get_rotation(@is.video_st);

                if (Math.Abs(theta - 90) < 1.0)
                {
                    if (!Helpers.INSERT_FILT("transpose", "clock", ref graph, ref ret, ref last_filter))
                        goto fail;
                }
                else if (Math.Abs(theta - 180) < 1.0)
                {
                    if (!Helpers.INSERT_FILT("hflip", null, ref graph, ref ret, ref last_filter))
                        goto fail;

                    if (!Helpers.INSERT_FILT("vflip", null, ref graph, ref ret, ref last_filter))
                        goto fail;
                }
                else if (Math.Abs(theta - 270) < 1.0)
                {
                    if (!Helpers.INSERT_FILT("transpose", "cclock", ref graph, ref ret, ref last_filter))
                        goto fail;
                }
                else if (Math.Abs(theta) > 1.0)
                {
                    if (!Helpers.INSERT_FILT("rotate", $"{theta}*PI/180", ref graph, ref ret, ref last_filter))
                        goto fail;
                }
            }

            if ((ret = configure_filtergraph(graph, vfilters, filt_src, last_filter)) < 0)
                goto fail;

            @is.in_video_filter = filt_src;
            @is.out_video_filter = filt_out;

        fail:
            return ret;
        }

        static int configure_audio_filters(VideoState @is, string afilters, bool force_output_format)
        {
            var sample_fmts = new[] { (int)AVSampleFormat.AV_SAMPLE_FMT_S16 };
            var sample_rates = new[] { 0 };
            var channel_layouts = new[] { 0 };
            var channels = new[] { 0 };

            AVFilterContext* filt_asrc = null, filt_asink = null;
            string aresample_swr_opts = string.Empty;
            AVDictionaryEntry* e = null;
            string asrc_args = null;
            int ret;

            fixed (AVFilterGraph** agraph = &@is.agraph)
                ffmpeg.avfilter_graph_free(agraph);

            if ((@is.agraph = ffmpeg.avfilter_graph_alloc()) == null)
                return ffmpeg.AVERROR(ffmpeg.ENOMEM);
            @is.agraph->nb_threads = filter_nbthreads;

            while ((e = ffmpeg.av_dict_get(swr_opts, "", e, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Marshal.PtrToStringUTF8((IntPtr)e->key);
                var value = Marshal.PtrToStringUTF8((IntPtr)e->value);
                aresample_swr_opts = $"{key}={value}:{aresample_swr_opts}";
            }

            if (string.IsNullOrWhiteSpace(aresample_swr_opts))
                aresample_swr_opts = null;

            ffmpeg.av_opt_set(@is.agraph, "aresample_swr_opts", aresample_swr_opts, 0);
            asrc_args = $"sample_rate={@is.audio_filter_src.Frequency}:sample_fmt={ffmpeg.av_get_sample_fmt_name(@is.audio_filter_src.SampleFormat)}:channels={@is.audio_filter_src.Channels}:time_base={1}/{@is.audio_filter_src.Frequency}";

            if (@is.audio_filter_src.Layout != 0)
                asrc_args = $"{asrc_args}:channel_layout=0x{@is.audio_filter_src.Layout.ToString("x16")}";

            ret = ffmpeg.avfilter_graph_create_filter(&filt_asrc,
                                               ffmpeg.avfilter_get_by_name("abuffer"), "ffplay_abuffer",
                                               asrc_args, null, @is.agraph);
            if (ret < 0)
                goto end;

            ret = ffmpeg.avfilter_graph_create_filter(&filt_asink,
                                               ffmpeg.avfilter_get_by_name("abuffersink"), "ffplay_abuffersink",
                                               null, null, @is.agraph);
            if (ret < 0)
                goto end;

            if ((ret = Helpers.av_opt_set_int_list(filt_asink, "sample_fmts", sample_fmts, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                goto end;

            if ((ret = ffmpeg.av_opt_set_int(filt_asink, "all_channel_counts", 1, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                goto end;

            if (force_output_format)
            {
                channel_layouts[0] = Convert.ToInt32(@is.audio_tgt.Layout);
                channels[0] = @is.audio_tgt.Layout != 0 ? -1 : @is.audio_tgt.Channels;
                sample_rates[0] = @is.audio_tgt.Frequency;
                if ((ret = ffmpeg.av_opt_set_int(filt_asink, "all_channel_counts", 0, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                    goto end;
                if ((ret = Helpers.av_opt_set_int_list(filt_asink, "channel_layouts", channel_layouts.Cast<int>().ToArray(), ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                    goto end;
                if ((ret = Helpers.av_opt_set_int_list(filt_asink, "channel_counts", channels, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                    goto end;
                if ((ret = Helpers.av_opt_set_int_list(filt_asink, "sample_rates", sample_rates, ffmpeg.AV_OPT_SEARCH_CHILDREN)) < 0)
                    goto end;
            }

            if ((ret = configure_filtergraph(@is.agraph, afilters, filt_asrc, filt_asink)) < 0)
                goto end;

            @is.in_audio_filter = filt_asrc;
            @is.out_audio_filter = filt_asink;

        end:
            if (ret < 0)
            {
                fixed (AVFilterGraph** agraph = &@is.agraph)
                    ffmpeg.avfilter_graph_free(agraph);
            }
            return ret;
        }

        static void audio_thread(object arg)
        {
            var @is = arg as VideoState;

            FrameHolder af;
            var last_serial = -1;
            long dec_channel_layout;
            bool reconfigure;
            int got_frame = 0;
            AVRational tb;
            int ret = 0;

            var frame = ffmpeg.av_frame_alloc();

            const int bufLength = 1024;
            var buf1 = stackalloc byte[bufLength];
            var buf2 = stackalloc byte[bufLength];

            do
            {
                if ((got_frame = @is.auddec.DecodeFrame(out frame, out _)) < 0)
                    goto the_end;

                if (got_frame != 0)
                {
                    tb = new() { num = 1, den = frame->sample_rate };

                    dec_channel_layout = (long)get_valid_channel_layout(frame->channel_layout, frame->channels);

                    reconfigure =
                        cmp_audio_fmts(@is.audio_filter_src.SampleFormat, @is.audio_filter_src.Channels,
                                       (AVSampleFormat)frame->format, frame->channels) ||
                        @is.audio_filter_src.Layout != dec_channel_layout ||
                        @is.audio_filter_src.Frequency != frame->sample_rate ||
                        @is.auddec.PacketSerial != last_serial;

                    if (reconfigure)
                    {
                        ffmpeg.av_get_channel_layout_string(buf1, bufLength, -1, (ulong)@is.audio_filter_src.Layout);
                        ffmpeg.av_get_channel_layout_string(buf2, bufLength, -1, (ulong)dec_channel_layout);
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_DEBUG,
                           $"Audio frame changed from " +
                           $"rate:{@is.audio_filter_src.Frequency} ch:{@is.audio_filter_src.Channels} fmt:{ffmpeg.av_get_sample_fmt_name(@is.audio_filter_src.SampleFormat)} layout:{Marshal.PtrToStringUTF8((IntPtr)buf1)} serial:{last_serial} to " +
                           $"rate:{frame->sample_rate} ch:{frame->channels} fmt:{ffmpeg.av_get_sample_fmt_name((AVSampleFormat)frame->format)} layout:{Marshal.PtrToStringUTF8((IntPtr)buf2)} serial:{@is.auddec.PacketSerial}\n");

                        @is.audio_filter_src.SampleFormat = (AVSampleFormat)frame->format;
                        @is.audio_filter_src.Channels = frame->channels;
                        @is.audio_filter_src.Layout = dec_channel_layout;
                        @is.audio_filter_src.Frequency = frame->sample_rate;
                        last_serial = @is.auddec.PacketSerial;

                        if ((ret = configure_audio_filters(@is, afilters, true)) < 0)
                            goto the_end;
                    }

                    if ((ret = ffmpeg.av_buffersrc_add_frame(@is.in_audio_filter, frame)) < 0)
                        goto the_end;

                    while ((ret = ffmpeg.av_buffersink_get_frame_flags(@is.out_audio_filter, frame, 0)) >= 0)
                    {
                        tb = ffmpeg.av_buffersink_get_time_base(@is.out_audio_filter);

                        if ((af = @is.sampq.PeekWriteable()) == null)
                            goto the_end;

                        af.Pts = (frame->pts == ffmpeg.AV_NOPTS_VALUE) ? double.NaN : frame->pts * ffmpeg.av_q2d(tb);
                        af.Position = frame->pkt_pos;
                        af.Serial = @is.auddec.PacketSerial;
                        af.Duration = ffmpeg.av_q2d(new AVRational() { num = frame->nb_samples, den = frame->sample_rate });

                        ffmpeg.av_frame_move_ref(af.FramePtr, frame);
                        @is.sampq.Push();

                        if (@is.audioq.Serial != @is.auddec.PacketSerial)
                            break;
                    }
                    if (ret == ffmpeg.AVERROR_EOF)
                        @is.auddec.HasFinished = @is.auddec.PacketSerial;
                }
            } while (ret >= 0 || ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF);
        the_end:
            fixed (AVFilterGraph** agraph = &@is.agraph)
                ffmpeg.avfilter_graph_free(agraph);
            ffmpeg.av_frame_free(&frame);
            // return ret;
        }



        static void video_thread(object arg)
        {
            var @is = arg as VideoState;
            AVFrame* frame = null; // ffmpeg.av_frame_alloc();
            double pts;
            double duration;
            int ret;
            AVRational tb = @is.video_st->time_base;
            AVRational frame_rate = ffmpeg.av_guess_frame_rate(@is.ic, @is.video_st, null);

            AVFilterGraph* graph = null;
            AVFilterContext* filt_out = null, filt_in = null;
            int last_w = 0;
            int last_h = 0;
            int last_format = -2;
            int last_serial = -1;
            int last_vfilter_idx = 0;

            for (; ; )
            {
                ret = get_video_frame(@is, out frame);
                if (ret < 0)
                    goto the_end;

                if (ret == 0)
                    continue;


                if (last_w != frame->width
                    || last_h != frame->height
                    || last_format != frame->format
                    || last_serial != @is.viddec.PacketSerial
                    || last_vfilter_idx != @is.vfilter_idx)
                {
                    var lastFormat = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)last_format);
                    lastFormat ??= "none";

                    var frameFormat = ffmpeg.av_get_pix_fmt_name((AVPixelFormat)frame->format);
                    frameFormat ??= "none";

                    ffmpeg.av_log(null, ffmpeg.AV_LOG_DEBUG,
                           $"Video frame changed from size:{last_w}x%{last_h} format:{lastFormat} serial:{last_serial} to " +
                           $"size:{frame->width}x{frame->height} format:{frameFormat} serial:{@is.viddec.PacketSerial}\n");

                    ffmpeg.avfilter_graph_free(&graph);
                    graph = ffmpeg.avfilter_graph_alloc();
                    if (graph == null)
                    {
                        ret = ffmpeg.AVERROR(ffmpeg.ENOMEM);
                        goto the_end;
                    }
                    graph->nb_threads = filter_nbthreads;
                    if ((ret = configure_video_filters(graph, @is, vfilters_list.Count > 0 ? vfilters_list[@is.vfilter_idx] : null, frame)) < 0)
                    {
                        var evt = new SDL.SDL_Event()
                        {
                            type = (SDL.SDL_EventType)FF_QUIT_EVENT,
                        };

                        // evt.user.data1 = GCHandle.ToIntPtr(VideoStateHandle);
                        SDL.SDL_PushEvent(ref evt);
                        goto the_end;
                    }

                    filt_in = @is.in_video_filter;
                    filt_out = @is.out_video_filter;
                    last_w = frame->width;
                    last_h = frame->height;
                    last_format = frame->format;
                    last_serial = @is.viddec.PacketSerial;
                    last_vfilter_idx = @is.vfilter_idx;
                    frame_rate = ffmpeg.av_buffersink_get_frame_rate(filt_out);
                }

                ret = ffmpeg.av_buffersrc_add_frame(filt_in, frame);
                if (ret < 0)
                    goto the_end;

                while (ret >= 0)
                {
                    @is.frame_last_returned_time = ffmpeg.av_gettime_relative() / 1000000.0;

                    ret = ffmpeg.av_buffersink_get_frame_flags(filt_out, frame, 0);
                    if (ret < 0)
                    {
                        if (ret == ffmpeg.AVERROR_EOF)
                            @is.viddec.HasFinished = @is.viddec.PacketSerial;
                        ret = 0;
                        break;
                    }

                    @is.frame_last_filter_delay = ffmpeg.av_gettime_relative() / 1000000.0 - @is.frame_last_returned_time;
                    if (Math.Abs(@is.frame_last_filter_delay) > Constants.AV_NOSYNC_THRESHOLD / 10.0)
                        @is.frame_last_filter_delay = 0;

                    tb = ffmpeg.av_buffersink_get_time_base(filt_out);
                    duration = (frame_rate.num != 0 && frame_rate.den != 0 ? ffmpeg.av_q2d(new AVRational() { num = frame_rate.den, den = frame_rate.num }) : 0);
                    pts = (frame->pts == ffmpeg.AV_NOPTS_VALUE) ? double.NaN : frame->pts * ffmpeg.av_q2d(tb);
                    ret = queue_picture(@is, frame, pts, duration, frame->pkt_pos, @is.viddec.PacketSerial);
                    ffmpeg.av_frame_unref(frame);

                    if (@is.videoq.Serial != @is.viddec.PacketSerial)
                        break;
                }

                if (ret < 0)
                    goto the_end;
            }
        the_end:

            ffmpeg.avfilter_graph_free(&graph);
            ffmpeg.av_frame_free(&frame);
            return; // 0;
        }

        static void subtitle_thread(object arg)
        {
            var @is = arg as VideoState;
            FrameHolder sp;
            int got_subtitle;
            double pts;

            for (; ; )
            {
                if ((sp = @is.subpq.PeekWriteable()) == null)
                    return; // 0;

                if ((got_subtitle = @is.subdec.DecodeFrame(out _, out var spsub)) < 0)
                    break;
                else
                    sp.SubtitlePtr = spsub;

                pts = 0;

                if (got_subtitle != 0 && sp.SubtitlePtr->format == 0)
                {
                    if (sp.SubtitlePtr->pts != ffmpeg.AV_NOPTS_VALUE)
                        pts = sp.SubtitlePtr->pts / (double)ffmpeg.AV_TIME_BASE;
                    sp.Pts = pts;
                    sp.Serial = @is.subdec.PacketSerial;
                    sp.Width = @is.subdec.CodecContext->width;
                    sp.Height = @is.subdec.CodecContext->height;
                    sp.uploaded = false;

                    /* now we can update the picture count */
                    @is.subpq.Push();
                }
                else if (got_subtitle != 0)
                {
                    ffmpeg.avsubtitle_free(sp.SubtitlePtr);
                }
            }
            return; // 0
        }

        /* copy samples for viewing in editor window */
        static void update_sample_display(VideoState @is, short* samples, int samples_size)
        {
            int size, len;

            size = samples_size / sizeof(short);
            while (size > 0)
            {
                len = Constants.SAMPLE_ARRAY_SIZE - @is.sample_array_index;
                if (len > size)
                    len = size;

                fixed (short* targetAddress = &@is.sample_array[@is.sample_array_index])
                    Buffer.MemoryCopy(samples, targetAddress, len * sizeof(short), len * sizeof(short));

                samples += len;
                @is.sample_array_index += len;
                if (@is.sample_array_index >= Constants.SAMPLE_ARRAY_SIZE)
                    @is.sample_array_index = 0;
                size -= len;
            }
        }

        /* return the wanted number of samples to get better sync if sync_type is video
 * or external master clock */
        static int synchronize_audio(VideoState @is, int nb_samples)
        {
            int wanted_nb_samples = nb_samples;

            /* if not master, then we try to remove or add samples to correct the clock */
            if (get_master_sync_type(@is) != ClockSync.AV_SYNC_AUDIO_MASTER)
            {
                double diff, avg_diff;
                int min_nb_samples, max_nb_samples;

                diff = @is.audclk.Get() - get_master_clock(@is);

                if (!double.IsNaN(diff) && Math.Abs(diff) < Constants.AV_NOSYNC_THRESHOLD)
                {
                    @is.audio_diff_cum = diff + @is.audio_diff_avg_coef * @is.audio_diff_cum;
                    if (@is.audio_diff_avg_count < Constants.AUDIO_DIFF_AVG_NB)
                    {
                        /* not enough measures to have a correct estimate */
                        @is.audio_diff_avg_count++;
                    }
                    else
                    {
                        /* estimate the A-V difference */
                        avg_diff = @is.audio_diff_cum * (1.0 - @is.audio_diff_avg_coef);

                        if (Math.Abs(avg_diff) >= @is.audio_diff_threshold)
                        {
                            wanted_nb_samples = nb_samples + (int)(diff * @is.audio_src.Frequency);
                            min_nb_samples = (int)((nb_samples * (100 - Constants.SAMPLE_CORRECTION_PERCENT_MAX) / 100));
                            max_nb_samples = (int)((nb_samples * (100 + Constants.SAMPLE_CORRECTION_PERCENT_MAX) / 100));
                            wanted_nb_samples = Helpers.av_clip(wanted_nb_samples, min_nb_samples, max_nb_samples);
                        }

                        ffmpeg.av_log(
                            null, ffmpeg.AV_LOG_TRACE, $"diff={diff} adiff={avg_diff} sample_diff={(wanted_nb_samples - nb_samples)} apts={@is.audio_clock} {@is.audio_diff_threshold}\n");
                    }
                }
                else
                {
                    /* too big difference : may be initial PTS errors, so
                       reset A-V filter */
                    @is.audio_diff_avg_count = 0;
                    @is.audio_diff_cum = 0;
                }
            }

            return wanted_nb_samples;
        }

        /**
 * Decode one audio frame and return its uncompressed size.
 *
 * The processed audio frame is decoded, converted if required, and
 * stored in is->audio_buf, with size in bytes given by the return
 * value.
 */
        static int audio_decode_frame(VideoState @is)
        {
            int data_size, resampled_data_size;
            long dec_channel_layout;
            double audio_clock0;
            int wanted_nb_samples;
            FrameHolder af;

            if (@is.paused)
                return -1;

            do
            {
                while (@is.sampq.PendingCount == 0)
                {
                    if ((ffmpeg.av_gettime_relative() - audio_callback_time) > 1000000L * @is.audio_hw_buf_size / @is.audio_tgt.BytesPerSecond / 2)
                        return -1;
                    ffmpeg.av_usleep(1000);
                }

                if ((af = @is.sampq.PeekReadable()) == null)
                    return -1;

                @is.sampq.Next();

            } while (af.Serial != @is.audioq.Serial);

            data_size = ffmpeg.av_samples_get_buffer_size(null, af.FramePtr->channels,
                                                   af.FramePtr->nb_samples,
                                                   (AVSampleFormat)af.FramePtr->format, 1);

            dec_channel_layout =
                (af.FramePtr->channel_layout != 0 && af.FramePtr->channels == ffmpeg.av_get_channel_layout_nb_channels(af.FramePtr->channel_layout))
                ? (long)af.FramePtr->channel_layout
                : ffmpeg.av_get_default_channel_layout(af.FramePtr->channels);
            wanted_nb_samples = synchronize_audio(@is, af.FramePtr->nb_samples);

            if (af.FramePtr->format != (int)@is.audio_src.SampleFormat ||
                dec_channel_layout != @is.audio_src.Layout ||
                af.FramePtr->sample_rate != @is.audio_src.Frequency ||
                (wanted_nb_samples != af.FramePtr->nb_samples && @is.swr_ctx == null))
            {
                fixed (SwrContext** is_swr_ctx = &@is.swr_ctx)
                    ffmpeg.swr_free(is_swr_ctx);

                @is.swr_ctx = ffmpeg.swr_alloc_set_opts(null,
                                                 @is.audio_tgt.Layout, @is.audio_tgt.SampleFormat, @is.audio_tgt.Frequency,
                                                 dec_channel_layout, (AVSampleFormat)af.FramePtr->format, af.FramePtr->sample_rate,
                                                 0, null);

                if (@is.swr_ctx == null || ffmpeg.swr_init(@is.swr_ctx) < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR,
                           $"Cannot create sample rate converter for conversion of {af.FramePtr->sample_rate} Hz " +
                           $"{ffmpeg.av_get_sample_fmt_name((AVSampleFormat)af.FramePtr->format)} {af.FramePtr->channels} channels to " +
                           $"{@is.audio_tgt.Frequency} Hz {ffmpeg.av_get_sample_fmt_name(@is.audio_tgt.SampleFormat)} {@is.audio_tgt.Channels} channels!\n");

                    fixed (SwrContext** is_swr_ctx = &@is.swr_ctx)
                        ffmpeg.swr_free(is_swr_ctx);

                    return -1;
                }
                @is.audio_src.Layout = dec_channel_layout;
                @is.audio_src.Channels = af.FramePtr->channels;
                @is.audio_src.Frequency = af.FramePtr->sample_rate;
                @is.audio_src.SampleFormat = (AVSampleFormat)af.FramePtr->format;
            }

            if (@is.swr_ctx != null)
            {
                var @in = af.FramePtr->extended_data;

                int out_count = (int)((long)wanted_nb_samples * @is.audio_tgt.Frequency / af.FramePtr->sample_rate + 256);
                int out_size = ffmpeg.av_samples_get_buffer_size(null, @is.audio_tgt.Channels, out_count, @is.audio_tgt.SampleFormat, 0);
                int len2;
                if (out_size < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "av_samples_get_buffer_size() failed\n");
                    return -1;
                }
                if (wanted_nb_samples != af.FramePtr->nb_samples)
                {
                    if (ffmpeg.swr_set_compensation(@is.swr_ctx, (wanted_nb_samples - af.FramePtr->nb_samples) * @is.audio_tgt.Frequency / af.FramePtr->sample_rate,
                                                wanted_nb_samples * @is.audio_tgt.Frequency / af.FramePtr->sample_rate) < 0)
                    {
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "swr_set_compensation() failed\n");
                        return -1;
                    }
                }

                if (@is.audio_buf1 == null)
                {
                    @is.audio_buf1 = (byte*)ffmpeg.av_mallocz((ulong)out_size);
                    @is.audio_buf1_size = (uint)out_size;
                }

                if (@is.audio_buf1_size < out_size && @is.audio_buf1 != null)
                {
                    ffmpeg.av_free(@is.audio_buf1);
                    @is.audio_buf1 = (byte*)ffmpeg.av_mallocz((ulong)out_size);
                    @is.audio_buf1_size = (uint)out_size;
                }

                fixed (byte** @out = &@is.audio_buf1)
                    len2 = ffmpeg.swr_convert(@is.swr_ctx, @out, out_count, @in, af.FramePtr->nb_samples);

                if (len2 < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "swr_convert() failed\n");
                    return -1;
                }
                if (len2 == out_count)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, "audio buffer is probably too small\n");
                    if (ffmpeg.swr_init(@is.swr_ctx) < 0)
                    {
                        fixed (SwrContext** is_swr_ctx = &@is.swr_ctx)
                            ffmpeg.swr_free(is_swr_ctx);
                    }
                }

                @is.audio_buf = @is.audio_buf1;
                resampled_data_size = len2 * @is.audio_tgt.Channels * ffmpeg.av_get_bytes_per_sample(@is.audio_tgt.SampleFormat);
            }
            else
            {
                @is.audio_buf = af.FramePtr->data[0];
                resampled_data_size = data_size;
            }

            audio_clock0 = @is.audio_clock;

            /* update the audio clock with the pts */
            if (!double.IsNaN(af.Pts))
                @is.audio_clock = af.Pts + (double)af.FramePtr->nb_samples / af.FramePtr->sample_rate;
            else
                @is.audio_clock = double.NaN;

            @is.audio_clock_serial = af.Serial;
            if (Debugger.IsAttached)
            {
                Console.WriteLine($"audio: delay={(@is.audio_clock - last_audio_clock),-8:0.####} clock={@is.audio_clock,-8:0.####} clock0={audio_clock0,-8:0.####}");
                last_audio_clock = @is.audio_clock;
            }

            return resampled_data_size;
        }

        /* prepare a new audio buffer */
        static void sdl_audio_callback(IntPtr opaque, IntPtr stream, int len)
        {
            var @is = GlobalVideoState;
            int audio_size, len1;

            audio_callback_time = ffmpeg.av_gettime_relative();

            while (len > 0)
            {
                if (@is.audio_buf_index >= @is.audio_buf_size)
                {
                    audio_size = audio_decode_frame(@is);
                    if (audio_size < 0)
                    {
                        /* if error, just output silence */
                        @is.audio_buf = null;
                        @is.audio_buf_size = (uint)(Constants.SDL_AUDIO_MIN_BUFFER_SIZE / @is.audio_tgt.FrameSize * @is.audio_tgt.FrameSize);
                    }
                    else
                    {
                        if (@is.show_mode != ShowMode.SHOW_MODE_VIDEO)
                            update_sample_display(@is, (short*)@is.audio_buf, audio_size);
                        @is.audio_buf_size = (uint)audio_size;
                    }
                    @is.audio_buf_index = 0;
                }
                len1 = (int)(@is.audio_buf_size - @is.audio_buf_index);
                if (len1 > len)
                    len1 = len;

                if (!@is.muted && @is.audio_buf != null && @is.audio_volume == SDL.SDL_MIX_MAXVOLUME)
                {
                    var dest = (byte*)stream;
                    var source = (@is.audio_buf + @is.audio_buf_index);
                    for (var b = 0; b < len1; b++)
                        dest[b] = source[b];
                }
                else
                {
                    var target = (byte*)stream;
                    for (var b = 0; b < len1; b++)
                        target[b] = 0;

                    if (!@is.muted && @is.audio_buf != null)
                        SDLNatives.SDL_MixAudioFormat((byte*)stream, @is.audio_buf + @is.audio_buf_index, SDL.AUDIO_S16SYS, (uint)len1, @is.audio_volume);
                }

                len -= len1;
                stream += len1;
                @is.audio_buf_index += len1;
            }
            @is.audio_write_buf_size = (int)(@is.audio_buf_size - @is.audio_buf_index);
            /* Let's assume the audio driver that is used by SDL has two periods. */
            if (!double.IsNaN(@is.audio_clock))
            {
                @is.audclk.SetAt(@is.audio_clock - (double)(2 * @is.audio_hw_buf_size + @is.audio_write_buf_size) / @is.audio_tgt.BytesPerSecond, @is.audio_clock_serial, audio_callback_time / 1000000.0);
                @is.extclk.SyncToSlave(@is.audclk);
            }
        }

        static int audio_open(VideoState @is, long wanted_channel_layout, int wanted_nb_channels, int wanted_sample_rate, ref AudioParams audio_hw_params)
        {
            SDL.SDL_AudioSpec wanted_spec = new();
            SDL.SDL_AudioSpec spec = new();

            var next_nb_channels = new[] { 0, 0, 1, 6, 2, 6, 4, 6 };
            var next_sample_rates = new[] { 0, 44100, 48000, 96000, 192000 };
            int next_sample_rate_idx = next_sample_rates.Length - 1;

            var env = Environment.GetEnvironmentVariable("SDL_AUDIO_CHANNELS");
            if (!string.IsNullOrWhiteSpace(env))
            {
                wanted_nb_channels = int.Parse(env);
                wanted_channel_layout = ffmpeg.av_get_default_channel_layout(wanted_nb_channels);
            }

            if (wanted_channel_layout == 0 || wanted_nb_channels != ffmpeg.av_get_channel_layout_nb_channels((ulong)wanted_channel_layout))
            {
                wanted_channel_layout = ffmpeg.av_get_default_channel_layout(wanted_nb_channels);
                wanted_channel_layout &= ~ffmpeg.AV_CH_LAYOUT_STEREO_DOWNMIX;
            }

            wanted_nb_channels = ffmpeg.av_get_channel_layout_nb_channels((ulong)wanted_channel_layout);
            wanted_spec.channels = (byte)wanted_nb_channels;
            wanted_spec.freq = wanted_sample_rate;
            if (wanted_spec.freq <= 0 || wanted_spec.channels <= 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "Invalid sample rate or channel count!\n");
                return -1;
            }

            while (next_sample_rate_idx != 0 && next_sample_rates[next_sample_rate_idx] >= wanted_spec.freq)
                next_sample_rate_idx--;

            wanted_spec.format = SDL.AUDIO_S16SYS;
            wanted_spec.silence = 0;
            wanted_spec.samples = (ushort)Math.Max(Constants.SDL_AUDIO_MIN_BUFFER_SIZE, 2 << ffmpeg.av_log2((uint)(wanted_spec.freq / Constants.SDL_AUDIO_MAX_CALLBACKS_PER_SEC)));
            wanted_spec.callback = sdl_audio_callback;
            // wanted_spec.userdata = GCHandle.ToIntPtr(VideoStateHandle);
            while ((audio_dev = SDL.SDL_OpenAudioDevice(null, 0, ref wanted_spec, out spec, (int)(SDL.SDL_AUDIO_ALLOW_FREQUENCY_CHANGE | SDL.SDL_AUDIO_ALLOW_CHANNELS_CHANGE))) == 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"SDL_OpenAudio ({wanted_spec.channels} channels, {wanted_spec.freq} Hz): {SDL.SDL_GetError()}\n");
                wanted_spec.channels = (byte)next_nb_channels[Math.Min(7, (int)wanted_spec.channels)];
                if (wanted_spec.channels == 0)
                {
                    wanted_spec.freq = next_sample_rates[next_sample_rate_idx--];
                    wanted_spec.channels = (byte)wanted_nb_channels;
                    if (wanted_spec.freq == 0)
                    {
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "No more combinations to try, audio open failed\n");
                        return -1;
                    }
                }

                wanted_channel_layout = ffmpeg.av_get_default_channel_layout(wanted_spec.channels);
            }
            if (spec.format != SDL.AUDIO_S16SYS)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR,
                       $"SDL advised audio format {spec.format} is not supported!\n");
                return -1;
            }
            if (spec.channels != wanted_spec.channels)
            {
                wanted_channel_layout = ffmpeg.av_get_default_channel_layout(spec.channels);
                if (wanted_channel_layout == 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR,
                           $"SDL advised channel count {spec.channels} is not supported!\n");
                    return -1;
                }
            }

            audio_hw_params.SampleFormat = AVSampleFormat.AV_SAMPLE_FMT_S16;
            audio_hw_params.Frequency = spec.freq;
            audio_hw_params.Layout = wanted_channel_layout;
            audio_hw_params.Channels = spec.channels;
            audio_hw_params.FrameSize = ffmpeg.av_samples_get_buffer_size(null, audio_hw_params.Channels, 1, audio_hw_params.SampleFormat, 1);
            audio_hw_params.BytesPerSecond = ffmpeg.av_samples_get_buffer_size(null, audio_hw_params.Channels, audio_hw_params.Frequency, audio_hw_params.SampleFormat, 1);
            if (audio_hw_params.BytesPerSecond <= 0 || audio_hw_params.FrameSize <= 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, "av_samples_get_buffer_size failed\n");
                return -1;
            }

            return (int)spec.size;
        }

        /* open a given stream. Return 0 if OK */
        static int stream_component_open(VideoState @is, int stream_index)
        {
            AVFormatContext* ic = @is.ic;
            AVCodecContext* avctx;
            AVCodec* codec;
            string forced_codec_name = null;
            AVDictionary* opts = null;
            AVDictionaryEntry* t = null;
            int sample_rate, nb_channels;
            long channel_layout;
            int ret = 0;
            int stream_lowres = lowres;

            if (stream_index < 0 || stream_index >= ic->nb_streams)
                return -1;

            avctx = ffmpeg.avcodec_alloc_context3(null);
            if (avctx == null)
                return ffmpeg.AVERROR(ffmpeg.ENOMEM);

            ret = ffmpeg.avcodec_parameters_to_context(avctx, ic->streams[stream_index]->codecpar);
            if (ret < 0) goto fail;
            avctx->pkt_timebase = ic->streams[stream_index]->time_base;

            codec = ffmpeg.avcodec_find_decoder(avctx->codec_id);

            switch (avctx->codec_type)
            {
                case AVMediaType.AVMEDIA_TYPE_AUDIO: @is.last_audio_stream = stream_index; forced_codec_name = audio_codec_name; break;
                case AVMediaType.AVMEDIA_TYPE_SUBTITLE: @is.last_subtitle_stream = stream_index; forced_codec_name = subtitle_codec_name; break;
                case AVMediaType.AVMEDIA_TYPE_VIDEO: @is.last_video_stream = stream_index; forced_codec_name = video_codec_name; break;
            }
            if (!string.IsNullOrWhiteSpace(forced_codec_name))
                codec = ffmpeg.avcodec_find_decoder_by_name(forced_codec_name);

            if (codec == null)
            {
                if (!string.IsNullOrWhiteSpace(forced_codec_name)) ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING,
                                              $"No codec could be found with name '{forced_codec_name}'\n");
                else ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING,
                            $"No decoder could be found for codec {ffmpeg.avcodec_get_name(avctx->codec_id)}\n");
                ret = ffmpeg.AVERROR(ffmpeg.EINVAL);
                goto fail;
            }

            avctx->codec_id = codec->id;
            if (stream_lowres > codec->max_lowres)
            {
                ffmpeg.av_log(avctx, ffmpeg.AV_LOG_WARNING, $"The maximum value for lowres supported by the decoder is {codec->max_lowres}\n");
                stream_lowres = codec->max_lowres;
            }

            avctx->lowres = stream_lowres;

            if (fast != 0)
                avctx->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;

            opts = Helpers.filter_codec_opts(codec_opts, avctx->codec_id, ic, ic->streams[stream_index], codec);
            if (ffmpeg.av_dict_get(opts, "threads", null, 0) == null)
                ffmpeg.av_dict_set(&opts, "threads", "auto", 0);

            if (stream_lowres != 0)
                ffmpeg.av_dict_set_int(&opts, "lowres", stream_lowres, 0);

            if (avctx->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO || avctx->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                ffmpeg.av_dict_set(&opts, "refcounted_frames", "1", 0);

            if ((ret = ffmpeg.avcodec_open2(avctx, codec, &opts)) < 0)
            {
                goto fail;
            }
            if ((t = ffmpeg.av_dict_get(opts, "", null, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Marshal.PtrToStringUTF8((IntPtr)t->key);
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"Option {key} not found.\n");
                ret = ffmpeg.AVERROR_OPTION_NOT_FOUND;
                goto fail;
            }

            @is.eof = false;
            ic->streams[stream_index]->discard = AVDiscard.AVDISCARD_DEFAULT;
            switch (avctx->codec_type)
            {
                case AVMediaType.AVMEDIA_TYPE_AUDIO:
                    {
                        AVFilterContext* sink;
                        @is.audio_filter_src.Frequency = avctx->sample_rate;
                        @is.audio_filter_src.Channels = avctx->channels;
                        @is.audio_filter_src.Layout = (long)get_valid_channel_layout(avctx->channel_layout, avctx->channels);
                        @is.audio_filter_src.SampleFormat = avctx->sample_fmt;
                        if ((ret = configure_audio_filters(@is, afilters, false)) < 0)
                            goto fail;
                        sink = @is.out_audio_filter;
                        sample_rate = ffmpeg.av_buffersink_get_sample_rate(sink);
                        nb_channels = ffmpeg.av_buffersink_get_channels(sink);
                        channel_layout = (long)ffmpeg.av_buffersink_get_channel_layout(sink);
                    }

                    sample_rate = avctx->sample_rate;
                    nb_channels = avctx->channels;
                    channel_layout = (long)avctx->channel_layout;

                    /* prepare audio output */
                    if ((ret = audio_open(@is, channel_layout, nb_channels, sample_rate, ref @is.audio_tgt)) < 0)
                        goto fail;

                    @is.audio_hw_buf_size = ret;
                    @is.audio_src = @is.audio_tgt;
                    @is.audio_buf_size = 0;
                    @is.audio_buf_index = 0;

                    /* init averaging filter */
                    @is.audio_diff_avg_coef = Math.Exp(Math.Log(0.01) / Constants.AUDIO_DIFF_AVG_NB);
                    @is.audio_diff_avg_count = 0;

                    /* since we do not have a precise anough audio FIFO fullness,
                       we correct audio sync only if larger than this threshold */
                    @is.audio_diff_threshold = (double)(@is.audio_hw_buf_size) / @is.audio_tgt.BytesPerSecond;

                    @is.audio_stream = stream_index;
                    @is.audio_st = ic->streams[stream_index];

                    @is.auddec = new(avctx, @is.audioq, @is.continue_read_thread, decoder_reorder_pts);
                    if ((@is.ic->iformat->flags & (ffmpeg.AVFMT_NOBINSEARCH | ffmpeg.AVFMT_NOGENSEARCH | ffmpeg.AVFMT_NO_BYTE_SEEK)) != 0 &&
                        @is.ic->iformat->read_seek.Pointer == IntPtr.Zero)
                    {
                        @is.auddec.StartPts = @is.audio_st->start_time;
                        @is.auddec.StartPtsTimeBase = @is.audio_st->time_base;
                    }

                    if ((ret = @is.auddec.Start(audio_thread, "audio_decoder", @is)) < 0)
                        goto @out;
                    SDL.SDL_PauseAudioDevice(audio_dev, 0);
                    break;
                case AVMediaType.AVMEDIA_TYPE_VIDEO:
                    @is.video_stream = stream_index;
                    @is.video_st = ic->streams[stream_index];

                    @is.viddec = new(avctx, @is.videoq, @is.continue_read_thread, decoder_reorder_pts);
                    if ((ret = @is.viddec.Start(video_thread, "video_decoder", @is)) < 0)
                        goto @out;
                    @is.queue_attachments_req = true;
                    break;
                case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                    @is.subtitle_stream = stream_index;
                    @is.subtitle_st = ic->streams[stream_index];

                    @is.subdec = new(avctx, @is.subtitleq, @is.continue_read_thread, decoder_reorder_pts);
                    if ((ret = @is.subdec.Start(subtitle_thread, "subtitle_decoder", @is)) < 0)
                        goto @out;
                    break;
                default:
                    break;
            }
            goto @out;

        fail:
            ffmpeg.avcodec_free_context(&avctx);
        @out:
            ffmpeg.av_dict_free(&opts);

            return ret;
        }

        static int decode_interrupt_cb(void* ctx)
        {
            var @is = GlobalVideoState;
            return @is.abort_request ? 1 : 0;
        }

        static bool stream_has_enough_packets(AVStream* st, int stream_id, PacketQueue queue)
        {
            return stream_id < 0 ||
                   queue.IsClosed ||
                   (st->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) != 0 ||
                   queue.Count > Constants.MIN_FRAMES && (queue.Duration == 0 || ffmpeg.av_q2d(st->time_base) * queue.Duration > 1.0);
        }

        static bool is_realtime(AVFormatContext* s)
        {
            var iformat = Helpers.PtrToString(s->iformat->name);
            if (iformat == "rtp" || iformat == "rtsp" || iformat == "sdp")
                return true;

            var url = Helpers.PtrToString(s->url);
            url = string.IsNullOrEmpty(url) ? string.Empty : url;

            if (s->pb != null && (url.StartsWith("rtp:") || url.StartsWith("udp:")))
                return true;

            return false;
        }

        /* this thread gets the stream from the disk or the network */
        static void read_thread(object arg)
        {
            var @is = arg as VideoState;
            AVFormatContext* ic = null;
            int err, i, ret;
            var st_index = new int[(int)AVMediaType.AVMEDIA_TYPE_NB];
            // AVPacket pkt1;
            // AVPacket* pkt = &pkt1;
            long stream_start_time;
            bool pkt_in_play_range = false;
            AVDictionaryEntry* t;
            bool scan_all_pmts_set = false;
            long pkt_ts;

            for (var b = 0; b < st_index.Length; b++)
                st_index[b] = -1;

            @is.eof = false;

            ic = ffmpeg.avformat_alloc_context();
            if (ic == null)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, "Could not allocate context.\n");
                ret = ffmpeg.AVERROR(ffmpeg.ENOMEM);
                goto fail;
            }

            ic->interrupt_callback.callback = (AVIOInterruptCB_callback)decode_interrupt_cb;
            // ic->interrupt_callback.opaque = (void*)GCHandle.ToIntPtr(VideoStateHandle);
            if (ffmpeg.av_dict_get(format_opts, "scan_all_pmts", null, ffmpeg.AV_DICT_MATCH_CASE) == null)
            {
                fixed (AVDictionary** ref_format_opts = &format_opts)
                    ffmpeg.av_dict_set(ref_format_opts, "scan_all_pmts", "1", ffmpeg.AV_DICT_DONT_OVERWRITE);
                scan_all_pmts_set = true;
            }

            fixed (AVDictionary** ref_format_opts = &format_opts)
                err = ffmpeg.avformat_open_input(&ic, @is.filename, @is.iformat, ref_format_opts);

            if (err < 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"{@is.filename}: {Helpers.print_error(err)}\n");
                ret = -1;
                goto fail;
            }
            if (scan_all_pmts_set)
            {
                fixed (AVDictionary** ref_format_opts = &format_opts)
                    ffmpeg.av_dict_set(ref_format_opts, "scan_all_pmts", null, ffmpeg.AV_DICT_MATCH_CASE);
            }

            if ((t = ffmpeg.av_dict_get(format_opts, "", null, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"Option {Helpers.PtrToString(t->key)} not found.\n");
                ret = ffmpeg.AVERROR_OPTION_NOT_FOUND;
                goto fail;
            }

            @is.ic = ic;

            if (genpts)
                ic->flags |= ffmpeg.AVFMT_FLAG_GENPTS;

            ffmpeg.av_format_inject_global_side_data(ic);

            if (find_stream_info)
            {
                AVDictionary** opts = Helpers.setup_find_stream_info_opts(ic, codec_opts);
                int orig_nb_streams = (int)ic->nb_streams;

                err = ffmpeg.avformat_find_stream_info(ic, opts);

                for (i = 0; i < orig_nb_streams; i++)
                    ffmpeg.av_dict_free(&opts[i]);

                ffmpeg.av_freep(&opts);

                if (err < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"{@is.filename}: could not find codec parameters\n");
                    ret = -1;
                    goto fail;
                }
            }

            if (ic->pb != null)
                ic->pb->eof_reached = 0; // FIXME hack, ffplay maybe should not use avio_feof() to test for the end

            if (seek_by_bytes < 0)
                seek_by_bytes = ((ic->iformat->flags & ffmpeg.AVFMT_TS_DISCONT) > 0 && Helpers.PtrToString(ic->iformat->name) != "ogg") ? 1 : 0;

            @is.max_frame_duration = (ic->iformat->flags & ffmpeg.AVFMT_TS_DISCONT) != 0 ? 10.0 : 3600.0;

            if (string.IsNullOrWhiteSpace(window_title) && (t = ffmpeg.av_dict_get(ic->metadata, "title", null, 0)) != null)
                window_title = $"{Helpers.PtrToString(t->value)} - {input_filename}";

            /* if seeking requested, we execute it */
            if (start_time != ffmpeg.AV_NOPTS_VALUE)
            {
                long timestamp;

                timestamp = start_time;
                /* add the stream start time */
                if (ic->start_time != ffmpeg.AV_NOPTS_VALUE)
                    timestamp += ic->start_time;
                ret = ffmpeg.avformat_seek_file(ic, -1, long.MinValue, timestamp, long.MaxValue, 0);
                if (ret < 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"{@is.filename}: could not seek to position {((double)timestamp / ffmpeg.AV_TIME_BASE)}\n");
                }
            }

            @is.realtime = is_realtime(ic);

            if (show_status != 0)
                ffmpeg.av_dump_format(ic, 0, @is.filename, 0);

            for (i = 0; i < ic->nb_streams; i++)
            {
                AVStream* st = ic->streams[i];
                var type = (int)st->codecpar->codec_type;
                st->discard = AVDiscard.AVDISCARD_ALL;
                if (type >= 0 && wanted_stream_spec[type] != null && st_index[type] == -1)
                    if (ffmpeg.avformat_match_stream_specifier(ic, st, wanted_stream_spec[type]) > 0)
                        st_index[type] = i;
            }
            for (i = 0; i < (int)AVMediaType.AVMEDIA_TYPE_NB; i++)
            {
                if (wanted_stream_spec[i] != null && st_index[i] == -1)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"Stream specifier {wanted_stream_spec[i]} does not match any {ffmpeg.av_get_media_type_string((AVMediaType)i)} stream\n");
                    st_index[i] = int.MaxValue;
                }
            }

            if (!video_disable)
                st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO] =
                    ffmpeg.av_find_best_stream(ic, AVMediaType.AVMEDIA_TYPE_VIDEO,
                                        st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO], -1, null, 0);
            if (!audio_disable)
                st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO] =
                    ffmpeg.av_find_best_stream(ic, AVMediaType.AVMEDIA_TYPE_AUDIO,
                                        st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO],
                                        st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO],
                                        null, 0);
            if (!video_disable && !subtitle_disable)
                st_index[(int)AVMediaType.AVMEDIA_TYPE_SUBTITLE] =
                    ffmpeg.av_find_best_stream(ic, AVMediaType.AVMEDIA_TYPE_SUBTITLE,
                                        st_index[(int)AVMediaType.AVMEDIA_TYPE_SUBTITLE],
                                        (st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO] >= 0 ?
                                         st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO] :
                                         st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO]),
                                        null, 0);

            @is.show_mode = show_mode;
            if (st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO] >= 0)
            {
                AVStream* st = ic->streams[st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO]];
                AVCodecParameters* codecpar = st->codecpar;
                AVRational sar = ffmpeg.av_guess_sample_aspect_ratio(ic, st, null);
                if (codecpar->width != 0)
                    set_default_window_size(codecpar->width, codecpar->height, sar);
            }

            /* open the streams */
            if (st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO] >= 0)
            {
                stream_component_open(@is, st_index[(int)AVMediaType.AVMEDIA_TYPE_AUDIO]);
            }

            ret = -1;
            if (st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO] >= 0)
            {
                ret = stream_component_open(@is, st_index[(int)AVMediaType.AVMEDIA_TYPE_VIDEO]);
            }

            if (@is.show_mode == ShowMode.SHOW_MODE_NONE)
                @is.show_mode = ret >= 0 ? ShowMode.SHOW_MODE_VIDEO : ShowMode.SHOW_MODE_RDFT;

            if (st_index[(int)AVMediaType.AVMEDIA_TYPE_SUBTITLE] >= 0)
            {
                stream_component_open(@is, st_index[(int)AVMediaType.AVMEDIA_TYPE_SUBTITLE]);
            }

            if (@is.video_stream < 0 && @is.audio_stream < 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, $"Failed to open file '{@is.filename}' or configure filtergraph\n");
                ret = -1;
                goto fail;
            }

            if (infinite_buffer < 0 && @is.realtime)
                infinite_buffer = 1;

            while (true)
            {
                if (@is.abort_request)
                    break;
                if (@is.paused != @is.last_paused)
                {
                    @is.last_paused = @is.paused;
                    if (@is.paused)
                        @is.read_pause_return = ffmpeg.av_read_pause(ic);
                    else
                        ffmpeg.av_read_play(ic);
                }

                if (@is.paused &&
                        (Helpers.PtrToString(ic->iformat->name) == "rtsp" ||
                         (ic->pb != null && input_filename.StartsWith("mmsh:"))))
                {
                    /* wait 10 ms to avoid trying to get another packet */
                    /* XXX: horrible */
                    SDL.SDL_Delay(10);
                    continue;
                }

                if (@is.seek_req)
                {
                    long seek_target = @is.seek_pos;
                    long seek_min = @is.seek_rel > 0 ? seek_target - @is.seek_rel + 2 : long.MinValue;
                    long seek_max = @is.seek_rel < 0 ? seek_target - @is.seek_rel - 2 : long.MaxValue;
                    // FIXME the +-2 is due to rounding being not done in the correct direction in generation
                    //      of the seek_pos/seek_rel variables

                    ret = ffmpeg.avformat_seek_file(@is.ic, -1, seek_min, seek_target, seek_max, @is.seek_flags);
                    if (ret < 0)
                    {
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_ERROR, $"{Helpers.PtrToString(@is.ic->url)}: error while seeking\n");
                    }
                    else
                    {
                        if (@is.audio_stream >= 0)
                        {
                            @is.audioq.Clear();
                            @is.audioq.PutFlush();
                        }
                        if (@is.subtitle_stream >= 0)
                        {
                            @is.subtitleq.Clear();
                            @is.subtitleq.PutFlush();
                        }
                        if (@is.video_stream >= 0)
                        {
                            @is.videoq.Clear();
                            @is.videoq.PutFlush();
                        }
                        if ((@is.seek_flags & ffmpeg.AVSEEK_FLAG_BYTE) != 0)
                        {
                            @is.extclk.Set(double.NaN, 0);
                        }
                        else
                        {
                            @is.extclk.Set(seek_target / (double)ffmpeg.AV_TIME_BASE, 0);
                        }
                    }
                    @is.seek_req = false;
                    @is.queue_attachments_req = true;
                    @is.eof = false;

                    if (@is.paused)
                        step_to_next_frame(@is);
                }
                if (@is.queue_attachments_req)
                {
                    if (@is.video_st != null && (@is.video_st->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) != 0)
                    {
                        var copy = ffmpeg.av_packet_clone(&@is.video_st->attached_pic);
                        @is.videoq.Put(copy);
                        @is.videoq.PutNull(@is.video_stream);
                    }

                    @is.queue_attachments_req = false;
                }

                /* if the queue are full, no need to read more */
                if (infinite_buffer < 1 &&
                      (@is.audioq.Size + @is.videoq.Size + @is.subtitleq.Size > Constants.MAX_QUEUE_SIZE
                    || (stream_has_enough_packets(@is.audio_st, @is.audio_stream, @is.audioq) &&
                        stream_has_enough_packets(@is.video_st, @is.video_stream, @is.videoq) &&
                        stream_has_enough_packets(@is.subtitle_st, @is.subtitle_stream, @is.subtitleq))))
                {
                    /* wait 10 ms */
                    @is.continue_read_thread.WaitOne(10);
                    continue;
                }
                if (!@is.paused &&
                    (@is.audio_st == null || (@is.auddec.HasFinished == @is.audioq.Serial && @is.sampq.PendingCount == 0)) &&
                    (@is.video_st == null || (@is.viddec.HasFinished == @is.videoq.Serial && @is.pictq.PendingCount == 0)))
                {
                    if (loop != 1 && (loop == 0 || (--loop) > 0))
                    {
                        stream_seek(@is, start_time != ffmpeg.AV_NOPTS_VALUE ? start_time : 0, 0, 0);
                    }
                    else if (autoexit)
                    {
                        ret = ffmpeg.AVERROR_EOF;
                        goto fail;
                    }
                }

                var pkt = ffmpeg.av_packet_alloc();
                ret = ffmpeg.av_read_frame(ic, pkt);
                if (ret < 0)
                {
                    if ((ret == ffmpeg.AVERROR_EOF || ffmpeg.avio_feof(ic->pb) != 0) && !@is.eof)
                    {
                        if (@is.video_stream >= 0)
                            @is.videoq.PutNull(@is.video_stream);
                        if (@is.audio_stream >= 0)
                            @is.audioq.PutNull(@is.audio_stream);
                        if (@is.subtitle_stream >= 0)
                            @is.subtitleq.PutNull(@is.subtitle_stream);
                        @is.eof = true;
                    }
                    if (ic->pb != null && ic->pb->error != 0)
                    {
                        if (autoexit)
                            goto fail;
                        else
                            break;
                    }

                    @is.continue_read_thread.WaitOne(10);

                    continue;
                }
                else
                {
                    @is.eof = false;
                }

                /* check if packet is in play range specified by user, then queue, otherwise discard */
                stream_start_time = ic->streams[pkt->stream_index]->start_time;
                pkt_ts = pkt->pts == ffmpeg.AV_NOPTS_VALUE ? pkt->dts : pkt->pts;
                pkt_in_play_range = duration == ffmpeg.AV_NOPTS_VALUE ||
                        (pkt_ts - (stream_start_time != ffmpeg.AV_NOPTS_VALUE ? stream_start_time : 0)) *
                        ffmpeg.av_q2d(ic->streams[pkt->stream_index]->time_base) -
                        (double)(start_time != ffmpeg.AV_NOPTS_VALUE ? start_time : 0) / 1000000
                        <= ((double)duration / 1000000);
                if (pkt->stream_index == @is.audio_stream && pkt_in_play_range)
                {
                    @is.audioq.Put(pkt);
                }
                else if (pkt->stream_index == @is.video_stream && pkt_in_play_range
                         && (@is.video_st->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) == 0)
                {
                    @is.videoq.Put(pkt);
                }
                else if (pkt->stream_index == @is.subtitle_stream && pkt_in_play_range)
                {
                    @is.subtitleq.Put(pkt);
                }
                else
                {
                    ffmpeg.av_packet_unref(pkt);
                }
            }

            ret = 0;
        fail:
            if (ic != null && @is.ic == null)
                ffmpeg.avformat_close_input(&ic);

            if (ret != 0)
            {
                SDL.SDL_Event evt = new();
                evt.type = (SDL.SDL_EventType)FF_QUIT_EVENT;
                // evt.user.data1 = GCHandle.ToIntPtr(VideoStateHandle);
                SDL.SDL_PushEvent(ref evt);
            }

            return; // 0;
        }

        static VideoState stream_open(string filename, AVInputFormat* iformat)
        {
            var @is = new VideoState();

            @is.last_video_stream = @is.video_stream = -1;
            @is.last_audio_stream = @is.audio_stream = -1;
            @is.last_subtitle_stream = @is.subtitle_stream = -1;
            @is.filename = filename;
            if (string.IsNullOrWhiteSpace(@is.filename))
                goto fail;

            @is.iformat = iformat;
            @is.ytop = 0;
            @is.xleft = 0;

            /* start video display */
            @is.pictq = new(@is.videoq, Constants.VIDEO_PICTURE_QUEUE_SIZE, true);
            @is.subpq = new(@is.subtitleq, Constants.SUBPICTURE_QUEUE_SIZE, false);
            @is.sampq = new(@is.audioq, Constants.SAMPLE_QUEUE_SIZE, true);

            @is.vidclk = new Clock(@is.videoq);
            @is.audclk = new Clock(@is.audioq);
            @is.extclk = new Clock(@is.extclk);

            @is.audio_clock_serial = -1;
            if (startup_volume < 0)
                ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"-volume={startup_volume} < 0, setting to 0\n");

            if (startup_volume > 100)
                ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"-volume={startup_volume} > 100, setting to 100\n");

            startup_volume = Helpers.av_clip(startup_volume, 0, 100);
            startup_volume = Helpers.av_clip(SDL.SDL_MIX_MAXVOLUME * startup_volume / 100, 0, SDL.SDL_MIX_MAXVOLUME);
            @is.audio_volume = startup_volume;
            @is.muted = false;
            @is.av_sync_type = av_sync_type;
            @is.read_tid = new Thread(read_thread) { IsBackground = true, Name = nameof(read_thread) };
            @is.read_tid.Start(@is);

            if (@is.read_tid == null)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, "SDL_CreateThread(): (new Thread())\n");
                goto fail;
            }

            return @is;

        fail:
            stream_close(@is);
            return null;
        }

        static void stream_cycle_channel(VideoState @is, int codec_type)
        {
            AVFormatContext* ic = @is.ic;
            int start_index, stream_index;
            int old_index;
            AVStream* st;
            AVProgram* p = null;
            int nb_streams = (int)@is.ic->nb_streams;

            if (codec_type == (int)AVMediaType.AVMEDIA_TYPE_VIDEO)
            {
                start_index = @is.last_video_stream;
                old_index = @is.video_stream;
            }
            else if (codec_type == (int)AVMediaType.AVMEDIA_TYPE_AUDIO)
            {
                start_index = @is.last_audio_stream;
                old_index = @is.audio_stream;
            }
            else
            {
                start_index = @is.last_subtitle_stream;
                old_index = @is.subtitle_stream;
            }
            stream_index = start_index;

            if (codec_type != (int)AVMediaType.AVMEDIA_TYPE_VIDEO && @is.video_stream != -1)
            {
                p = ffmpeg.av_find_program_from_stream(ic, null, @is.video_stream);
                if (p != null)
                {
                    nb_streams = (int)p->nb_stream_indexes;
                    for (start_index = 0; start_index < nb_streams; start_index++)
                        if (p->stream_index[start_index] == stream_index)
                            break;
                    if (start_index == nb_streams)
                        start_index = -1;
                    stream_index = start_index;
                }
            }

            for (; ; )
            {
                if (++stream_index >= nb_streams)
                {
                    if (codec_type == (int)AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                    {
                        stream_index = -1;
                        @is.last_subtitle_stream = -1;
                        goto the_end;
                    }
                    if (start_index == -1)
                        return;
                    stream_index = 0;
                }
                if (stream_index == start_index)
                    return;
                st = @is.ic->streams[p != null ? p->stream_index[stream_index] : stream_index];
                if ((int)st->codecpar->codec_type == codec_type)
                {
                    /* check that parameters are OK */
                    switch ((AVMediaType)codec_type)
                    {
                        case AVMediaType.AVMEDIA_TYPE_AUDIO:
                            if (st->codecpar->sample_rate != 0 &&
                                st->codecpar->channels != 0)
                                goto the_end;
                            break;
                        case AVMediaType.AVMEDIA_TYPE_VIDEO:
                        case AVMediaType.AVMEDIA_TYPE_SUBTITLE:
                            goto the_end;
                        default:
                            break;
                    }
                }
            }
        the_end:
            if (p != null && stream_index != -1)
                stream_index = (int)p->stream_index[stream_index];
            ffmpeg.av_log(null, ffmpeg.AV_LOG_INFO, $"Switch {ffmpeg.av_get_media_type_string((AVMediaType)codec_type)} stream from #{old_index} to #{stream_index}\n");

            stream_component_close(@is, old_index);
            stream_component_open(@is, stream_index);
        }

        static void toggle_full_screen(VideoState @is)
        {
            is_full_screen = !is_full_screen;
            SDL.SDL_SetWindowFullscreen(window, (uint)(is_full_screen ? SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP : 0));
        }

        static void toggle_audio_display(VideoState @is)
        {
            int next = (int)@is.show_mode;
            do
            {
                next = (next + 1) % (int)ShowMode.SHOW_MODE_NB;
            } while (next != (int)@is.show_mode && (next == (int)ShowMode.SHOW_MODE_VIDEO && @is.video_st == null || next != (int)ShowMode.SHOW_MODE_VIDEO && @is.audio_st == null));
            if ((int)@is.show_mode != next)
            {
                @is.force_refresh = true;
                @is.show_mode = (ShowMode)next;
            }
        }

        static void refresh_loop_wait_event(VideoState @is, out SDL.SDL_Event @event)
        {
            double remaining_time = 0.0;
            SDL.SDL_PumpEvents();
            var events = new SDL.SDL_Event[1];

            while (SDL.SDL_PeepEvents(events, 1, SDL.SDL_eventaction.SDL_GETEVENT, SDL.SDL_EventType.SDL_FIRSTEVENT, SDL.SDL_EventType.SDL_LASTEVENT) == 0)
            {
                if (!cursor_hidden && ffmpeg.av_gettime_relative() - cursor_last_shown > Constants.CURSOR_HIDE_DELAY)
                {
                    SDL.SDL_ShowCursor(0);
                    cursor_hidden = true;
                }

                if (remaining_time > 0.0)
                    ffmpeg.av_usleep((uint)(remaining_time * 1000000.0));

                remaining_time = Constants.REFRESH_RATE;

                if (@is.show_mode != ShowMode.SHOW_MODE_NONE && (!@is.paused || @is.force_refresh))
                    video_refresh(@is, ref remaining_time);

                SDL.SDL_PumpEvents();
            }

            @event = events[0];
        }

        static void seek_chapter(VideoState @is, int incr)
        {
            long pos = (long)(get_master_clock(@is) * ffmpeg.AV_TIME_BASE);
            int i;

            if (@is.ic->nb_chapters <= 0)
                return;

            /* find the current chapter */
            for (i = 0; i < @is.ic->nb_chapters; i++)
            {
                AVChapter* ch = @is.ic->chapters[i];
                if (ffmpeg.av_compare_ts(pos, Constants.AV_TIME_BASE_Q, ch->start, ch->time_base) < 0)
                {
                    i--;
                    break;
                }
            }

            i += incr;
            i = Math.Max(i, 0);
            if (i >= @is.ic->nb_chapters)
                return;

            ffmpeg.av_log(null, ffmpeg.AV_LOG_VERBOSE, $"Seeking to chapter {i}.\n");
            stream_seek(@is, ffmpeg.av_rescale_q(@is.ic->chapters[i]->start, @is.ic->chapters[i]->time_base, Constants.AV_TIME_BASE_Q), 0, 0);
        }

        /* handle an event sent by the GUI */
        static void event_loop(VideoState cur_stream)
        {
            SDL.SDL_Event @event;
            double incr, pos, frac;

            while (true)
            {
                double x;
                refresh_loop_wait_event(cur_stream, out @event);
                switch ((int)@event.type)
                {
                    case (int)SDL.SDL_EventType.SDL_KEYDOWN:
                        if (exit_on_keydown || @event.key.keysym.sym == SDL.SDL_Keycode.SDLK_ESCAPE || @event.key.keysym.sym == SDL.SDL_Keycode.SDLK_q)
                        {
                            do_exit(cur_stream);
                            break;
                        }
                        // If we don't yet have a window, skip all key events, because read_thread might still be initializing...
                        if (cur_stream.width <= 0)
                            continue;
                        switch (@event.key.keysym.sym)
                        {
                            case SDL.SDL_Keycode.SDLK_f:
                                toggle_full_screen(cur_stream);
                                cur_stream.force_refresh = true;
                                break;
                            case SDL.SDL_Keycode.SDLK_p:
                            case SDL.SDL_Keycode.SDLK_SPACE:
                                toggle_pause(cur_stream);
                                break;
                            case SDL.SDL_Keycode.SDLK_m:
                                toggle_mute(cur_stream);
                                break;
                            case SDL.SDL_Keycode.SDLK_KP_MULTIPLY:
                            case SDL.SDL_Keycode.SDLK_0:
                                update_volume(cur_stream, 1, Constants.SDL_VOLUME_STEP);
                                break;
                            case SDL.SDL_Keycode.SDLK_KP_DIVIDE:
                            case SDL.SDL_Keycode.SDLK_9:
                                update_volume(cur_stream, -1, Constants.SDL_VOLUME_STEP);
                                break;
                            case SDL.SDL_Keycode.SDLK_s: // S: Step to next frame
                                step_to_next_frame(cur_stream);
                                break;
                            case SDL.SDL_Keycode.SDLK_a:
                                stream_cycle_channel(cur_stream, (int)AVMediaType.AVMEDIA_TYPE_AUDIO);
                                break;
                            case SDL.SDL_Keycode.SDLK_v:
                                stream_cycle_channel(cur_stream, (int)AVMediaType.AVMEDIA_TYPE_VIDEO);
                                break;
                            case SDL.SDL_Keycode.SDLK_c:
                                stream_cycle_channel(cur_stream, (int)AVMediaType.AVMEDIA_TYPE_VIDEO);
                                stream_cycle_channel(cur_stream, (int)AVMediaType.AVMEDIA_TYPE_AUDIO);
                                stream_cycle_channel(cur_stream, (int)AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                                break;
                            case SDL.SDL_Keycode.SDLK_t:
                                stream_cycle_channel(cur_stream, (int)AVMediaType.AVMEDIA_TYPE_SUBTITLE);
                                break;
                            case SDL.SDL_Keycode.SDLK_w:

                                if (cur_stream.show_mode == ShowMode.SHOW_MODE_VIDEO && cur_stream.vfilter_idx < nb_vfilters - 1)
                                {
                                    if (++cur_stream.vfilter_idx >= nb_vfilters)
                                        cur_stream.vfilter_idx = 0;
                                }
                                else
                                {
                                    cur_stream.vfilter_idx = 0;
                                    toggle_audio_display(cur_stream);
                                }
                                break;
                            case SDL.SDL_Keycode.SDLK_PAGEUP:
                                if (cur_stream.ic->nb_chapters <= 1)
                                {
                                    incr = 600.0;
                                    goto do_seek;
                                }
                                seek_chapter(cur_stream, 1);
                                break;
                            case SDL.SDL_Keycode.SDLK_PAGEDOWN:
                                if (cur_stream.ic->nb_chapters <= 1)
                                {
                                    incr = -600.0;
                                    goto do_seek;
                                }
                                seek_chapter(cur_stream, -1);
                                break;
                            case SDL.SDL_Keycode.SDLK_LEFT:
                                incr = seek_interval != 0 ? -seek_interval : -10.0;
                                goto do_seek;
                            case SDL.SDL_Keycode.SDLK_RIGHT:
                                incr = seek_interval != 0 ? seek_interval : 10.0;
                                goto do_seek;
                            case SDL.SDL_Keycode.SDLK_UP:
                                incr = 60.0;
                                goto do_seek;
                            case SDL.SDL_Keycode.SDLK_DOWN:
                                incr = -60.0;
                            do_seek:
                                if (seek_by_bytes != 0)
                                {
                                    pos = -1;
                                    if (pos < 0 && cur_stream.video_stream >= 0)
                                        pos = cur_stream.pictq.LastPosition;
                                    if (pos < 0 && cur_stream.audio_stream >= 0)
                                        pos = cur_stream.sampq.LastPosition;
                                    if (pos < 0)
                                        pos = ffmpeg.avio_tell(cur_stream.ic->pb);
                                    if (cur_stream.ic->bit_rate != 0)
                                        incr *= cur_stream.ic->bit_rate / 8.0;
                                    else
                                        incr *= 180000.0;
                                    pos += incr;
                                    stream_seek(cur_stream, (long)pos, (long)incr, 1);
                                }
                                else
                                {
                                    pos = get_master_clock(cur_stream);
                                    if (double.IsNaN(pos))
                                        pos = (double)cur_stream.seek_pos / ffmpeg.AV_TIME_BASE;
                                    pos += incr;
                                    if (cur_stream.ic->start_time != ffmpeg.AV_NOPTS_VALUE && pos < cur_stream.ic->start_time / (double)ffmpeg.AV_TIME_BASE)
                                        pos = cur_stream.ic->start_time / (double)ffmpeg.AV_TIME_BASE;
                                    stream_seek(cur_stream, (long)(pos * ffmpeg.AV_TIME_BASE), (long)(incr * ffmpeg.AV_TIME_BASE), 0);
                                }
                                break;
                            default:
                                break;
                        }
                        break;
                    case (int)SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN:
                        if (exit_on_mousedown)
                        {
                            do_exit(cur_stream);
                            break;
                        }

                        if (@event.button.button == SDL.SDL_BUTTON_LEFT)
                        {
                            last_mouse_left_click = 0;
                            if (ffmpeg.av_gettime_relative() - last_mouse_left_click <= 500000)
                            {
                                toggle_full_screen(cur_stream);
                                cur_stream.force_refresh = true;
                                last_mouse_left_click = 0;
                            }
                            else
                            {
                                last_mouse_left_click = ffmpeg.av_gettime_relative();
                            }
                        }

                        break;
                    case (int)SDL.SDL_EventType.SDL_MOUSEMOTION:
                        if (cursor_hidden)
                        {
                            SDL.SDL_ShowCursor(1);
                            cursor_hidden = false;
                        }
                        cursor_last_shown = ffmpeg.av_gettime_relative();
                        if (@event.type == SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN)
                        {
                            if (@event.button.button != SDL.SDL_BUTTON_RIGHT)
                                break;
                            x = @event.button.x;
                        }
                        else
                        {
                            if ((@event.motion.state & SDL.SDL_BUTTON_RMASK) == 0)
                                break;
                            x = @event.motion.x;
                        }
                        if (seek_by_bytes != 0 || cur_stream.ic->duration <= 0)
                        {
                            long size = ffmpeg.avio_size(cur_stream.ic->pb);
                            stream_seek(cur_stream, (long)(size * x / cur_stream.width), 0, 1);
                        }
                        else
                        {
                            long ts;
                            int ns, hh, mm, ss;
                            int tns, thh, tmm, tss;
                            tns = (int)(cur_stream.ic->duration / 1000000L);
                            thh = tns / 3600;
                            tmm = (tns % 3600) / 60;
                            tss = (tns % 60);
                            frac = x / cur_stream.width;
                            ns = (int)(frac * tns);
                            hh = ns / 3600;
                            mm = (ns % 3600) / 60;
                            ss = (ns % 60);
                            ffmpeg.av_log(null, ffmpeg.AV_LOG_INFO, "Seek to {(frac * 100)} ({hh}:{mm}:{ss}) of total duration ({thh}:{tmm}:{tss})       \n");
                            ts = (long)(frac * cur_stream.ic->duration);
                            if (cur_stream.ic->start_time != ffmpeg.AV_NOPTS_VALUE)
                                ts += cur_stream.ic->start_time;
                            stream_seek(cur_stream, ts, 0, 0);
                        }
                        break;
                    case (int)SDL.SDL_EventType.SDL_WINDOWEVENT:
                        switch (@event.window.windowEvent)
                        {
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_SIZE_CHANGED:
                                screen_width = cur_stream.width = @event.window.data1;
                                screen_height = cur_stream.height = @event.window.data2;
                                if (cur_stream.vis_texture != IntPtr.Zero)
                                {
                                    SDL.SDL_DestroyTexture(cur_stream.vis_texture);
                                    cur_stream.vis_texture = IntPtr.Zero;
                                }
                                break;
                            case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_EXPOSED:
                                cur_stream.force_refresh = true;
                                break;
                        }
                        break;
                    case (int)SDL.SDL_EventType.SDL_QUIT:
                    case FF_QUIT_EVENT:
                        do_exit(cur_stream);
                        break;
                    default:
                        break;
                }
            }
        }

        public static void MainPort(string[] args)
        {
            audio_disable = true;
            subtitle_disable = true;

            uint flags;
            // VideoState @is;

            Helpers.LoadNativeLibraries();
            ffmpeg.av_log_set_flags(ffmpeg.AV_LOG_SKIP_REPEATED);
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);

            /* register all codecs, demux and protocols */
            ffmpeg.avdevice_register_all();
            ffmpeg.avformat_network_init();

            //init_opts();

            //signal(SIGINT, sigterm_handler); /* Interrupt (ANSI).    */
            //signal(SIGTERM, sigterm_handler); /* Termination (ANSI).  */

            input_filename = @"C:\Users\unosp\OneDrive\ffme-testsuite\video-subtitles-03.mkv";

            if (string.IsNullOrWhiteSpace(input_filename))
            {
                Environment.Exit(1);
            }

            if (display_disable)
                video_disable = true;

            flags = SDL.SDL_INIT_VIDEO | SDL.SDL_INIT_AUDIO | SDL.SDL_INIT_TIMER;
            if (audio_disable)
                flags &= ~SDL.SDL_INIT_AUDIO;
            else
            {
                /* Try to work around an occasional ALSA buffer underflow issue when the
                 * period size is NPOT due to ALSA resampling by forcing the buffer size. */
                if (Environment.GetEnvironmentVariable("SDL_AUDIO_ALSA_SET_BUFFER_SIZE") == null)
                    Environment.SetEnvironmentVariable("SDL_AUDIO_ALSA_SET_BUFFER_SIZE", "1", EnvironmentVariableTarget.Process);
            }

            if (display_disable)
                flags &= ~SDL.SDL_INIT_VIDEO;

            if (SDL.SDL_Init(flags) != 0)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, $"Could not initialize SDL - {SDL.SDL_GetError()}\n");
                Environment.Exit(1);
            }

            SDL.SDL_EventState(SDL.SDL_EventType.SDL_SYSWMEVENT, SDL.SDL_IGNORE);
            SDL.SDL_EventState(SDL.SDL_EventType.SDL_USEREVENT, SDL.SDL_IGNORE);

            if (!display_disable)
            {
                flags = (uint)SDL.SDL_WindowFlags.SDL_WINDOW_HIDDEN;
                if (alwaysontop)
                    flags |= (uint)SDL.SDL_WindowFlags.SDL_WINDOW_ALWAYS_ON_TOP;

                if (borderless)
                    flags |= (uint)SDL.SDL_WindowFlags.SDL_WINDOW_BORDERLESS;
                else
                    flags |= (uint)SDL.SDL_WindowFlags.SDL_WINDOW_RESIZABLE;

                window = SDL.SDL_CreateWindow(
                    Constants.program_name, SDL.SDL_WINDOWPOS_UNDEFINED, SDL.SDL_WINDOWPOS_UNDEFINED, default_width, default_height, (SDL.SDL_WindowFlags)flags);

                SDL.SDL_SetHint(SDL.SDL_HINT_RENDER_SCALE_QUALITY, "linear");

                if (window != IntPtr.Zero)
                {
                    renderer = SDL.SDL_CreateRenderer(window, -1, SDL.SDL_RendererFlags.SDL_RENDERER_ACCELERATED | SDL.SDL_RendererFlags.SDL_RENDERER_PRESENTVSYNC);
                    if (renderer == IntPtr.Zero)
                    {
                        ffmpeg.av_log(null, ffmpeg.AV_LOG_WARNING, $"Failed to initialize a hardware accelerated renderer: {SDL.SDL_GetError()}\n");
                        renderer = SDL.SDL_CreateRenderer(window, -1, 0);
                    }

                    if (renderer != IntPtr.Zero)
                    {
                        if (SDL.SDL_GetRendererInfo(renderer, out renderer_info) == 0)
                            ffmpeg.av_log(null, ffmpeg.AV_LOG_VERBOSE, $"Initialized {Marshal.PtrToStringUTF8(renderer_info.name)} renderer.\n");
                    }
                }
                if (window == IntPtr.Zero || renderer == IntPtr.Zero || renderer_info.num_texture_formats <= 0)
                {
                    ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, $"Failed to create window or renderer: {SDL.SDL_GetError()}");
                    do_exit(null);
                }
            }

            GlobalVideoState = stream_open(input_filename, file_iformat);

            if (GlobalVideoState == null)
            {
                ffmpeg.av_log(null, ffmpeg.AV_LOG_FATAL, "Failed to initialize VideoState!\n");
                do_exit(null);
            }

            event_loop(GlobalVideoState);

            /* never returns */

        }


    }
}

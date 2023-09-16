namespace SDL2
{
    using System.Runtime.InteropServices;

    public static partial class SDL
    {
		/* format refers to an SDL_AudioFormat */
		[LibraryImport("SDL2")]
		public static unsafe partial void SDL_MixAudioFormat(
			byte* dst,
			byte* src,
			ushort format,
			uint len,
			int volume
		);
	}
}

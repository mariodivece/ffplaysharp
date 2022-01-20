namespace SDL2
{
    using System.Runtime.InteropServices;

    public static partial class SDL
    {
		/* format refers to an SDL_AudioFormat */
		[DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
		public static extern unsafe void SDL_MixAudioFormat(
			byte* dst,
			byte* src,
			ushort format,
			uint len,
			int volume
		);
	}
}

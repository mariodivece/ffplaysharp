namespace SDL2
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Runtime.InteropServices;
	using System.Text;
	using System.Threading.Tasks;

	public static unsafe class SDLNatives
    {
		/* format refers to an SDL_AudioFormat */
		[DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
		public static extern void SDL_MixAudioFormat(
			byte* dst,
			byte* src,
			ushort format,
			uint len,
			int volume
		);
	}
}

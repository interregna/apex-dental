/*
 * Bridge from System.Drawing images to PdfSharpCore XImage for the Linux core.
 * PdfSharpCore (the cross-platform PdfSharp) has no XImage.FromGdiPlusImage;
 * we round-trip through PNG bytes instead (PDFsharp 6 API). Requires System.Drawing.Common 6.0
 * with System.Drawing.EnableUnixSupport + libgdiplus at runtime; if GDI is
 * unavailable the helper degrades to a 1x1 transparent placeholder so PDF
 * generation continues without the image.
 */
using System;
using System.IO;
using PdfSharp.Drawing;

namespace OpenDentBusiness {
	public static class PdfImageOD {
		//1x1 transparent PNG.
		private static readonly byte[] _bytesPlaceholder=Convert.FromBase64String(
			"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

		public static XImage From(System.Drawing.Image image) {
			byte[] bytesPng=_bytesPlaceholder;
			try {
				if(image!=null) {
					using MemoryStream memoryStream=new MemoryStream();
					image.Save(memoryStream,System.Drawing.Imaging.ImageFormat.Png);
					bytesPng=memoryStream.ToArray();
				}
			}
			catch(Exception ex) {
				Console.Error.WriteLine("[PdfImageOD] GDI image unavailable, using placeholder: "+ex.Message);
			}
			byte[] bytesCaptured=bytesPng;
			return XImage.FromStream(new MemoryStream(bytesCaptured,0,bytesCaptured.Length,writable:false,publiclyVisible:true));
		}
	}
}

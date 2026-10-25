/*
 * Compatibility layer between the old GDI-build PdfSharp 1.x API that the
 * upstream sources were written against and cross-platform PDFsharp 6.
 *
 * PDFsharp 6 removed the implicit System.Drawing conversions (Brush→XBrush,
 * RectangleF→XRect, Color→XColor, ...) and renamed XFontStyle→XFontStyleEx.
 * Rather than editing ~70 upstream call sites, this file exploits C# name
 * resolution: types declared inside namespace OpenDentBusiness shadow
 * using-imported ones, so `Brushes.Black` and `XFontStyle.Bold` resolve here,
 * and extension methods supply the removed XGraphics overloads.
 */
using System;
using System.Drawing;
using PdfSharp.Drawing;

namespace OpenDentBusiness {

	///<summary>Shadow of the removed PdfSharp 1.x XFontStyle. Converts to XFontStyleEx.</summary>
	public readonly struct XFontStyle {
		private readonly XFontStyleEx _value;
		private XFontStyle(XFontStyleEx value) { _value=value; }
		public static readonly XFontStyle Regular=new XFontStyle(XFontStyleEx.Regular);
		public static readonly XFontStyle Bold=new XFontStyle(XFontStyleEx.Bold);
		public static readonly XFontStyle Italic=new XFontStyle(XFontStyleEx.Italic);
		public static readonly XFontStyle Underline=new XFontStyle(XFontStyleEx.Underline);
		public static readonly XFontStyle Strikeout=new XFontStyle(XFontStyleEx.Strikeout);
		public static readonly XFontStyle BoldItalic=new XFontStyle(XFontStyleEx.BoldItalic);
		public static implicit operator XFontStyleEx(XFontStyle style) => style._value;
		public static XFontStyle operator |(XFontStyle a,XFontStyle b) => new XFontStyle(a._value|b._value);
	}

	///<summary>A brush usable with both GDI Graphics and PDFsharp XGraphics calls.</summary>
	public sealed class BrushOD {
		private readonly Color _color;
		private Brush _brushGdi;
		private XSolidBrush _brushX;
		public BrushOD(Color color) { _color=color; }
		public object Clone() => new SolidBrush(_color);
		public static implicit operator Brush(BrushOD brush) => brush._brushGdi??=new SolidBrush(brush._color);
		public static implicit operator XBrush(BrushOD brush) => brush._brushX??=new XSolidBrush(brush._color);
	}

	///<summary>Shadow of System.Drawing.Brushes limited to the members the business layer uses.
	///Members convert implicitly to either Brush (GDI) or XBrush (PDFsharp).</summary>
	public static class Brushes {
		public static readonly BrushOD Black=new BrushOD(Color.Black);
		public static readonly BrushOD White=new BrushOD(Color.White);
		public static readonly BrushOD Gray=new BrushOD(Color.Gray);
		public static readonly BrushOD LightGray=new BrushOD(Color.LightGray);
		public static readonly BrushOD DarkGray=new BrushOD(Color.DarkGray);
		public static readonly BrushOD Red=new BrushOD(Color.Red);
		public static readonly BrushOD Blue=new BrushOD(Color.Blue);
		public static readonly BrushOD Green=new BrushOD(Color.Green);
		public static readonly BrushOD Yellow=new BrushOD(Color.Yellow);
		public static readonly BrushOD Orange=new BrushOD(Color.Orange);
		public static readonly BrushOD Purple=new BrushOD(Color.Purple);
		public static readonly BrushOD Silver=new BrushOD(Color.Silver);
		public static readonly BrushOD Transparent=new BrushOD(Color.Transparent);
		public static readonly BrushOD WhiteSmoke=new BrushOD(Color.WhiteSmoke);
	}

	///<summary>Shadow of XPen restoring the System.Drawing.Color constructors that
	///PDFsharp 6 removed (the real type is sealed, so this wraps and converts).</summary>
	public class XPen {
		private readonly PdfSharp.Drawing.XPen _inner;
		public XPen(Color color,double width) { _inner=new PdfSharp.Drawing.XPen(XColorOD.From(color),width); }
		public XPen(Color color) { _inner=new PdfSharp.Drawing.XPen(XColorOD.From(color)); }
		public XPen(XColor color,double width) { _inner=new PdfSharp.Drawing.XPen(color,width); }
		public XPen(XColor color) { _inner=new PdfSharp.Drawing.XPen(color); }
		public static implicit operator PdfSharp.Drawing.XPen(XPen pen) => pen._inner;
	}

	///<summary>Shadow of XSolidBrush restoring the System.Drawing.Color constructor that
	///PDFsharp 6 removed (the real type is sealed, so this wraps and converts).</summary>
	public class XSolidBrush {
		private readonly PdfSharp.Drawing.XSolidBrush _inner;
		public XSolidBrush(Color color) { _inner=new PdfSharp.Drawing.XSolidBrush(XColorOD.From(color)); }
		public XSolidBrush(XColor color) { _inner=new PdfSharp.Drawing.XSolidBrush(color); }
		public static implicit operator PdfSharp.Drawing.XSolidBrush(XSolidBrush brush) => brush._inner;
		public static implicit operator XBrush(XSolidBrush brush) => brush._inner;
	}

	public static class XColorOD {
		public static XColor From(Color color) => XColor.FromArgb(color.A,color.R,color.G,color.B);
	}

	///<summary>Extension overloads restoring the PdfSharp 1.x XGraphics surface that took
	///System.Drawing geometry types via implicit conversion.</summary>
	public static class XGraphicsCompat {
		private static XRect R(RectangleF rect) => new XRect(rect.X,rect.Y,rect.Width,rect.Height);
		private static XRect R(Rectangle rect) => new XRect(rect.X,rect.Y,rect.Width,rect.Height);

		public static void DrawString(this XGraphics g,string s,XFont font,XBrush brush,RectangleF rect) =>
			g.DrawString(s,font,brush,R(rect),XStringFormats.TopLeft);
		public static void DrawString(this XGraphics g,string s,XFont font,XBrush brush,RectangleF rect,XStringFormat format) =>
			g.DrawString(s,font,brush,R(rect),format);
		public static void DrawString(this XGraphics g,string s,XFont font,XBrush brush,PointF point) =>
			g.DrawString(s,font,brush,new XPoint(point.X,point.Y));
		public static void DrawString(this XGraphics g,string s,XFont font,XBrush brush,Point point) =>
			g.DrawString(s,font,brush,new XPoint(point.X,point.Y));

		public static void DrawRectangle(this XGraphics g,XBrush brush,RectangleF rect) =>
			g.DrawRectangle(brush,R(rect));
		public static void DrawRectangle(this XGraphics g,XBrush brush,Rectangle rect) =>
			g.DrawRectangle(brush,R(rect));
		public static void DrawRectangle(this XGraphics g,PdfSharp.Drawing.XPen pen,RectangleF rect) =>
			g.DrawRectangle(pen,R(rect));

		public static void DrawLine(this XGraphics g,PdfSharp.Drawing.XPen pen,Point pt1,Point pt2) =>
			g.DrawLine(pen,new XPoint(pt1.X,pt1.Y),new XPoint(pt2.X,pt2.Y));
		public static void DrawLine(this XGraphics g,PdfSharp.Drawing.XPen pen,PointF pt1,PointF pt2) =>
			g.DrawLine(pen,new XPoint(pt1.X,pt1.Y),new XPoint(pt2.X,pt2.Y));

		public static void DrawImage(this XGraphics g,XImage image,Rectangle rect) =>
			g.DrawImage(image,R(rect));
		public static void DrawImage(this XGraphics g,XImage image,RectangleF rect) =>
			g.DrawImage(image,R(rect));

		public static void DrawEllipse(this XGraphics g,XBrush brush,RectangleF rect) =>
			g.DrawEllipse(brush,R(rect));

		public static void DrawString(this XGraphics g,string s,XFont font,XBrush brush,double x,double y) =>
			g.DrawString(s,font,brush,new XPoint(x,y));
		public static void DrawString(this XGraphics g,string s,XFont font,XBrush brush,XUnit x,XUnit y) =>
			g.DrawString(s,font,brush,new XPoint(x.Point,y.Point));

		private static PdfSharp.Drawing.XPen P(Pen pen) => new PdfSharp.Drawing.XPen(XColorOD.From(pen.Color),pen.Width);
		private static XBrush B(Brush brush) => new PdfSharp.Drawing.XSolidBrush(
			brush is SolidBrush solidBrush?XColorOD.From(solidBrush.Color):XColors.Black);

		public static void DrawRectangle(this XGraphics g,Pen pen,double x,double y,double w,double h) =>
			g.DrawRectangle(P(pen),x,y,w,h);
		public static void DrawRectangle(this XGraphics g,Pen pen,RectangleF rect) =>
			g.DrawRectangle(P(pen),R(rect));
		public static void DrawRectangle(this XGraphics g,Brush brush,double x,double y,double w,double h) =>
			g.DrawRectangle(B(brush),x,y,w,h);
		public static void DrawRectangle(this XGraphics g,Brush brush,RectangleF rect) =>
			g.DrawRectangle(B(brush),R(rect));
		public static void DrawLine(this XGraphics g,Pen pen,float x1,float y1,float x2,float y2) =>
			g.DrawLine(P(pen),x1,y1,x2,y2);
	}
}

namespace OpenDentBusiness {
	///<summary>Font resolver so PDFsharp 6 can embed fonts on Linux. Maps every requested
	///family onto the Liberation metric-compatible set (Arial→Liberation Sans, etc).</summary>
	public class FontResolverOD : PdfSharp.Fonts.IFontResolver {
		private static readonly string[] _dirsFonts={
			"/usr/share/fonts/truetype/liberation",
			"/usr/share/fonts/truetype/liberation2",
		};
		public byte[] GetFont(string faceName) {
			foreach(string dir in _dirsFonts) {
				string path=System.IO.Path.Combine(dir,faceName+".ttf");
				if(System.IO.File.Exists(path)) {
					return System.IO.File.ReadAllBytes(path);
				}
			}
			throw new System.IO.FileNotFoundException("Font face not found: "+faceName);
		}
		public PdfSharp.Fonts.FontResolverInfo ResolveTypeface(string familyName,bool isBold,bool isItalic) {
			string family=familyName.ToLowerInvariant() switch {
				"courier new" or "courier" or "consolas" => "LiberationMono",
				"times new roman" or "times" or "serif" => "LiberationSerif",
				_ => "LiberationSans",//Arial, Helvetica, Segoe UI, Microsoft Sans Serif, ...
			};
			string face=(isBold,isItalic) switch {
				(true,true) => family+"-BoldItalic",
				(true,false) => family+"-Bold",
				(false,true) => family+"-Italic",
				_ => family+"-Regular",
			};
			return new PdfSharp.Fonts.FontResolverInfo(face);
		}
		public static void Register() {
			if(PdfSharp.Fonts.GlobalFontSettings.FontResolver==null) {
				PdfSharp.Fonts.GlobalFontSettings.FontResolver=new FontResolverOD();
			}
		}
	}
}

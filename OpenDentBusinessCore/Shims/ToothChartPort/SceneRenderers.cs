/*
 * The two thin backends over ToothChartScene (workplan §4 step 4):
 *  - GdiSceneRenderer: Bitmap via System.Drawing/libgdiplus, consumed by
 *    ToothChartHelper.GetImage for perio PDFs and sheet tooth-chart fields.
 *  - SvgSceneRenderer: markup for the browser chart.
 * Both scale the classic 500x370 chart space to the requested size.
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;

namespace OpenDentBusiness.ToothChartPort {

	public static class GdiSceneRenderer {
		public static Image Render(ToothChartScene scene) {
			int width=Math.Max(scene.Size.Width,1);
			int height=Math.Max(scene.Size.Height,1);
			Bitmap bitmap=new Bitmap(width,height);
			using Graphics g=Graphics.FromImage(bitmap);
			g.SmoothingMode=SmoothingMode.AntiAlias;
			g.Clear(scene.Background);
			g.ScaleTransform(width/ToothChartSceneBuilder.CHART_W,height/ToothChartSceneBuilder.CHART_H);
			foreach(SceneGroup group in scene.Groups) {
				GraphicsState saved=g.Save();
				g.TranslateTransform(group.TranslateX,group.TranslateY);
				if(group.RotateDegrees!=0) {
					g.TranslateTransform(group.RotateCenterX,group.RotateCenterY);
					g.RotateTransform(group.RotateDegrees);
					g.TranslateTransform(-group.RotateCenterX,-group.RotateCenterY);
				}
				foreach(ScenePrimitive primitive in group.Primitives) {
					Draw(g,primitive);
				}
				g.Restore(saved);
			}
			return bitmap;
		}

		static void Draw(Graphics g,ScenePrimitive primitive) {
			switch(primitive) {
				case SceneLine line:
					using(Pen pen=new Pen(line.Color,line.Width)) {
						g.DrawLine(pen,line.X1,line.Y1,line.X2,line.Y2);
					}
					break;
				case SceneRect rect:
					using(GraphicsPath path=RoundedPath(rect)) {
						if(rect.Fill!=null) {
							using SolidBrush brush=new SolidBrush(rect.Fill.Value);
							g.FillPath(brush,path);
						}
						if(rect.Stroke!=null) {
							using Pen pen=new Pen(rect.Stroke.Value,rect.StrokeWidth);
							g.DrawPath(pen,path);
						}
					}
					break;
				case SceneEllipse ellipse:
					if(ellipse.Fill!=null) {
						using SolidBrush brush=new SolidBrush(ellipse.Fill.Value);
						g.FillEllipse(brush,ellipse.X,ellipse.Y,ellipse.W,ellipse.H);
					}
					if(ellipse.Stroke!=null) {
						using Pen pen=new Pen(ellipse.Stroke.Value,1);
						g.DrawEllipse(pen,ellipse.X,ellipse.Y,ellipse.W,ellipse.H);
					}
					break;
				case ScenePolygon polygon:
					if(polygon.Points.Count>2) {
						PointF[] arrayPoints=polygon.Points.ToArray();
						if(polygon.Fill!=null) {
							using SolidBrush brush=new SolidBrush(polygon.Fill.Value);
							g.FillPolygon(brush,arrayPoints);
						}
						if(polygon.Stroke!=null && polygon.StrokeWidth>0) {
							using Pen pen=new Pen(polygon.Stroke.Value,polygon.StrokeWidth);
							pen.LineJoin=LineJoin.Round;
							g.DrawPolygon(pen,arrayPoints);
						}
					}
					break;
				case ScenePolyline polyline:
					if(polyline.Points.Count>1) {
						using Pen pen=new Pen(polyline.Color,polyline.Width);
						pen.LineJoin=LineJoin.Round;
						g.DrawLines(pen,polyline.Points.ToArray());
					}
					break;
				case SceneText text:
					using(Font font=new Font("Liberation Sans",text.Size))
					using(SolidBrush brush=new SolidBrush(text.Color)) {
						if(text.Centered) {
							using StringFormat format=new StringFormat() { Alignment=StringAlignment.Center };
							g.DrawString(text.Text,font,brush,text.X,text.Y,format);
						}
						else {
							g.DrawString(text.Text,font,brush,text.X,text.Y);
						}
					}
					break;
			}
		}

		static GraphicsPath RoundedPath(SceneRect rect) {
			GraphicsPath path=new GraphicsPath();
			float r=Math.Min(rect.Rx,Math.Min(rect.W,rect.H)/2f);
			if(r<=0) {
				path.AddRectangle(new RectangleF(rect.X,rect.Y,rect.W,rect.H));
				return path;
			}
			float d=r*2;
			path.AddArc(rect.X,rect.Y,d,d,180,90);
			path.AddArc(rect.X+rect.W-d,rect.Y,d,d,270,90);
			path.AddArc(rect.X+rect.W-d,rect.Y+rect.H-d,d,d,0,90);
			path.AddArc(rect.X,rect.Y+rect.H-d,d,d,90,90);
			path.CloseFigure();
			return path;
		}
	}

	public static class SvgSceneRenderer {
		static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
		static string F(float v) => v.ToString("0.##",CultureInfo.InvariantCulture);

		public static string Render(ToothChartScene scene) {
			StringBuilder sb=new StringBuilder();
			sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(ToothChartSceneBuilder.CHART_W)} {F(ToothChartSceneBuilder.CHART_H)}\" ");
			sb.Append($"width=\"{scene.Size.Width}\" height=\"{scene.Size.Height}\" font-family=\"Liberation Sans,Arial,sans-serif\">");
			sb.Append($"<rect width=\"100%\" height=\"100%\" fill=\"{Hex(scene.Background)}\"/>");
			foreach(SceneGroup group in scene.Groups) {
				string transform="";
				if(group.TranslateX!=0 || group.TranslateY!=0) {
					transform+=$"translate({F(group.TranslateX)},{F(group.TranslateY)}) ";
				}
				if(group.RotateDegrees!=0) {
					transform+=$"rotate({F(group.RotateDegrees)},{F(group.RotateCenterX)},{F(group.RotateCenterY)})";
				}
				sb.Append(transform==""?"<g>":$"<g transform=\"{transform.Trim()}\">");
				foreach(ScenePrimitive primitive in group.Primitives) {
					Append(sb,primitive);
				}
				sb.Append("</g>");
			}
			sb.Append("</svg>");
			return sb.ToString();
		}

		static void Append(StringBuilder sb,ScenePrimitive primitive) {
			switch(primitive) {
				case SceneLine line:
					sb.Append($"<line x1=\"{F(line.X1)}\" y1=\"{F(line.Y1)}\" x2=\"{F(line.X2)}\" y2=\"{F(line.Y2)}\" stroke=\"{Hex(line.Color)}\" stroke-width=\"{F(line.Width)}\"/>");
					break;
				case SceneRect rect:
					sb.Append($"<rect x=\"{F(rect.X)}\" y=\"{F(rect.Y)}\" width=\"{F(rect.W)}\" height=\"{F(rect.H)}\" rx=\"{F(rect.Rx)}\" ");
					sb.Append($"fill=\"{(rect.Fill!=null?Hex(rect.Fill.Value):"none")}\" ");
					if(rect.Stroke!=null) {
						sb.Append($"stroke=\"{Hex(rect.Stroke.Value)}\" stroke-width=\"{F(rect.StrokeWidth)}\"");
					}
					sb.Append("/>");
					break;
				case SceneEllipse ellipse:
					sb.Append($"<ellipse cx=\"{F(ellipse.X+ellipse.W/2)}\" cy=\"{F(ellipse.Y+ellipse.H/2)}\" rx=\"{F(ellipse.W/2)}\" ry=\"{F(ellipse.H/2)}\" ");
					sb.Append($"fill=\"{(ellipse.Fill!=null?Hex(ellipse.Fill.Value):"none")}\"");
					if(ellipse.Stroke!=null) {
						sb.Append($" stroke=\"{Hex(ellipse.Stroke.Value)}\"");
					}
					sb.Append("/>");
					break;
				case ScenePolygon polygon: {
					sb.Append("<polygon points=\"");
					foreach(var point in polygon.Points) {
						sb.Append($"{F(point.X)},{F(point.Y)} ");
					}
					string fill=polygon.Fill!=null?Hex(polygon.Fill.Value):"none";
					sb.Append($"\" fill=\"{fill}\"");
					if(polygon.Fill!=null && polygon.Fill.Value.A<255) {
						sb.Append($" fill-opacity=\"{F(polygon.Fill.Value.A/255f)}\"");
					}
					if(polygon.Stroke!=null && polygon.StrokeWidth>0) {
						sb.Append($" stroke=\"{Hex(polygon.Stroke.Value)}\" stroke-width=\"{F(polygon.StrokeWidth)}\"");
						if(polygon.Stroke.Value.A<255) {
							sb.Append($" stroke-opacity=\"{F(polygon.Stroke.Value.A/255f)}\"");
						}
					}
					sb.Append(" stroke-linejoin=\"round\"/>");
					break;
				}
				case ScenePolyline polyline:
					sb.Append("<polyline points=\"");
					foreach(var point in polyline.Points) {
						sb.Append($"{F(point.X)},{F(point.Y)} ");
					}
					sb.Append($"\" fill=\"none\" stroke=\"{Hex(polyline.Color)}\" stroke-width=\"{F(polyline.Width)}\" stroke-linejoin=\"round\"/>");
					break;
				case SceneText text:
					string anchor=text.Centered?" text-anchor=\"middle\"":"";
					sb.Append($"<text x=\"{F(text.X)}\" y=\"{F(text.Y+text.Size)}\" font-size=\"{F(text.Size+2)}\"{anchor} fill=\"{Hex(text.Color)}\">");
					sb.Append(System.Security.SecurityElement.Escape(text.Text??""));
					sb.Append("</text>");
					break;
			}
		}
	}
}

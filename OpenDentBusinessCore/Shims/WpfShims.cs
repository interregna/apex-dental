/*
 * Minimal stand-ins for WPF value types (System.Windows.*) used by a few
 * business files for geometry math, plus the WPF MessageBox some files
 * resolve via `using System.Windows;`.
 */
using System;

namespace System.Windows {

	public struct Point {
		public Point(double x,double y) { X=x; Y=y; }
		public double X { get; set; }
		public double Y { get; set; }
	}

	public struct Size {
		public Size(double width,double height) { Width=width; Height=height; }
		public double Width { get; set; }
		public double Height { get; set; }
	}

	public struct Rect {
		public Rect(double x,double y,double width,double height) { X=x; Y=y; Width=width; Height=height; }
		public double X { get; set; }
		public double Y { get; set; }
		public double Width { get; set; }
		public double Height { get; set; }
		public double Left => X;
		public double Top => Y;
		public double Right => X+Width;
		public double Bottom => Y+Height;
	}

	public struct Vector {
		public Vector(double x,double y) { X=x; Y=y; }
		public double X { get; set; }
		public double Y { get; set; }
		public double Length => Math.Sqrt(X*X+Y*Y);
		///<summary>Signed angle in degrees between the two vectors, matching WPF semantics.</summary>
		public static double AngleBetween(Vector vector1,Vector vector2) {
			double sin=vector1.X*vector2.Y-vector2.X*vector1.Y;
			double cos=vector1.X*vector2.X+vector1.Y*vector2.Y;
			return Math.Atan2(sin,cos)*(180/Math.PI);
		}
	}

	public struct Thickness {
		public Thickness(double uniformLength) { Left=Top=Right=Bottom=uniformLength; }
		public Thickness(double left,double top,double right,double bottom) { Left=left; Top=top; Right=right; Bottom=bottom; }
		public double Left { get; set; }
		public double Top { get; set; }
		public double Right { get; set; }
		public double Bottom { get; set; }
	}

	public enum MessageBoxButton {
		OK=0, OKCancel=1, YesNoCancel=3, YesNo=4
	}

	public enum MessageBoxResult {
		None=0, OK=1, Cancel=2, Yes=6, No=7
	}

	///<summary>Headless WPF MessageBox: logs to stderr, answers OK/Yes.</summary>
	public static class MessageBox {
		public static MessageBoxResult Show(string messageBoxText) {
			Console.Error.WriteLine("[MessageBox] "+messageBoxText);
			return MessageBoxResult.OK;
		}
		public static MessageBoxResult Show(string messageBoxText,string caption) => Show(caption+": "+messageBoxText);
		public static MessageBoxResult Show(string messageBoxText,string caption,MessageBoxButton button) {
			Console.Error.WriteLine("[MessageBox] "+caption+": "+messageBoxText);
			if(button==MessageBoxButton.YesNo || button==MessageBoxButton.YesNoCancel) {
				return MessageBoxResult.Yes;
			}
			return MessageBoxResult.OK;
		}
	}
}

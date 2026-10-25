/*
 * Headless shims for the Linux port (OpenDentBusinessCore).
 * Business-layer files reference a handful of shallow System.Windows.Forms
 * symbols (MessageBox.Show, DialogResult, Application.DoEvents, ...).
 * On Linux there is no WinForms, so these minimal stand-ins log to stderr
 * and return non-interactive defaults. Anything needing a real widget stays
 * excluded via Excluded.items.
 */
using System;

namespace System.Windows.Forms {

	public enum DialogResult {
		None=0, OK=1, Cancel=2, Abort=3, Retry=4, Ignore=5, Yes=6, No=7
	}

	public enum MessageBoxButtons {
		OK=0, OKCancel=1, AbortRetryIgnore=2, YesNoCancel=3, YesNo=4, RetryCancel=5
	}

	public enum MessageBoxIcon {
		None=0, Hand=16, Stop=16, Error=16, Question=32, Exclamation=48, Warning=48, Asterisk=64, Information=64
	}

	public enum MessageBoxDefaultButton {
		Button1=0, Button2=256, Button3=512
	}

	public enum HorizontalAlignment {
		Left=0, Right=1, Center=2
	}

	[Flags]
	public enum AnchorStyles {
		None=0, Top=1, Bottom=2, Left=4, Right=8
	}

	public interface IWin32Window {
		IntPtr Handle { get; }
	}

	///<summary>Headless: logs to stderr. Yes/No prompts answer Yes; OK/Cancel answer OK.</summary>
	public static class MessageBox {
		private static DialogResult Log(string text,MessageBoxButtons buttons) {
			Console.Error.WriteLine("[MessageBox] "+text);
			switch(buttons) {
				case MessageBoxButtons.YesNo:
				case MessageBoxButtons.YesNoCancel:
					return DialogResult.Yes;
				default:
					return DialogResult.OK;
			}
		}
		public static DialogResult Show(string text) => Log(text,MessageBoxButtons.OK);
		public static DialogResult Show(string text,string caption) => Log(caption+": "+text,MessageBoxButtons.OK);
		public static DialogResult Show(string text,string caption,MessageBoxButtons buttons) => Log(caption+": "+text,buttons);
		public static DialogResult Show(string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon) => Log(caption+": "+text,buttons);
		public static DialogResult Show(string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon,MessageBoxDefaultButton defaultButton) => Log(caption+": "+text,buttons);
		public static DialogResult Show(IWin32Window owner,string text) => Log(text,MessageBoxButtons.OK);
		public static DialogResult Show(IWin32Window owner,string text,string caption) => Log(caption+": "+text,MessageBoxButtons.OK);
		public static DialogResult Show(IWin32Window owner,string text,string caption,MessageBoxButtons buttons) => Log(caption+": "+text,buttons);
		public static DialogResult Show(IWin32Window owner,string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon) => Log(caption+": "+text,buttons);
	}

	public static class Application {
		public static System.Globalization.CultureInfo CurrentCulture {
			get => System.Threading.Thread.CurrentThread.CurrentCulture;
			set => System.Threading.Thread.CurrentThread.CurrentCulture=value;
		}
		public static void DoEvents() { }
		public static string StartupPath => AppContext.BaseDirectory;
		public static string ExecutablePath => Environment.ProcessPath ?? AppContext.BaseDirectory;
		public static string ProductVersion => "24.3.63.0";
		public static event EventHandler ApplicationExit { add { } remove { } }
		public static void Exit() => Environment.Exit(0);
	}

	public class Cursor {
		public Cursor() { }
		public Cursor(IntPtr handle) { }
		public static Cursor Current { get; set; } = new Cursor();
	}

	public static class Cursors {
		public static Cursor Default { get; } = new Cursor();
		public static Cursor WaitCursor { get; } = new Cursor();
		public static Cursor Hand { get; } = new Cursor();
	}

	public class ScrollEventArgs : EventArgs {
		public int NewValue { get; set; }
		public int OldValue { get; set; }
	}

	public static class Clipboard {
		public static void SetText(string text) { Console.Error.WriteLine("[Clipboard] (discarded on headless Linux)"); }
		public static string GetText() => string.Empty;
		public static bool ContainsText() => false;
		public static bool ContainsImage() => false;
		public static System.Drawing.Image GetImage() => null;
		public static void SetImage(System.Drawing.Image image) { }
		public static void Clear() { }
		public static IDataObject GetDataObject() => null;
		public static void SetDataObject(object data) { }
		public static void SetDataObject(object data,bool copy) { }
	}
}

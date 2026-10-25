/*
 * Headless Control/Form stand-ins for the Linux core build.
 * InvokeRequired is always false and Invoke runs delegates inline, so
 * business code written to marshal onto a UI thread simply executes
 * synchronously. Forms never display; ShowDialog returns DialogResult.
 */
using System;
using System.Collections.Generic;

namespace System.Windows.Forms {

	public class Control : IDisposable, IWin32Window {
		public string Name { get; set; }
		public string Text { get; set; }
		public bool Visible { get; set; }
		public bool Enabled { get; set; } = true;
		public object Tag { get; set; }
		public System.Drawing.Color BackColor { get; set; }
		public System.Drawing.Color ForeColor { get; set; }
		public Cursor Cursor { get; set; } = new Cursor();
		public int TabIndex { get; set; }
		public bool TabStop { get; set; }
		public int Width { get; set; }
		public int Height { get; set; }
		public System.Drawing.Point Location { get; set; }
		public System.Drawing.Size Size { get; set; }
		public System.Drawing.Font Font { get; set; }
		public AnchorStyles Anchor { get; set; }
		public List<Control> Controls { get; } = new List<Control>();
		public bool InvokeRequired => false;
		public bool IsHandleCreated => true;
		public bool IsDisposed { get; private set; }
		public bool Disposing => false;
		public System.Drawing.Size MinimumSize { get; set; }
		public System.Drawing.Size MaximumSize { get; set; }
		public IntPtr Handle => IntPtr.Zero;
		public event EventHandler Disposed;
		public object Invoke(Delegate method) => method.DynamicInvoke();
		public object Invoke(Delegate method,params object[] args) => method.DynamicInvoke(args);
		public IAsyncResult BeginInvoke(Delegate method) { method.DynamicInvoke(); return null; }
		public IAsyncResult BeginInvoke(Delegate method,params object[] args) { method.DynamicInvoke(args); return null; }
		public void Dispose() {
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		protected virtual void Dispose(bool disposing) {
			IsDisposed=true;
			Disposed?.Invoke(this,EventArgs.Empty);
		}
	}

	public enum CloseReason {
		None=0, WindowsShutDown=1, MdiFormClosing=2, UserClosing=3, TaskManagerClosing=4, FormOwnerClosing=5, ApplicationExitCall=6
	}

	public class FormClosingEventArgs : System.ComponentModel.CancelEventArgs {
		public CloseReason CloseReason { get; }
		public FormClosingEventArgs(CloseReason closeReason,bool cancel) : base(cancel) {
			CloseReason=closeReason;
		}
	}

	public class FormClosedEventArgs : EventArgs {
		public CloseReason CloseReason { get; }
		public FormClosedEventArgs(CloseReason closeReason) {
			CloseReason=closeReason;
		}
	}

	public delegate void FormClosingEventHandler(object sender,FormClosingEventArgs e);
	public delegate void FormClosedEventHandler(object sender,FormClosedEventArgs e);

	public class Form : Control {
		public static Form ActiveForm => null;
		public DialogResult DialogResult { get; set; }
		public bool TopMost { get; set; }
		public bool ShowInTaskbar { get; set; }
		public event FormClosingEventHandler FormClosing;
		public event FormClosedEventHandler FormClosed;
		public event EventHandler Load;
		public event EventHandler Shown;
		public void Show() { Load?.Invoke(this,EventArgs.Empty); Shown?.Invoke(this,EventArgs.Empty); }
		public void Show(IWin32Window owner) => Show();
		public DialogResult ShowDialog() { Show(); return DialogResult; }
		public DialogResult ShowDialog(IWin32Window owner) => ShowDialog();
		public void Close() {
			FormClosingEventArgs eClosing=new FormClosingEventArgs(CloseReason.UserClosing,false);
			OnFormClosing(eClosing);
			if(eClosing.Cancel) {
				return;
			}
			OnFormClosed(new FormClosedEventArgs(CloseReason.UserClosing));
		}
		public void BringToFront() { }
		public void Activate() { }
		protected virtual void OnFormClosing(FormClosingEventArgs e) => FormClosing?.Invoke(this,e);
		protected virtual void OnFormClosed(FormClosedEventArgs e) => FormClosed?.Invoke(this,e);
		protected virtual void OnLoad(EventArgs e) => Load?.Invoke(this,e);
	}

	public enum ProgressBarStyle {
		Blocks=0, Continuous=1, Marquee=2
	}

	public class TreeNode {
		public TreeNode() { Nodes=new List<TreeNode>(); }
		public TreeNode(string text) : this() { Text=text; }
		public string Name { get; set; }
		public string Text { get; set; }
		public object Tag { get; set; }
		public List<TreeNode> Nodes { get; }
	}
}

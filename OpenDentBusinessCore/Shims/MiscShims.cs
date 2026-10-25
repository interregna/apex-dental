/*
 * Remaining small stand-ins for the headless Linux core build.
 */
using System;

namespace System.Windows.Forms {
	public enum CheckState {
		Unchecked=0, Checked=1, Indeterminate=2
	}

	public class TextBox : Control {
		public bool Multiline { get; set; }
		public bool ReadOnly { get; set; }
		public void AppendText(string text) { Text=(Text??"")+text; }
	}
}

namespace System.Windows.Forms {
	public static class SystemInformation {
		public static int MouseWheelScrollLines => 3;
		public static System.Drawing.Size PrimaryMonitorSize => new System.Drawing.Size(1920,1080);
		public static int DoubleClickTime => 500;
		///<summary>Read via reflection by Security.GetComplexComputerName.</summary>
		public static bool TerminalServerSession => false;
	}
}

namespace System.Media {
	///<summary>Headless no-op sound player.</summary>
	public class SoundPlayer : IDisposable {
		public SoundPlayer() { }
		public SoundPlayer(string soundLocation) { }
		public SoundPlayer(System.IO.Stream stream) { }
		public void Play() { }
		public void PlaySync() { }
		public void PlayLooping() { }
		public void Load() { }
		public void Stop() { }
		public void Dispose() { }
	}
}

namespace System.Windows {
	///<summary>WPF clipboard stand-in; discards on headless Linux.</summary>
	public static class Clipboard {
		public static void SetText(string text) { Console.Error.WriteLine("[Clipboard] (discarded on headless Linux)"); }
		public static string GetText() => string.Empty;
		public static bool ContainsText() => false;
	}
}

namespace System.Web {
	///<summary>Headless: never running under ASP.NET, so Current is always null.</summary>
	public class HttpContext {
		public static HttpContext Current => null;
		public object Session => null;
		public HttpRequest Request => null;
		public HttpServerUtility Server => null;
	}

	public class HttpRequest {
		public string UserHostAddress => null;
		public string UserAgent => null;
		public System.Collections.Specialized.NameValueCollection Headers { get; } = new System.Collections.Specialized.NameValueCollection();
	}

	public class HttpServerUtility {
		public string MapPath(string path) => null;
	}
}

namespace PasswordVaultWrapper {
	///<summary>Headless stand-in for the WinRT Windows PasswordVault wrapper (its
	///Windows.winmd dependency breaks CoreCLR startup on Linux). No credentials are
	///ever stored; retrieval always reports not-found.</summary>
	public class WindowsPasswordVaultWrapper {
		public static void ClearCredentials(string uri) { }
		public static void WritePassword(string uri,string username,string password) { }
		public static bool TryRetrieveUserName(string uri,out string username) {
			username=null;
			return false;
		}
		public static string RetrievePassword(string uri,string username) => "";
	}
}

namespace CDO {
	///<summary>Stub for the Windows-only CDO COM mail interop. Send() throws; the
	///implicit-SSL SMTP path that uses it should use MailKit on Linux instead.</summary>
	public class Message {
		public Configuration Configuration { get; } = new Configuration();
		public string To { get; set; }
		public string From { get; set; }
		public string CC { get; set; }
		public string BCC { get; set; }
		public string Subject { get; set; }
		public string TextBody { get; set; }
		public string HTMLBody { get; set; }
		public BodyPart AddAttachment(string path) => new BodyPart();
		public void Send() => throw new PlatformNotSupportedException("CDO COM mail is not supported in the headless Linux core build.");
	}
	public class Configuration {
		public Fields Fields { get; } = new Fields();
	}
	public class BodyPart {
		public Fields Fields { get; } = new Fields();
	}
	public class Fields {
		private readonly System.Collections.Generic.Dictionary<string,Field> _dict=new System.Collections.Generic.Dictionary<string,Field>();
		public Field this[string name] {
			get {
				if(!_dict.TryGetValue(name,out Field field)) {
					field=new Field();
					_dict[name]=field;
				}
				return field;
			}
		}
		public void Update() { }
	}
	public class Field {
		public object Value { get; set; }
	}
}

namespace System.Web.Hosting {
	///<summary>Headless: never IIS-hosted, so all paths are null and IsHosted is false.</summary>
	public static class HostingEnvironment {
		public static bool IsHosted => false;
		public static string ApplicationVirtualPath => null;
		public static string ApplicationPhysicalPath => null;
		public static string SiteName => null;
	}
}

namespace Microsoft.VisualBasic.Devices {
	///<summary>Headless stand-in reporting the actual host OS/memory via .NET APIs.</summary>
	public class ComputerInfo {
		public string OSFullName => System.Runtime.InteropServices.RuntimeInformation.OSDescription;
		public string OSPlatform => Environment.OSVersion.Platform.ToString();
		public string OSVersion => Environment.OSVersion.Version.ToString();
		public ulong TotalPhysicalMemory => (ulong)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
		public ulong AvailablePhysicalMemory => (ulong)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
	}
}


namespace CodeBase {
	///<summary>Headless stand-in for the WinForms ODMessageBox: logs to stderr, returns OK/Yes.</summary>
	public static class ODMessageBox {
		public static System.Windows.Forms.DialogResult Show(string text) {
			Console.Error.WriteLine("[ODMessageBox] "+text);
			return System.Windows.Forms.DialogResult.OK;
		}
		public static System.Windows.Forms.DialogResult Show(string text,string caption) => Show(caption+": "+text);
		public static System.Windows.Forms.DialogResult Show(string text,string caption,System.Windows.Forms.MessageBoxButtons buttons) {
			Console.Error.WriteLine("[ODMessageBox] "+caption+": "+text);
			if(buttons==System.Windows.Forms.MessageBoxButtons.YesNo || buttons==System.Windows.Forms.MessageBoxButtons.YesNoCancel) {
				return System.Windows.Forms.DialogResult.Yes;
			}
			return System.Windows.Forms.DialogResult.OK;
		}
	}

	///<summary>Headless stand-in for the copy-paste message box Form.</summary>
	public class MsgBoxCopyPaste : System.Windows.Forms.Form {
		public MsgBoxCopyPaste(string msg) {
			Console.Error.WriteLine("[MsgBoxCopyPaste] "+msg);
		}
	}

	///<summary>Headless stand-in for the excluded WinForms ODProgress: runs the action
	///synchronously with no progress window.</summary>
	public static class ODProgress {
		public static void ShowAction(Action actionComputation,string startingMessage="Please Wait...",
			Action<Exception> actionException=null,
			System.Windows.Forms.ProgressBarStyle progStyle=System.Windows.Forms.ProgressBarStyle.Marquee,
			bool hasMinimize=true,bool hasHistory=false) {
			try {
				actionComputation();
			}
			catch(Exception ex) {
				if(actionException==null) {
					throw;
				}
				actionException(ex);
			}
		}
	}

	///<summary>Headless replacement for the WinForms-based ODProgressExtended: inherits the
	///do-nothing behavior of ODProgressExtendedNull so long-running business methods just run.
	///Mirrors the real class's extra public surface (ctor, Fire, Close).</summary>
	public class ODProgressExtended : ODProgressExtendedNull {
		public ODProgressExtended() { }
		public ODProgressExtended(System.Windows.Forms.Form currentForm,object tag=null,ProgBarStyle progBarStyle=ProgBarStyle.Blocks,
			string lanThis="",string cancelCaption="") { }
		public void Fire(ODEventType odEventType,object tag) { }
		public void Close() { }
	}
}

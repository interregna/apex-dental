/*
 * Compile-time stand-ins for System.Web.Services (classic ASMX SOAP stack),
 * which does not exist on .NET 8. The generated proxies under
 * "Web References/" are mostly plain DTO classes; only the client classes
 * derive from SoapHttpClientProtocol. These shims let all of that compile.
 * Actually invoking a SOAP method throws PlatformNotSupportedException —
 * the HQ web services are unreachable from a headless dev box anyway.
 */
using System;
using System.ComponentModel;
using System.Net;
using System.Threading;

namespace System.Web.Services {
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface,AllowMultiple=true)]
	public sealed class WebServiceBindingAttribute : Attribute {
		public string Name { get; set; }
		public string Namespace { get; set; }
		public string Location { get; set; }
		public bool EmulateHttpVersion10 { get; set; }
	}
	[AttributeUsage(AttributeTargets.Class)]
	public sealed class WebServiceAttribute : Attribute {
		public string Namespace { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
	}
	[AttributeUsage(AttributeTargets.Method)]
	public sealed class WebMethodAttribute : Attribute {
		public string Description { get; set; }
		public bool EnableSession { get; set; }
	}
}

namespace System.Web.Services.Description {
	public enum SoapBindingUse {
		Default=0, Encoded=1, Literal=2
	}
}

namespace System.Web.Services.Protocols {

	public class SoapException : Exception {
		public SoapException() { }
		public SoapException(string message,System.Xml.XmlQualifiedName code) : base(message) { Code=code; }
		public SoapException(string message,System.Xml.XmlQualifiedName code,Exception innerException) : base(message,innerException) { Code=code; }
		public System.Xml.XmlQualifiedName Code { get; }
		public System.Xml.XmlNode Detail => null;
		public string Actor => null;
	}

	public enum SoapParameterStyle {
		Default=0, Bare=1, Wrapped=2
	}

	[AttributeUsage(AttributeTargets.Method)]
	public sealed class SoapDocumentMethodAttribute : Attribute {
		public SoapDocumentMethodAttribute() { }
		public SoapDocumentMethodAttribute(string action) { Action=action; }
		public string Action { get; set; }
		public string RequestNamespace { get; set; }
		public string RequestElementName { get; set; }
		public string ResponseNamespace { get; set; }
		public string ResponseElementName { get; set; }
		public bool OneWay { get; set; }
		public string Binding { get; set; }
		public System.Web.Services.Description.SoapBindingUse Use { get; set; }
		public SoapParameterStyle ParameterStyle { get; set; }
	}

	[AttributeUsage(AttributeTargets.Method)]
	public sealed class SoapRpcMethodAttribute : Attribute {
		public SoapRpcMethodAttribute() { }
		public SoapRpcMethodAttribute(string action) { Action=action; }
		public string Action { get; set; }
		public string RequestNamespace { get; set; }
		public string ResponseNamespace { get; set; }
		public bool OneWay { get; set; }
		public string Binding { get; set; }
	}

	public class InvokeCompletedEventArgs : AsyncCompletedEventArgs {
		public InvokeCompletedEventArgs(object[] results,Exception error,bool cancelled,object userState) : base(error,cancelled,userState) {
			Results=results;
		}
		public object[] Results { get; }
	}

	public abstract class WebClientProtocol : Component {
		public string Url { get; set; }
		public int Timeout { get; set; } = 100000;
		public ICredentials Credentials { get; set; }
		public bool UseDefaultCredentials { get; set; }
		public void Abort() { }
	}

	public abstract class HttpWebClientProtocol : WebClientProtocol {
		public CookieContainer CookieContainer { get; set; }
		public IWebProxy Proxy { get; set; }
		public string UserAgent { get; set; }
		public bool AllowAutoRedirect { get; set; }
		public bool PreAuthenticate { get; set; }
		public bool EnableDecompression { get; set; }
		public System.Security.Cryptography.X509Certificates.X509CertificateCollection ClientCertificates { get; }
			= new System.Security.Cryptography.X509Certificates.X509CertificateCollection();
	}

	public abstract class SoapHttpClientProtocol : HttpWebClientProtocol {
		private static Exception NotSupported() =>
			new PlatformNotSupportedException("Classic ASMX SOAP calls are not supported in the headless Linux core build.");
		protected object[] Invoke(string methodName,object[] parameters) => throw NotSupported();
		protected IAsyncResult BeginInvoke(string methodName,object[] parameters,AsyncCallback callback,object asyncState) => throw NotSupported();
		protected object[] EndInvoke(IAsyncResult asyncResult) => throw NotSupported();
		protected void InvokeAsync(string methodName,object[] parameters,SendOrPostCallback callback) => throw NotSupported();
		protected void InvokeAsync(string methodName,object[] parameters,SendOrPostCallback callback,object userState) => throw NotSupported();
		public void CancelAsync(object userState) { }
		public void Discover() => throw NotSupported();
	}
}

// Modified for Apex Dental, 2026-10-25: restrict scanner destinations, redirects and resource usage; see NOTICE.
/*
 * Scanner acquisition for the Linux web demo, per the portability plan:
 *   1. eSCL/AirScan (driverless network scanning, plain HTTP+XML) — primary.
 *   2. SANE (Linux scanning stack; includes the hardware-free "test" backend) — fallback.
 *   3. POST /api/scan/upload — the contract a future local TWAIN agent (or any
 *      client) uses to push acquired images; intraoral sensors stay on the
 *      streamed Windows client until vendors expose DICOMweb/REST.
 * Acquired files flow through the real business layer via ImageStore.Import.
 */
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace OpenDentalWebDemo;

public record ScanSource(string Id,string Kind,string Name);

public static class ScanService {

	///<summary>eSCL scanner base URLs, e.g. "http://192.168.1.50:80/eSCL". Set via OD_ESCL_SCANNERS (comma separated).
	///Real deployments would add mDNS (_uscan._tcp) discovery here.</summary>
	static readonly string[] EsclScanners =
		(Environment.GetEnvironmentVariable("OD_ESCL_SCANNERS")??"")
		.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);

	public static List<ScanSource> GetSources() {
		List<ScanSource> list=new();
		foreach(string baseUrl in EsclScanners) {
			list.Add(new ScanSource("escl|"+baseUrl,"escl","eSCL scanner at "+new Uri(baseUrl).Host));
		}
		foreach(string dev in GetSaneDevices()) {
			list.Add(new ScanSource("sane|"+dev,"sane","SANE device "+dev));
		}
		list.Add(new ScanSource("upload","upload","Manual upload / TWAIN agent"));
		return list;
	}

	///<summary>Acquire one page from the given source id. Returns (bytes, file extension).</summary>
	static readonly SemaphoreSlim ScanSlot=new(1,1);
	public static async Task<(byte[] bytes,string ext)> Acquire(string sourceId) {
		string[] parts=(sourceId??"").Split('|',2);
		if(parts.Length!=2 || string.IsNullOrWhiteSpace(parts[1])) { throw new ArgumentException("Unknown scan source."); }
		if(parts[0]=="escl") {
			if(!EsclScanners.Contains(parts[1],StringComparer.Ordinal)) { throw new ArgumentException("Scanner is not configured."); }
			ValidateScanner(parts[1]);
		}
		else if(parts[0]!="sane" || !GetSaneDevices().Contains(parts[1],StringComparer.Ordinal)) {
			throw new ArgumentException("Scanner is not configured.");
		}
		if(!await ScanSlot.WaitAsync(0)) { throw new InvalidOperationException("Scanner is busy."); }
		try { return parts[0]=="escl"?await EsclScan(parts[1]):SaneScan(parts[1]); }
		finally { ScanSlot.Release(); }
	}
	static Uri ValidateScanner(string value) {
		if(!Uri.TryCreate(value,UriKind.Absolute,out var uri) || (uri.Scheme!="http" && uri.Scheme!="https")
			|| uri.UserInfo!="" || uri.Query!="" || uri.Fragment!="") {
			throw new ArgumentException("Invalid scanner configuration.");
		}
		return uri;
	}
	internal static Uri ValidateJob(string baseUrl,Uri location) {
		Uri scanner=ValidateScanner(baseUrl);
		Uri job=new Uri(scanner,location);
		string path=Uri.UnescapeDataString(job.AbsolutePath);
		if(job.Scheme!=scanner.Scheme || !job.Authority.Equals(scanner.Authority,StringComparison.OrdinalIgnoreCase)
			|| job.UserInfo!="" || job.Query!="" || job.Fragment!=""
			|| !path.StartsWith(scanner.AbsolutePath.TrimEnd('/')+"/ScanJobs/",StringComparison.Ordinal)
			|| path.Split('/').Any(part => part==".." || part==".") || path.Contains('\\')) {
			throw new InvalidOperationException("Scanner returned an unsafe job address.");
		}
		return job;
	}

	//----- eSCL (AirScan): POST ScanJobs, poll NextDocument until 404. -----

	static readonly HttpClient _http=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false,UseProxy=false }) { Timeout=TimeSpan.FromSeconds(120),MaxResponseContentBufferSize=WebSecurity.MaxBodyBytes };

	const string ESCL_SETTINGS="""
		<?xml version="1.0" encoding="UTF-8"?>
		<scan:ScanSettings xmlns:scan="http://schemas.hp.com/imaging/escl/2011/05/03"
		                   xmlns:pwg="http://www.pwg.org/schemas/2010/12/sm">
		  <pwg:Version>2.0</pwg:Version>
		  <pwg:InputSource>Platen</pwg:InputSource>
		  <scan:ColorMode>RGB24</scan:ColorMode>
		  <pwg:DocumentFormat>image/jpeg</pwg:DocumentFormat>
		  <scan:XResolution>300</scan:XResolution>
		  <scan:YResolution>300</scan:YResolution>
		</scan:ScanSettings>
		""";

	static async Task<(byte[],string)> EsclScan(string baseUrl) {
		using HttpRequestMessage req=new(HttpMethod.Post,baseUrl.TrimEnd('/')+"/ScanJobs");
		req.Content=new StringContent(ESCL_SETTINGS,Encoding.UTF8,"text/xml");
		using HttpResponseMessage resp=await _http.SendAsync(req);
		if((int)resp.StatusCode!=201 || resp.Headers.Location is null) {
			throw new Exception($"eSCL ScanJobs failed: {(int)resp.StatusCode}");
		}
		string jobUrl=ValidateJob(baseUrl,resp.Headers.Location).AbsoluteUri;
		using HttpResponseMessage docResp=await _http.GetAsync(jobUrl.TrimEnd('/')+"/NextDocument",HttpCompletionOption.ResponseHeadersRead);
		if(!docResp.IsSuccessStatusCode) {
			throw new Exception($"eSCL NextDocument failed: {(int)docResp.StatusCode}");
		}
		using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(120));
		using var data=await docResp.Content.ReadAsStreamAsync(deadline.Token);
		using var output=new MemoryStream();
		byte[] buffer=new byte[64*1024];
		int count;
		while((count=await data.ReadAsync(buffer,deadline.Token))>0) {
			if(output.Length+count>WebSecurity.MaxBodyBytes) { throw new InvalidDataException("Scanned document is too large."); }
			await output.WriteAsync(buffer.AsMemory(0,count),deadline.Token);
		}
		byte[] bytes=output.ToArray();
		string ext=docResp.Content.Headers.ContentType?.MediaType switch {
			"image/png" => ".png",
			"application/pdf" => ".pdf",
			_ => ".jpg",
		};
		//Best effort: tell the scanner we're done so it releases the job.
		try { await _http.DeleteAsync(jobUrl); } catch { }
		return (bytes,ext);
	}

	//----- SANE fallback: shell out to scanimage. -----

	static List<string> GetSaneDevices() {
		try {
			string output=RunProcess("scanimage",new[]{ "-L" },timeoutMs:15000);
			//Lines look like: device `test:0' is a Noname frontend-tester virtual device
			return output.Split('\n')
				.Select(x => System.Text.RegularExpressions.Regex.Match(x,"device `([^']+)'"))
				.Where(m => m.Success)
				.Select(m => m.Groups[1].Value)
				.ToList();
		}
		catch {
			return new List<string>();//scanimage not installed
		}
	}

	static (byte[],string) SaneScan(string device) {
		string tmpPath=Path.Combine(Path.GetTempPath(),"odscan_"+Guid.NewGuid().ToString("N")+".png");
		try {
			RunProcess("scanimage",new[]{ "-d",device,"--format=png","--resolution","300","-o",tmpPath },timeoutMs:120000);
			if(new FileInfo(tmpPath).Length>WebSecurity.MaxBodyBytes) { throw new InvalidDataException("Scanned document is too large."); }
			return (File.ReadAllBytes(tmpPath),".png");
		}
		finally {
			try { File.Delete(tmpPath); } catch { }
		}
	}

	static string RunProcess(string fileName,string[] args,int timeoutMs) {
		using Process process=new();
		process.StartInfo.FileName=fileName;
		foreach(string arg in args) {
			process.StartInfo.ArgumentList.Add(arg);
		}
		process.StartInfo.UseShellExecute=false;
		process.StartInfo.RedirectStandardOutput=true;
		process.StartInfo.RedirectStandardError=true;
		process.Start();
		var stdout=process.StandardOutput.ReadToEndAsync();
		var stderr=process.StandardError.ReadToEndAsync();
		if(!process.WaitForExit(timeoutMs)) {
			process.Kill(true);
			throw new TimeoutException(fileName+" timed out");
		}
		if(process.ExitCode!=0) {
			throw new Exception("Scanner process failed.");
		}
		return stdout.GetAwaiter().GetResult();
	}
}

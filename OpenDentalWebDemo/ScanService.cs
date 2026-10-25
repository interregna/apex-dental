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
	public static List<string> EsclScanners =
		(Environment.GetEnvironmentVariable("OD_ESCL_SCANNERS")??"")
		.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToList();

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
	public static async Task<(byte[] bytes,string ext)> Acquire(string sourceId) {
		string[] parts=sourceId.Split('|',2);
		return parts[0] switch {
			"escl" => await EsclScan(parts[1]),
			"sane" => SaneScan(parts[1]),
			_ => throw new ArgumentException("Unknown scan source: "+sourceId),
		};
	}

	//----- eSCL (AirScan): POST ScanJobs, poll NextDocument until 404. -----

	static readonly HttpClient _http=new HttpClient { Timeout=TimeSpan.FromSeconds(120) };

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
		string jobUrl=resp.Headers.Location.IsAbsoluteUri
			? resp.Headers.Location.ToString()
			: new Uri(new Uri(baseUrl),resp.Headers.Location).ToString();
		using HttpResponseMessage docResp=await _http.GetAsync(jobUrl.TrimEnd('/')+"/NextDocument");
		if(!docResp.IsSuccessStatusCode) {
			throw new Exception($"eSCL NextDocument failed: {(int)docResp.StatusCode}");
		}
		byte[] bytes=await docResp.Content.ReadAsByteArrayAsync();
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
		process.StartInfo.RedirectStandardOutput=true;
		process.StartInfo.RedirectStandardError=true;
		process.Start();
		string stdout=process.StandardOutput.ReadToEnd();
		string stderr=process.StandardError.ReadToEnd();
		if(!process.WaitForExit(timeoutMs)) {
			process.Kill(true);
			throw new TimeoutException(fileName+" timed out");
		}
		if(process.ExitCode!=0) {
			throw new Exception(fileName+" failed: "+stderr);
		}
		return stdout;
	}
}

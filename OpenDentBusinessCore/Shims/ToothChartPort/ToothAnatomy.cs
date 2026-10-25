/*
 * Loader for the projected tooth anatomy (ToothAnatomy.json, produced by
 * Tools/project_tooth_meshes.py from the SparksToothChart .obj meshes).
 * Coordinates are model-space outlines in "image" orientation: y grows
 * downward, root apex at top for upper teeth (the meshes are authored in
 * display orientation per arch, so lower teeth already point their roots down).
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace OpenDentBusiness.ToothChartPort {

	public class ToothAnatomyEntry {
		public float W;
		public float H;
		public Dictionary<string,List<List<float[]>>> Groups=new Dictionary<string,List<List<float[]>>>();
	}

	public static class ToothAnatomy {
		private static Dictionary<string,ToothAnatomyEntry> _entries;
		private static readonly object _lock=new object();

		public static Dictionary<string,ToothAnatomyEntry> Entries {
			get {
				if(_entries==null) {
					lock(_lock) {
						_entries??=Load();
					}
				}
				return _entries;
			}
		}

		static Dictionary<string,ToothAnatomyEntry> Load() {
			try {
				Assembly assembly=typeof(ToothAnatomy).Assembly;
				using Stream stream=assembly.GetManifestResourceStream("OpenDentBusinessCore.ToothAnatomy.json");
				if(stream==null) {
					return new Dictionary<string,ToothAnatomyEntry>();
				}
				using JsonDocument doc=JsonDocument.Parse(stream);
				var entries=new Dictionary<string,ToothAnatomyEntry>();
				foreach(JsonProperty tooth in doc.RootElement.EnumerateObject()) {
					ToothAnatomyEntry entry=new ToothAnatomyEntry() {
						W=tooth.Value.GetProperty("w").GetSingle(),
						H=tooth.Value.GetProperty("h").GetSingle(),
					};
					foreach(JsonProperty group in tooth.Value.GetProperty("groups").EnumerateObject()) {
						var outlines=new List<List<float[]>>();
						foreach(JsonElement outline in group.Value.EnumerateArray()) {
							var points=new List<float[]>();
							foreach(JsonElement point in outline.EnumerateArray()) {
								points.Add(new[] { point[0].GetSingle(),point[1].GetSingle() });
							}
							outlines.Add(points);
						}
						entry.Groups[group.Name]=outlines;
					}
					entries[tooth.Name]=entry;
				}
				return entries;
			}
			catch(Exception ex) {
				Console.Error.WriteLine("[ToothAnatomy] failed to load outlines, falling back to schematic: "+ex.Message);
				return new Dictionary<string,ToothAnatomyEntry>();
			}
		}

		///<summary>Mesh resource for a tooth id, mirroring ToothGraphic.ImportObj: contralateral
		///pairs share a mesh; teeth 9-24 are drawn mirrored.</summary>
		public static string MeshKeyFor(string toothId) {
			switch(toothId) {
				case "1": case "16": return "tooth1";
				case "2": case "15": return "tooth2";
				case "3": case "14": return "tooth3";
				case "4": case "13": return "tooth4";
				case "5": case "12": return "tooth5";
				case "6": case "11": return "tooth6";
				case "7": case "10": return "tooth7";
				case "8": case "9": return "tooth8";
				case "17": case "32": return "tooth32";
				case "18": case "31": return "tooth31";
				case "19": case "30": return "tooth30";
				case "20": case "29": return "tooth29";
				case "21": case "28": return "tooth28";
				case "22": case "27": return "tooth27";
				case "23": case "24": case "25": case "26": return "tooth25";
				case "A": case "J": return "toothA";
				case "B": case "I": return "toothB";
				case "C": case "H": return "toothC";
				case "D": case "G": return "toothD";
				case "E": case "F": return "toothE";
				case "P": case "O": case "Q": case "N": return "toothP";
				case "R": case "M": return "toothR";
				case "S": case "L": return "toothS";
				case "T": case "K": return "toothT";
				default: return null;
			}
		}

		public static bool IsMirrored(string toothId) {
			if(int.TryParse(toothId,out int tooth)) {
				return tooth>=9 && tooth<=24;
			}
			//Primary letters on the left side mirror too (F..O).
			char c=toothId.Length==1?toothId[0]:' ';
			return c>='F' && c<='O';
		}
	}
}

/*
 * Preferences editor: a searchable key/value editor over the `preference` table
 * via the real Prefs class. Retires the long tail of tiny Setup dialogs whose only
 * job is toggling a single pref.
 *
 * Reads come from the pref cache (Prefs.GetTableFromCache(false)) — no SQL. Writes go
 * through the Prefs.Update* helpers (UpdateBool / UpdateLong / UpdateString / UpdateRaw),
 * which log the change into the cache; we then Prefs.RefreshCache(),
 * Signalods.SetInvalid(InvalidType.Prefs), and stamp a SecurityLogs.MakeLogEntry(Setup,...).
 */
using System;
using System.Data;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SetupPreferencesModule {
	record PrefUpdateDto(string Value);

	//Best-effort input hint so the UI can pick a checkbox vs. a number/text field.
	static string KindOf(string value) {
		if(value=="0" || value=="1" || value=="True" || value=="False") {
			return "bool";
		}
		if(!string.IsNullOrEmpty(value) && double.TryParse(value,out _)) {
			return "number";
		}
		return "text";
	}

	public static void Map(WebApplication app) {

		//---------- list all prefs, searchable (retires the scattered pref toggles) ----------
		app.MapGet("/api/prefs",(string? search) => {
			string needle=(search??"").Trim().ToLowerInvariant();
			DataTable table=Prefs.GetTableFromCache(false);//cache read, no SQL
			var all=table.AsEnumerable()
				.Select(row => new {
					prefName=row["PrefName"].ToString(),
					valueString=row["ValueString"].ToString(),
				})
				.Where(x => needle=="" || (x.prefName??"").ToLowerInvariant().Contains(needle))
				.OrderBy(x => x.prefName,StringComparer.OrdinalIgnoreCase)
				.ToList();
			int totalMatches=all.Count;
			var rows=all.Take(300).Select(x => new {
				prefName=x.prefName,
				valueString=x.valueString,
				kind=KindOf(x.valueString),
			});
			return Results.Json(new { totalMatches,rows });
		});

		//---------- one pref ----------
		app.MapGet("/api/prefs/{prefName}",(string prefName) => {
			if(!Prefs.GetContainsKey(prefName)) {
				return Results.NotFound();
			}
			Pref pref=Prefs.GetOne(prefName);
			return Results.Json(new {
				prefName=pref.PrefName,
				valueString=pref.ValueString,
				kind=KindOf(pref.ValueString),
			});
		});

		//---------- update one pref ----------
		app.MapPut("/api/prefs/{prefName}",(string prefName,PrefUpdateDto dto) => {
			if(!Prefs.GetContainsKey(prefName)) {
				return Results.NotFound();
			}
			string newValue=dto?.Value??"";
			Pref prefOld=Prefs.GetOne(prefName);
			string oldValue=prefOld.ValueString??"";
			//Route through the typed Prefs.Update* helper matching the value's kind. Each helper
			//sets the cache (Prefs.UpdateValueForKey) as part of the write. UpdateRaw is the fallback
			//for any prefName that isn't a known PrefName enum member.
			string kind=KindOf(newValue);
			if(Enum.TryParse(prefName,out PrefName prefNameEnum)) {
				if(kind=="bool") {
					bool boolVal=(newValue=="1" || string.Equals(newValue,"True",StringComparison.OrdinalIgnoreCase));
					Prefs.UpdateBool(prefNameEnum,boolVal);
				}
				else if(kind=="number" && long.TryParse(newValue,out long longVal)) {
					Prefs.UpdateLong(prefNameEnum,longVal);
				}
				else {
					Prefs.UpdateString(prefNameEnum,newValue);
				}
			}
			else {
				Prefs.UpdateRaw(prefName,newValue);//non-standard pref name not in the PrefName enum
			}
			Prefs.RefreshCache();
			Signalods.SetInvalid(InvalidType.Prefs);
			SecurityLogs.MakeLogEntry(EnumPermType.Setup,0,
				"Preference "+prefName+" changed from '"+oldValue+"' to '"+newValue+"'.");
			Pref prefNew=Prefs.GetOne(prefName);
			return Results.Json(new {
				prefName=prefNew.PrefName,
				valueString=prefNew.ValueString,
				kind=KindOf(prefNew.ValueString),
			});
		});
	}
}

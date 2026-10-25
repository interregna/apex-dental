/*
 * Ortho chart ENTRY (web slice of FormOrthoChart).  The ortho chart is a dated grid:
 * each row is an OrthoChartRow (DateTimeService + ProvNum + optional Signature) and every
 * cell is an OrthoChart row keyed to that OrthoChartRow via OrthoChartRowNum, carrying one
 * FieldName/FieldValue pair.  The columns come from DisplayFields.GetForCategory(OrthoChart);
 * for an ortho display field the column KEY (== OrthoChart.FieldName) is the field's
 * Description, the visible header is DescriptionOverride (or Description), and the special
 * InternalName values "Signature"/"Provider" are computed columns, not editable text.
 *
 * Writes use the same business calls FormOrthoChart makes: OrthoChartRows.Insert/Update and
 * OrthoCharts.Insert/Update/Delete (Crud.OrthoChartCrud.SelectOne to fetch one cell, since the
 * Data-Interface GetOne is commented out upstream).  Editing/deleting/adding a cell on a signed
 * row clears that row's Signature (the desktop invalidates the hash the same way).  Every
 * mutation stamps a SecurityLogs OrthoChartEditFull entry whose text ends in the service date as
 * yyyyMMdd so OrthoCharts.GetOrthoDateFromLog can recover it.
 *
 * OrthoChart has NO default columns (DisplayFields.GetDefaultList returns an empty list for the
 * category), so on a demo DB with none configured GET /api/ortho/fields returns [] cleanly and
 * the grid shows an empty state rather than crashing.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class ClinicalOrthoChartModule {
	record NewRowDto(DateTime Date,Dictionary<string,string> Values);
	record CellEditDto(string Value);

	//A display field is an editable ortho text/pick column unless its InternalName marks it a
	//computed column (Signature is drawn from OrthoChartRow.Signature; Provider from ProvNum).
	static bool IsEditableField(DisplayField df)
		=> df.InternalName!="Signature" && df.InternalName!="Provider";

	//The key that ortho cells are stored under (OrthoChart.FieldName). For ordinary fields this
	//is Description; Signature/Provider carry the key in InternalName when Description is blank.
	static string FieldKey(DisplayField df)
		=> string.IsNullOrEmpty(df.Description)?df.InternalName:df.Description;

	static object FieldDto(DisplayField df) => new {
		fieldName=FieldKey(df),
		description=!string.IsNullOrEmpty(df.DescriptionOverride)?df.DescriptionOverride
			:(!string.IsNullOrEmpty(df.Description)?df.Description:df.InternalName),
		internalName=df.InternalName??"",
		editable=IsEditableField(df),
		pickList=string.IsNullOrEmpty(df.PickList)
			?new List<string>()
			:df.PickList.Split(new[]{"\r\n"},StringSplitOptions.None).ToList(),
	};

	static List<DisplayField> GetOrthoFields() {
		DisplayFields.RefreshCache();//pick up any columns configured after startup
		return DisplayFields.GetForCategory(DisplayFieldCategory.OrthoChart)
			.OrderBy(x => x.ItemOrder).ToList();
	}

	//An edit to any cell on a signed row invalidates the stored signature hash, so clear it.
	static void InvalidateSignature(long orthoChartRowNum) {
		if(orthoChartRowNum<=0) {
			return;
		}
		OrthoChartRow row=OrthoChartRows.GetOne(orthoChartRowNum);
		if(row!=null && !string.IsNullOrEmpty(row.Signature)) {
			row.Signature="";
			OrthoChartRows.Update(row);
		}
	}

	static void LogOrtho(long patNum,string text,DateTime dateService)
		=> SecurityLogs.MakeLogEntry(EnumPermType.OrthoChartEditFull,patNum,
			text+" "+dateService.ToString("yyyyMMdd"));//8-digit date must be last for GetOrthoDateFromLog

	public static void Map(WebApplication app) {
		//The ortho column set (DisplayFields for the OrthoChart category, ordered). Empty when
		//no columns have been configured — OrthoChart has no defaults.
		app.MapGet("/api/ortho/fields",() => {
			return Results.Json(GetOrthoFields().Select(FieldDto));
		});

		//The patient's ortho rows grouped by date, plus the field columns. Each row exposes the
		//value map (key -> FieldValue) and a parallel cellNums map (key -> OrthoChartNum) so the
		//UI can address individual cells for PUT/DELETE.
		app.MapGet("/api/patients/{patNum:long}/orthochart",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<DisplayField> listFields=GetOrthoFields();
			List<OrthoChartRow> listRows=OrthoChartRows.GetAllForPatient(patNum)
				.OrderBy(x => x.DateTimeService).ToList();
			return Results.Json(new {
				fields=listFields.Select(FieldDto),
				rows=listRows.Select(r => new {
					orthoChartRowNum=r.OrthoChartRowNum,
					date=r.DateTimeService.ToString("yyyy-MM-dd"),
					provNum=r.ProvNum,
					provAbbr=Providers.GetAbbr(r.ProvNum),
					hasSignature=!string.IsNullOrEmpty(r.Signature),
					values=r.ListOrthoCharts
						.GroupBy(c => c.FieldName)
						.ToDictionary(g => g.Key,g => g.First().FieldValue??""),
					cellNums=r.ListOrthoCharts
						.GroupBy(c => c.FieldName)
						.ToDictionary(g => g.Key,g => g.First().OrthoChartNum),
				}),
			});
		});

		//Add a dated visit row: create (or reuse) the OrthoChartRow for that date, then insert an
		//OrthoChart cell for each supplied non-empty value that matches an editable ortho column.
		app.MapPost("/api/patients/{patNum:long}/orthochart",(long patNum,NewRowDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			if(dto==null || dto.Date==DateTime.MinValue) {
				return Results.Text("A visit date is required.",statusCode:400);
			}
			DateTime dateService=dto.Date.Date;
			//Only fields configured as editable ortho columns can be written; keyed by column key.
			Dictionary<string,DisplayField> mapEditable=GetOrthoFields()
				.Where(IsEditableField)
				.GroupBy(FieldKey).ToDictionary(g => g.Key,g => g.First());
			List<OrthoChartRow> listRows=OrthoChartRows.GetAllForPatient(patNum);
			OrthoChartRow row=listRows.FirstOrDefault(x => x.DateTimeService.Date==dateService);
			if(row==null) {
				row=new OrthoChartRow() {
					PatNum=patNum,
					DateTimeService=dateService,
					ProvNum=pat.PriProv,
					UserNum=Security.CurUser.UserNum,
					Signature="",
				};
				row.OrthoChartRowNum=OrthoChartRows.Insert(row);
			}
			List<OrthoChart> listExisting=OrthoChartRows.GetOne(row.OrthoChartRowNum)?.ListOrthoCharts
				??new List<OrthoChart>();
			int inserted=0,updated=0;
			foreach(KeyValuePair<string,string> kv in dto.Values??new Dictionary<string,string>()) {
				string val=kv.Value??"";
				if(val=="" || !mapEditable.ContainsKey(kv.Key)) {
					continue;//blank cells are not stored; unknown/computed columns are ignored
				}
				OrthoChart cellExisting=listExisting.FirstOrDefault(x => x.FieldName==kv.Key);
				if(cellExisting!=null) {
					if(cellExisting.FieldValue!=val) {
						cellExisting.FieldValue=val;
						cellExisting.UserNum=Security.CurUser.UserNum;
						OrthoCharts.Update(cellExisting);
						updated++;
					}
				}
				else {
					OrthoCharts.Insert(new OrthoChart() {
						PatNum=patNum,
						DateService=dateService,
						FieldName=kv.Key,
						FieldValue=val,
						UserNum=Security.CurUser.UserNum,
						ProvNum=pat.PriProv,
						OrthoChartRowNum=row.OrthoChartRowNum,
					});
					inserted++;
				}
			}
			if(inserted>0 || updated>0) {
				InvalidateSignature(row.OrthoChartRowNum);
				LogOrtho(patNum,"Ortho chart row saved.",dateService);
			}
			return Results.Json(new { orthoChartRowNum=row.OrthoChartRowNum,inserted,updated });
		});

		//Edit a single cell's value. An empty value deletes the cell (the desktop clears cells the
		//same way — blank ortho values are never stored).
		app.MapPut("/api/orthochart/{orthoChartNum:long}",(long orthoChartNum,CellEditDto dto) => {
			OrthoChart cell=OpenDentBusiness.Crud.OrthoChartCrud.SelectOne(orthoChartNum);
			if(cell==null) {
				return Results.NotFound();
			}
			string val=dto?.Value??"";
			string oldVal=cell.FieldValue??"";
			if(val==oldVal) {
				return Results.Json(new { orthoChartNum,unchanged=true });
			}
			DateTime dateService=OrthoChartRows.GetOne(cell.OrthoChartRowNum)?.DateTimeService??cell.DateService;
			if(val=="") {
				OrthoCharts.Delete(cell.OrthoChartNum);
			}
			else {
				cell.FieldValue=val;
				cell.UserNum=Security.CurUser.UserNum;
				OrthoCharts.Update(cell);
			}
			InvalidateSignature(cell.OrthoChartRowNum);
			LogOrtho(cell.PatNum,
				$"Ortho chart field edited. Field name: {cell.FieldName} Old value: \"{oldVal}\"  New value: \"{val}\"",
				dateService);
			return Results.Json(new { orthoChartNum,deleted=val=="" });
		});

		//Delete a single cell.
		app.MapDelete("/api/orthochart/{orthoChartNum:long}",(long orthoChartNum) => {
			OrthoChart cell=OpenDentBusiness.Crud.OrthoChartCrud.SelectOne(orthoChartNum);
			if(cell==null) {
				return Results.NotFound();
			}
			DateTime dateService=OrthoChartRows.GetOne(cell.OrthoChartRowNum)?.DateTimeService??cell.DateService;
			OrthoCharts.Delete(cell.OrthoChartNum);
			InvalidateSignature(cell.OrthoChartRowNum);
			LogOrtho(cell.PatNum,
				$"Ortho chart field deleted. Field name: {cell.FieldName} Old value: \"{cell.FieldValue}\"",
				dateService);
			return Results.Ok();
		});
	}
}

/*
 * Appointment Types and Auto Codes setup: CRUD over the real business layer.
 * Appointment types via AppointmentTypes.GetDeepCopy/Insert/Update/Delete with
 * CheckInUse guarding deletes; auto codes via AutoCodes.GetListDeep/Insert/Update/Delete
 * with each code's AutoCodeItems + AutoCodeConds returned for the expandable detail rows.
 * Retires FormApptTypes, FormApptTypeEdit, FormAutoCode, FormAutoCodeEdit.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SetupApptTypesAutoCodesModule {
	record ApptTypeDto(string AppointmentTypeName,string Color,string Pattern,string CodeStr,string CodeStrRequired,
		string RequiredProcCodesNeeded,string BlockoutTypes,bool IsHidden);
	record AutoCodeDto(string Description,bool IsHidden,bool LessIntrusive);

	public static void Map(WebApplication app) {
		string Hex(System.Drawing.Color c) => c.ToArgb()==0?"":$"#{c.R:X2}{c.G:X2}{c.B:X2}";
		System.Drawing.Color ParseColor(string hex) =>
			string.IsNullOrWhiteSpace(hex)?System.Drawing.Color.Empty:System.Drawing.ColorTranslator.FromHtml(hex);
		//Time patterns are stored in 5 minute increments using X (provider) and / (assistant) only.
		bool IsValidPattern(string pattern) => (pattern??"").All(x => x=='X' || x=='/');

		//---------- Appointment types (FormApptTypes / FormApptTypeEdit) ----------

		app.MapGet("/api/setup/appttypes",() => {
			AppointmentTypes.RefreshCache();//appointmenttype cache is not part of the startup Cache.Refresh set
			List<Def> listDefsBlockout=Defs.GetDefsForCategory(DefCat.BlockoutTypes,false);
			return Results.Json(AppointmentTypes.GetDeepCopy(false).Select(x => new {
				appointmentTypeNum=x.AppointmentTypeNum,
				appointmentTypeName=x.AppointmentTypeName,
				color=Hex(x.AppointmentTypeColor),
				itemOrder=x.ItemOrder,
				isHidden=x.IsHidden,
				pattern=x.Pattern,
				codeStr=x.CodeStr,
				codeStrRequired=x.CodeStrRequired,
				requiredProcCodesNeeded=x.RequiredProcCodesNeeded.ToString(),
				blockoutTypes=x.BlockoutTypes,
				blockoutTypeNames=string.Join(", ",(x.BlockoutTypes??"").Split(',',StringSplitOptions.RemoveEmptyEntries)
					.Select(y => long.TryParse(y,out long defNum)?(listDefsBlockout.FirstOrDefault(z => z.DefNum==defNum)?.ItemName??y):y)),
			}));
		});

		app.MapPost("/api/setup/appttypes",(ApptTypeDto dto) => {
			if(string.IsNullOrWhiteSpace(dto.AppointmentTypeName)) {
				return Results.Text("Appointment type name is required",statusCode:400);
			}
			if(!IsValidPattern(dto.Pattern)) {
				return Results.Text("Pattern may only contain X and / characters",statusCode:400);
			}
			AppointmentType appointmentType=new AppointmentType();
			appointmentType.AppointmentTypeName=dto.AppointmentTypeName;
			appointmentType.AppointmentTypeColor=ParseColor(dto.Color);
			appointmentType.Pattern=dto.Pattern??"";
			appointmentType.CodeStr=dto.CodeStr??"";
			appointmentType.CodeStrRequired=dto.CodeStrRequired??"";
			appointmentType.RequiredProcCodesNeeded=Enum.TryParse(dto.RequiredProcCodesNeeded,out EnumRequiredProcCodesNeeded requiredNeeded)
				?requiredNeeded:EnumRequiredProcCodesNeeded.None;
			appointmentType.BlockoutTypes=dto.BlockoutTypes??"";
			appointmentType.IsHidden=dto.IsHidden;
			appointmentType.ItemOrder=AppointmentTypes.GetDeepCopy(false).Count;//append at the end, same as the desktop dialog
			long appointmentTypeNum=AppointmentTypes.Insert(appointmentType);
			AppointmentTypes.RefreshCache();
			return Results.Json(new { appointmentTypeNum });
		});

		app.MapPut("/api/setup/appttypes/{appointmentTypeNum:long}",(long appointmentTypeNum,ApptTypeDto dto) => {
			AppointmentTypes.RefreshCache();
			AppointmentType appointmentType=AppointmentTypes.GetOne(appointmentTypeNum);
			if(appointmentType==null) {
				return Results.NotFound();
			}
			if(string.IsNullOrWhiteSpace(dto.AppointmentTypeName)) {
				return Results.Text("Appointment type name is required",statusCode:400);
			}
			if(!IsValidPattern(dto.Pattern)) {
				return Results.Text("Pattern may only contain X and / characters",statusCode:400);
			}
			appointmentType=appointmentType.Copy();
			appointmentType.AppointmentTypeName=dto.AppointmentTypeName;
			appointmentType.AppointmentTypeColor=ParseColor(dto.Color);
			appointmentType.Pattern=dto.Pattern??"";
			appointmentType.CodeStr=dto.CodeStr??"";
			appointmentType.CodeStrRequired=dto.CodeStrRequired??"";
			appointmentType.RequiredProcCodesNeeded=Enum.TryParse(dto.RequiredProcCodesNeeded,out EnumRequiredProcCodesNeeded requiredNeeded)
				?requiredNeeded:EnumRequiredProcCodesNeeded.None;
			appointmentType.BlockoutTypes=dto.BlockoutTypes??"";
			appointmentType.IsHidden=dto.IsHidden;
			AppointmentTypes.Update(appointmentType);
			AppointmentTypes.RefreshCache();
			return Results.Ok();
		});

		app.MapDelete("/api/setup/appttypes/{appointmentTypeNum:long}",(long appointmentTypeNum) => {
			AppointmentTypes.RefreshCache();
			AppointmentType appointmentType=AppointmentTypes.GetOne(appointmentTypeNum);
			if(appointmentType==null) {
				return Results.NotFound();
			}
			string strInUse=AppointmentTypes.CheckInUse(appointmentTypeNum);
			if(strInUse!="") {
				return Results.Text(strInUse,statusCode:409);
			}
			try {
				AppointmentTypes.Delete(appointmentTypeNum);//re-checks CheckInUse internally and throws if in use
			}
			catch(ApplicationException ex) {
				return Results.Text(ex.Message,statusCode:409);
			}
			AppointmentTypes.RefreshCache();
			return Results.Ok();
		});

		//---------- Auto codes (FormAutoCode / FormAutoCodeEdit) ----------

		app.MapGet("/api/setup/autocodes",() => {
			//These caches are not part of the startup Cache.Refresh set.
			AutoCodes.RefreshCache();
			AutoCodeItems.RefreshCache();
			AutoCodeConds.RefreshCache();
			return Results.Json(AutoCodes.GetListDeep(false).OrderBy(x => x.Description).Select(x => new {
				autoCodeNum=x.AutoCodeNum,
				description=x.Description,
				isHidden=x.IsHidden,
				lessIntrusive=x.LessIntrusive,
				items=AutoCodeItems.GetListForCode(x.AutoCodeNum).Select(y => new {
					autoCodeItemNum=y.AutoCodeItemNum,
					codeNum=y.CodeNum,
					procCode=ProcedureCodes.GetStringProcCode(y.CodeNum,doThrowIfMissing:false),
					conditions=AutoCodeConds.GetListForItem(y.AutoCodeItemNum).Select(z => z.Cond.ToString()).ToList(),
				}).ToList(),
			}));
		});

		app.MapPost("/api/setup/autocodes",(AutoCodeDto dto) => {
			if(string.IsNullOrWhiteSpace(dto.Description)) {
				return Results.Text("Description is required",statusCode:400);
			}
			AutoCode autoCode=new AutoCode();
			autoCode.Description=dto.Description;
			autoCode.IsHidden=dto.IsHidden;
			autoCode.LessIntrusive=dto.LessIntrusive;
			long autoCodeNum=AutoCodes.Insert(autoCode);
			AutoCodes.RefreshCache();
			return Results.Json(new { autoCodeNum });
		});

		app.MapPut("/api/setup/autocodes/{autoCodeNum:long}",(long autoCodeNum,AutoCodeDto dto) => {
			AutoCodes.RefreshCache();
			AutoCode autoCode=AutoCodes.GetOne(autoCodeNum);
			if(autoCode==null) {
				return Results.NotFound();
			}
			if(string.IsNullOrWhiteSpace(dto.Description)) {
				return Results.Text("Description is required",statusCode:400);
			}
			autoCode=autoCode.Copy();
			autoCode.Description=dto.Description;
			autoCode.IsHidden=dto.IsHidden;
			autoCode.LessIntrusive=dto.LessIntrusive;
			AutoCodes.Update(autoCode);
			AutoCodes.RefreshCache();
			return Results.Ok();
		});

		app.MapDelete("/api/setup/autocodes/{autoCodeNum:long}",(long autoCodeNum) => {
			AutoCodes.RefreshCache();
			AutoCode autoCode=AutoCodes.GetOne(autoCodeNum);
			if(autoCode==null) {
				return Results.NotFound();
			}
			try {
				AutoCodes.Delete(autoCode);//throws if in use by procedure buttons; also deletes items and conditions
			}
			catch(ApplicationException ex) {
				return Results.Text(ex.Message,statusCode:409);
			}
			AutoCodes.RefreshCache();
			AutoCodeItems.RefreshCache();
			AutoCodeConds.RefreshCache();
			return Results.Ok();
		});
	}
}

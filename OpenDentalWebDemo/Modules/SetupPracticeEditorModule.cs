/*
 * Practice resources editor: write actions on top of the read-only Setup module.
 * Definitions add/edit/hide (Defs.Insert/Update/HideDef), operatory add/edit
 * (Operatories.Insert/Update), and provider edit (Providers.Update).
 * Retires FormDefinitions, FormDefEdit, FormOperatories, FormOperatoryEdit, FormProvEdit.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SetupPracticeEditorModule {
	record NewDef(string Category,string ItemName,string ItemValue,string Color);
	record EditDef(string ItemName,string ItemValue,string Color,bool IsHidden);
	record NewOperatory(string OpName,string Abbrev,long ProvDentist,long ProvHygienist,bool IsHygiene);
	record EditOperatory(string OpName,string Abbrev,long ProvDentist,long ProvHygienist,bool IsHygiene,bool IsHidden);
	record EditProvider(string Abbr,string LName,string FName,string Suffix,string Color,bool IsHidden,bool IsSecondary);

	public static void Map(WebApplication app) {
		string Hex(System.Drawing.Color c) => c.ToArgb()==0?"":$"#{c.R:X2}{c.G:X2}{c.B:X2}";
		System.Drawing.Color ParseColor(string hex) =>
			string.IsNullOrWhiteSpace(hex)?System.Drawing.Color.Empty:System.Drawing.ColorTranslator.FromHtml(hex);
		//Definitions can only be addressed by DefNum from the UI; category never changes after insert,
		//so locate an existing def by scanning the cached category lists.
		Def FindDef(long defNum) => Defs.GetDeepCopy(false).SelectMany(x => x.Value).FirstOrDefault(x => x.DefNum==defNum);

		//---------- Definitions (FormDefinitions / FormDefEdit) ----------

		app.MapPost("/api/setup/defs",(NewDef dto) => {
			if(!Enum.TryParse(dto.Category,out DefCat defCat)) {
				return Results.Text("Unknown DefCat: "+dto.Category,statusCode:400);
			}
			if(string.IsNullOrWhiteSpace(dto.ItemName)) {
				return Results.Text("ItemName is required",statusCode:400);
			}
			Def def=new Def();
			def.Category=defCat;
			def.ItemName=dto.ItemName;
			def.ItemValue=dto.ItemValue??"";
			def.ItemColor=ParseColor(dto.Color);
			def.ItemOrder=Defs.GetDefsForCategory(defCat,false).Count;//append at the end, same as the desktop dialog
			long defNum=Defs.Insert(def);
			Defs.RefreshCache();
			return Results.Json(new { defNum });
		});

		app.MapPut("/api/setup/defs/{defNum:long}",(long defNum,EditDef dto) => {
			Def def=FindDef(defNum);
			if(def==null) {
				return Results.NotFound();
			}
			if(string.IsNullOrWhiteSpace(dto.ItemName)) {
				return Results.Text("ItemName is required",statusCode:400);
			}
			def=def.Copy();
			def.ItemName=dto.ItemName;
			def.ItemValue=dto.ItemValue??"";
			def.ItemColor=ParseColor(dto.Color);
			def.IsHidden=dto.IsHidden;
			Defs.Update(def);
			Defs.RefreshCache();
			return Results.Ok();
		});

		app.MapPost("/api/setup/defs/{defNum:long}/hide",(long defNum) => {
			Def def=FindDef(defNum);
			if(def==null) {
				return Results.NotFound();
			}
			Defs.HideDef(def.Copy());
			Defs.RefreshCache();
			return Results.Ok();
		});

		//---------- Operatories (FormOperatories / FormOperatoryEdit) ----------

		app.MapGet("/api/setup/operatories/{opNum:long}",(long opNum) => {
			Operatories.RefreshCache();//operatory cache is not part of the startup Cache.Refresh set
			Operatory op=Operatories.GetOperatory(opNum);
			if(op==null) {
				return Results.NotFound();
			}
			return Results.Json(new {
				operatoryNum=op.OperatoryNum,
				opName=op.OpName,
				abbrev=op.Abbrev,
				provDentist=op.ProvDentist,
				provHygienist=op.ProvHygienist,
				isHygiene=op.IsHygiene,
				isHidden=op.IsHidden,
				itemOrder=op.ItemOrder,
			});
		});

		app.MapPost("/api/setup/operatories",(NewOperatory dto) => {
			if(string.IsNullOrWhiteSpace(dto.OpName) || string.IsNullOrWhiteSpace(dto.Abbrev)) {
				return Results.Text("OpName and Abbrev are required",statusCode:400);
			}
			Operatories.RefreshCache();
			List<Operatory> listOps=Operatories.GetDeepCopy(false);
			Operatory op=new Operatory();
			op.OpName=dto.OpName;
			op.Abbrev=dto.Abbrev;
			op.ProvDentist=dto.ProvDentist;
			op.ProvHygienist=dto.ProvHygienist;
			op.IsHygiene=dto.IsHygiene;
			op.ItemOrder=listOps.Count==0?0:listOps.Max(x => x.ItemOrder)+1;
			long operatoryNum=Operatories.Insert(op);
			Operatories.RefreshCache();
			return Results.Json(new { operatoryNum });
		});

		app.MapPut("/api/setup/operatories/{opNum:long}",(long opNum,EditOperatory dto) => {
			Operatories.RefreshCache();
			Operatory op=Operatories.GetOperatory(opNum);
			if(op==null) {
				return Results.NotFound();
			}
			if(string.IsNullOrWhiteSpace(dto.OpName) || string.IsNullOrWhiteSpace(dto.Abbrev)) {
				return Results.Text("OpName and Abbrev are required",statusCode:400);
			}
			op=op.Copy();
			op.OpName=dto.OpName;
			op.Abbrev=dto.Abbrev;
			op.ProvDentist=dto.ProvDentist;
			op.ProvHygienist=dto.ProvHygienist;
			op.IsHygiene=dto.IsHygiene;
			op.IsHidden=dto.IsHidden;
			Operatories.Update(op);
			Operatories.RefreshCache();
			return Results.Ok();
		});

		//---------- Providers (FormProvEdit, edit path) ----------

		app.MapGet("/api/setup/providers/{provNum:long}",(long provNum) => {
			Provider prov=Providers.GetProv(provNum);
			if(prov==null) {
				return Results.NotFound();
			}
			return Results.Json(new {
				provNum=prov.ProvNum,
				abbr=prov.Abbr,
				lName=prov.LName,
				fName=prov.FName,
				suffix=prov.Suffix,
				color=Hex(prov.ProvColor),
				isHidden=prov.IsHidden,
				isSecondary=prov.IsSecondary,
			});
		});

		app.MapPut("/api/setup/providers/{provNum:long}",(long provNum,EditProvider dto) => {
			Provider prov=Providers.GetProv(provNum);
			if(prov==null) {
				return Results.NotFound();
			}
			if(string.IsNullOrWhiteSpace(dto.Abbr) || string.IsNullOrWhiteSpace(dto.LName)) {
				return Results.Text("Abbr and LName are required",statusCode:400);
			}
			prov=prov.Copy();
			prov.Abbr=dto.Abbr;
			prov.LName=dto.LName;
			prov.FName=dto.FName??"";
			prov.Suffix=dto.Suffix??"";
			prov.ProvColor=ParseColor(dto.Color);
			prov.IsHidden=dto.IsHidden;
			prov.IsSecondary=dto.IsSecondary;
			Providers.Update(prov);
			Providers.RefreshCache();
			return Results.Ok();
		});
	}
}

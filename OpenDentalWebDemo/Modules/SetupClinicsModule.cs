/*
 * Clinics setup: list / create / edit / hide clinics through the real Clinics
 * business layer (Clinics.GetDeepCopy / GetFirstOrDefault / Insert / Update /
 * RefreshCache). Retires FormClinics and the core of FormClinicEdit.
 * Writes are audit-logged (SecurityLogs.MakeLogEntry) and invalidate the
 * Providers cache signal, matching FormClinics_Closing on the desktop.
 * Clinics are not in the Program.cs startup Cache.Refresh set, so every
 * handler calls Clinics.RefreshCache() first (same pattern as operatories).
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SetupClinicsModule {
	record NewClinic(string Description,string Abbr,string Address,string Address2,string City,string State,
		string Zip,string Phone,long DefaultProv,bool IsHidden);
	record EditClinic(string Description,string Abbr,string Address,string Address2,string City,string State,
		string Zip,string Phone,long DefaultProv,bool IsHidden);

	public static void Map(WebApplication app) {

		app.MapGet("/api/clinics",(bool includeHidden=false) => {
			Clinics.RefreshCache();//clinics are not part of the startup Cache.Refresh set
			List<Clinic> listClinics=Clinics.GetDeepCopy(!includeHidden)//isShort=true excludes hidden
				.OrderBy(x => x.ItemOrder).ToList();
			return Results.Json(listClinics.Select(x => new {
				clinicNum=x.ClinicNum,
				description=x.Description,
				abbr=x.Abbr,
				address=x.Address,
				city=x.City,
				state=x.State,
				zip=x.Zip,
				phone=x.Phone,
				defaultProv=x.DefaultProv,
				isHidden=x.IsHidden,
				itemOrder=x.ItemOrder,
			}));
		});

		app.MapGet("/api/clinics/{clinicNum:long}",(long clinicNum) => {
			Clinics.RefreshCache();
			Clinic clinic=Clinics.GetFirstOrDefault(x => x.ClinicNum==clinicNum);
			if(clinic==null) {
				return Results.NotFound();
			}
			return Results.Json(new {
				clinicNum=clinic.ClinicNum,
				description=clinic.Description,
				abbr=clinic.Abbr,
				address=clinic.Address,
				address2=clinic.Address2,
				city=clinic.City,
				state=clinic.State,
				zip=clinic.Zip,
				phone=clinic.Phone,
				defaultProv=clinic.DefaultProv,
				isHidden=clinic.IsHidden,
				itemOrder=clinic.ItemOrder,
			});
		});

		app.MapPost("/api/clinics",(NewClinic dto) => {
			if(string.IsNullOrWhiteSpace(dto.Description)) {
				return Results.Text("Description cannot be blank",statusCode:400);
			}
			if(string.IsNullOrWhiteSpace(dto.Abbr)) {
				return Results.Text("Abbreviation cannot be blank",statusCode:400);
			}
			Clinics.RefreshCache();
			List<Clinic> listClinics=Clinics.GetDeepCopy(false);
			//Duplicate-abbr guard for non-hidden clinics, same as FormClinicEdit validation.
			if(!dto.IsHidden && listClinics.Any(x => !x.IsHidden
				&& x.Abbr.ToLower()==dto.Abbr.Trim().ToLower())) {
				return Results.Text("Abbreviation already exists",statusCode:400);
			}
			Clinic clinic=new Clinic();
			clinic.Description=dto.Description;
			clinic.Abbr=dto.Abbr;
			clinic.Address=dto.Address??"";
			clinic.Address2=dto.Address2??"";
			clinic.City=dto.City??"";
			clinic.State=dto.State??"";
			clinic.Zip=dto.Zip??"";
			clinic.Phone=NormalizePhone(dto.Phone);
			clinic.DefaultProv=dto.DefaultProv;
			clinic.IsHidden=dto.IsHidden;
			//Append at the end, exactly like FormClinics.butAdd_Click.
			clinic.ItemOrder=listClinics.Count==0?0:listClinics.Max(x => x.ItemOrder)+1;
			long clinicNum=Clinics.Insert(clinic);
			Clinics.RefreshCache();
			SecurityLogs.MakeLogEntry(EnumPermType.Setup,0,"Clinic '"+clinic.Abbr+"' created.");
			Signalods.SetInvalid(InvalidType.Providers);
			return Results.Json(new { clinicNum });
		});

		app.MapPut("/api/clinics/{clinicNum:long}",(long clinicNum,EditClinic dto) => {
			if(string.IsNullOrWhiteSpace(dto.Description)) {
				return Results.Text("Description cannot be blank",statusCode:400);
			}
			if(string.IsNullOrWhiteSpace(dto.Abbr)) {
				return Results.Text("Abbreviation cannot be blank",statusCode:400);
			}
			Clinics.RefreshCache();
			Clinic clinic=Clinics.GetFirstOrDefault(x => x.ClinicNum==clinicNum);
			if(clinic==null) {
				return Results.NotFound();
			}
			//Duplicate-abbr guard against the other non-hidden clinics.
			if(!dto.IsHidden && Clinics.GetDeepCopy(false).Any(x => x.ClinicNum!=clinicNum && !x.IsHidden
				&& x.Abbr.ToLower()==dto.Abbr.Trim().ToLower())) {
				return Results.Text("Abbreviation already exists",statusCode:400);
			}
			clinic=clinic.Copy();
			clinic.Description=dto.Description;
			clinic.Abbr=dto.Abbr;
			clinic.Address=dto.Address??"";
			clinic.Address2=dto.Address2??"";
			clinic.City=dto.City??"";
			clinic.State=dto.State??"";
			clinic.Zip=dto.Zip??"";
			clinic.Phone=NormalizePhone(dto.Phone);
			clinic.DefaultProv=dto.DefaultProv;
			clinic.IsHidden=dto.IsHidden;
			Clinics.Update(clinic);
			Clinics.RefreshCache();
			SecurityLogs.MakeLogEntry(EnumPermType.Setup,0,"Clinic '"+clinic.Abbr+"' edited.");
			Signalods.SetInvalid(InvalidType.Providers);
			return Results.Ok();
		});
	}

	//US phone numbers are stored digits-only, mirroring FormClinicEdit's strip of ()- and spaces.
	static string NormalizePhone(string phone) {
		if(string.IsNullOrEmpty(phone)) {
			return "";
		}
		return new string(phone.Where(char.IsDigit).ToArray());
	}
}

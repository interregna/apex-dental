/*
 * Insurance carrier manager: practice-wide carrier list and CRUD.
 * Retires FormCarriers / FormCarrierEdit / FormCarrierCombine. All data flows
 * through the real business layer: Carriers.GetBigList for the searchable grid,
 * Carriers.Insert/Update/Delete (all InsEditLog-audited) for edits,
 * Carriers.DependentPlans as the delete guard, and Carriers.Combine for
 * merging duplicate carriers into one picked survivor.
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class InsCarrierManagerModule {
	record CarrierDto(string CarrierName,string Address,string Address2,string City,string State,string Zip,string Phone,
		string ElectID,string TIN,bool IsHidden,string NoSendElect,bool IsCoinsuranceInverted);
	record CombineDto(List<long> CarrierNums,long PickedCarrierNum);

	public static void Map(WebApplication app) {
		//Searchable carrier grid: the same rows FormCarriers shows, via Carriers.GetBigList.
		//GetBigList only applies the phone/isCanadian/showHidden filters in SQL (the desktop
		//form filters name and elect ID client-side), so name/electId are matched here.
		app.MapGet("/api/carriers",(string? name,string? phone,string? electId,bool showHidden=false,bool canadian=false) => {
			DataTable table=Carriers.GetBigList(canadian,showHidden,name??"",phone??"",electId??"");
			string nameFilter=(name??"").Trim();
			string electIdFilter=(electId??"").Trim();
			List<DataRow> listRows=table.Rows.OfType<DataRow>()
				.Where(x => nameFilter=="" || x["CarrierName"].ToString().Contains(nameFilter,StringComparison.OrdinalIgnoreCase))
				.Where(x => electIdFilter=="" || x["ElectID"].ToString().Contains(electIdFilter,StringComparison.OrdinalIgnoreCase))
				.ToList();
			return Results.Json(listRows.Select(x => new {
				carrierNum=long.Parse(x["CarrierNum"].ToString()),
				carrierName=x["CarrierName"].ToString(),
				address=x["Address"].ToString(),
				address2=x["Address2"].ToString(),
				city=x["City"].ToString(),
				state=x["State"].ToString(),
				zip=x["Zip"].ToString(),
				phone=x["Phone"].ToString(),
				electId=x["ElectID"].ToString(),
				insPlanCount=x["insPlanCount"].ToString(),
				isHidden=x["isHidden"].ToString()=="X",
				isCDA=x["isCDA"].ToString()=="X",
			}));
		});

		//Full single-carrier detail for the edit form (grid rows omit flags like NoSendElect/TIN).
		app.MapGet("/api/carriers/{carrierNum:long}",(long carrierNum) => {
			Carrier carrier=Carriers.GetCarrierDB(carrierNum);
			if(carrier==null) {
				return Results.NotFound();
			}
			return Results.Json(ToDetail(carrier));
		});

		//Names of subscribers whose insplans depend on this carrier; shown as the delete guard.
		app.MapGet("/api/carriers/{carrierNum:long}/dependents",(long carrierNum) => {
			Carrier carrier=Carriers.GetCarrierDB(carrierNum);
			if(carrier==null) {
				return Results.NotFound();
			}
			List<string> listDependents=Carriers.DependentPlans(carrier);
			return Results.Json(listDependents);
		});

		//Create a carrier (FormCarrierEdit "new"); Insert handles Canadian ElectID validation and audit logging.
		app.MapPost("/api/carriers",(CarrierDto dto) => {
			if(string.IsNullOrWhiteSpace(dto.CarrierName)) {
				return Results.Text("Carrier name is required",statusCode:400);
			}
			Carrier carrier=new Carrier();
			ApplyDto(carrier,dto);
			try {
				long carrierNum=Carriers.Insert(carrier);
				return Results.Json(new { carrierNum });
			}
			catch(ApplicationException ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
		});

		//Edit a carrier through the audited two-object Update (also enforces Canadian ElectID rules).
		app.MapPut("/api/carriers/{carrierNum:long}",(long carrierNum,CarrierDto dto) => {
			Carrier carrier=Carriers.GetCarrierDB(carrierNum);
			if(carrier==null) {
				return Results.NotFound();
			}
			if(string.IsNullOrWhiteSpace(dto.CarrierName)) {
				return Results.Text("Carrier name is required",statusCode:400);
			}
			Carrier carrierOld=carrier.Copy();
			ApplyDto(carrier,dto);
			try {
				Carriers.Update(carrier,carrierOld);
				return Results.Json(ToDetail(carrier));
			}
			catch(ApplicationException ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
		});

		//Delete; the business layer throws when insplans or etrans history depend on the carrier.
		app.MapDelete("/api/carriers/{carrierNum:long}",(long carrierNum) => {
			Carrier carrier=Carriers.GetCarrierDB(carrierNum);
			if(carrier==null) {
				return Results.NotFound();
			}
			try {
				Carriers.Delete(carrier);
				return Results.Ok();
			}
			catch(ApplicationException ex) {
				return Results.Text(ex.Message,statusCode:409);
			}
		});

		//Merge duplicates (FormCarrierCombine): moves insplans/etrans/bluebook rows to the
		//picked carrier, deletes the rest, and writes InsEditLog entries for everything.
		app.MapPost("/api/carriers/combine",(CombineDto dto) => {
			if(dto.CarrierNums==null || dto.CarrierNums.Count<2) {
				return Results.Text("Select at least two carriers to combine",statusCode:400);
			}
			if(!dto.CarrierNums.Contains(dto.PickedCarrierNum)) {
				return Results.Text("The carrier to keep must be one of the selected carriers",statusCode:400);
			}
			if(Carriers.GetCarrierDB(dto.PickedCarrierNum)==null) {
				return Results.NotFound();
			}
			try {
				Carriers.Combine(dto.CarrierNums,dto.PickedCarrierNum);
				return Results.Json(new { pickedCarrierNum=dto.PickedCarrierNum,combined=dto.CarrierNums.Count-1 });
			}
			catch(ApplicationException ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
		});
	}

	static void ApplyDto(Carrier carrier,CarrierDto dto) {
		carrier.CarrierName=dto.CarrierName.Trim();
		carrier.Address=dto.Address?.Trim()??"";
		carrier.Address2=dto.Address2?.Trim()??"";
		carrier.City=dto.City?.Trim()??"";
		carrier.State=dto.State?.Trim()??"";
		carrier.Zip=dto.Zip?.Trim()??"";
		carrier.Phone=dto.Phone?.Trim()??"";
		carrier.ElectID=dto.ElectID?.Trim()??"";
		carrier.TIN=dto.TIN?.Trim()??"";
		carrier.IsHidden=dto.IsHidden;
		carrier.NoSendElect=Enum.TryParse(dto.NoSendElect,out NoSendElectType noSendElect)?noSendElect:NoSendElectType.SendElect;
		carrier.IsCoinsuranceInverted=dto.IsCoinsuranceInverted;
		//Fields not surfaced in the web editor keep their defaults (new) or current values (edit):
		//IsCDA/CDAnetVersion/CanadianNetworkNum and the other Canadian EDI settings, group, colors, ERA overrides.
	}

	static object ToDetail(Carrier carrier) {
		return new {
			carrierNum=carrier.CarrierNum,
			carrierName=carrier.CarrierName??"",
			address=carrier.Address??"",
			address2=carrier.Address2??"",
			city=carrier.City??"",
			state=carrier.State??"",
			zip=carrier.Zip??"",
			phone=carrier.Phone??"",
			electId=carrier.ElectID??"",
			tin=carrier.TIN??"",
			isHidden=carrier.IsHidden,
			isCDA=carrier.IsCDA,
			noSendElect=carrier.NoSendElect.ToString(),
			isCoinsuranceInverted=carrier.IsCoinsuranceInverted,
		};
	}
}

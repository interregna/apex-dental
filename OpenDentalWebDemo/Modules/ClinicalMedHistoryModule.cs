/*
 * Clinical medical-history module: allergies, problems, and medications for one
 * patient — the web replacement for the FormMedical hub (FormAllergyEdit,
 * FormDiseaseEdit slice, FormMedPat, FormMedications, FormReconcileProblem).
 * All reads/writes go through the real business layer: Allergies.GetAll/Insert/Update,
 * Diseases.Refresh/Insert/Update, MedicationPats.Refresh/Insert/Update, with pick
 * lists from AllergyDefs.GetAll, DiseaseDefs cache, and the Medications cache.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class ClinicalMedHistoryModule {
	record AllergyDto(long AllergyDefNum,string Reaction,string DateAdverseReaction,bool StatusIsActive);
	record ProblemDto(long DiseaseDefNum,string PatNote,string DateStart,string DateStop,string ProbStatus);
	record MedicationPatDto(long MedicationNum,string MedDescript,string NewMedName,string PatNote,string DateStart,string DateStop,bool Discontinued);

	public static void Map(WebApplication app) {
		//---------- pick lists: allergy defs, problem defs (DiseaseDefs cache), medications cache ----------
		app.MapGet("/api/medhist/defs",() => {
			List<AllergyDef> listAllergyDefs=AllergyDefs.GetAll(false);
			List<DiseaseDef> listDiseaseDefs=DiseaseDefs.GetDeepCopy(true);//short list=non-hidden, cache lazily fills
			List<Medication> listMedications=Medications.GetList();
			return Results.Json(new {
				allergyDefs=listAllergyDefs.OrderBy(x => x.Description).Select(x => new {
					allergyDefNum=x.AllergyDefNum,
					description=x.Description,
				}),
				diseaseDefs=listDiseaseDefs.OrderBy(x => x.ItemOrder).Select(x => new {
					diseaseDefNum=x.DiseaseDefNum,
					diseaseName=x.DiseaseName,
					icd10=x.Icd10Code,
				}),
				medications=listMedications.OrderBy(x => x.MedName).Select(x => new {
					medicationNum=x.MedicationNum,
					medName=x.MedName,
					generic=x.GenericNum==x.MedicationNum?"":Medications.GetGenericName(x.GenericNum),
				}),
			});
		});

		//---------- allergies ----------
		app.MapGet("/api/patients/{patNum:long}/allergies",(long patNum,bool showInactive=false) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<AllergyDef> listAllergyDefs=AllergyDefs.GetAll(true);
			List<Allergy> listAllergies=Allergies.GetAll(patNum,showInactive);
			return Results.Json(listAllergies.OrderBy(x => AllergyDefs.GetOne(x.AllergyDefNum,listAllergyDefs)?.Description??"").Select(x => new {
				allergyNum=x.AllergyNum,
				allergyDefNum=x.AllergyDefNum,
				description=AllergyDefs.GetOne(x.AllergyDefNum,listAllergyDefs)?.Description??"",
				reaction=x.Reaction,
				statusIsActive=x.StatusIsActive,
				dateAdverseReaction=x.DateAdverseReaction.Year>1880?x.DateAdverseReaction.ToString("yyyy-MM-dd"):"",
			}));
		});

		app.MapPost("/api/patients/{patNum:long}/allergies",(long patNum,AllergyDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			if(AllergyDefs.GetOne(dto.AllergyDefNum)==null) {
				return Results.Text("Pick an allergy from the list",statusCode:400);
			}
			Allergy allergy=new Allergy();
			allergy.PatNum=patNum;
			allergy.AllergyDefNum=dto.AllergyDefNum;
			allergy.Reaction=dto.Reaction??"";
			allergy.StatusIsActive=dto.StatusIsActive;
			allergy.DateAdverseReaction=DateTime.TryParse(dto.DateAdverseReaction,out DateTime dateReaction)?dateReaction:DateTime.MinValue;
			long allergyNum=Allergies.Insert(allergy);
			return Results.Json(new { allergyNum });
		});

		app.MapPut("/api/allergies/{allergyNum:long}",(long allergyNum,AllergyDto dto) => {
			Allergy allergy=Allergies.GetOne(allergyNum);
			if(allergy==null) {
				return Results.NotFound();
			}
			if(dto.AllergyDefNum!=0) {
				allergy.AllergyDefNum=dto.AllergyDefNum;
			}
			allergy.Reaction=dto.Reaction??"";
			allergy.StatusIsActive=dto.StatusIsActive;
			allergy.DateAdverseReaction=DateTime.TryParse(dto.DateAdverseReaction,out DateTime dateReaction)?dateReaction:DateTime.MinValue;
			Allergies.Update(allergy);
			return Results.Json(new { allergyNum=allergy.AllergyNum });
		});

		//---------- problems (diseases) ----------
		app.MapGet("/api/patients/{patNum:long}/problems",(long patNum,bool showInactive=false) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<Disease> listDiseases=Diseases.Refresh(patNum,showActiveOnly:!showInactive);
			return Results.Json(listDiseases.OrderBy(x => DiseaseDefs.GetName(x.DiseaseDefNum)).Select(x => new {
				diseaseNum=x.DiseaseNum,
				diseaseDefNum=x.DiseaseDefNum,
				diseaseName=DiseaseDefs.GetName(x.DiseaseDefNum),
				patNote=x.PatNote,
				probStatus=x.ProbStatus.ToString(),
				dateStart=x.DateStart.Year>1880?x.DateStart.ToString("yyyy-MM-dd"):"",
				dateStop=x.DateStop.Year>1880?x.DateStop.ToString("yyyy-MM-dd"):"",
			}));
		});

		app.MapPost("/api/patients/{patNum:long}/problems",(long patNum,ProblemDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			if(DiseaseDefs.GetOne(dto.DiseaseDefNum)==null) {
				return Results.Text("Pick a problem from the list",statusCode:400);
			}
			Disease disease=new Disease();
			disease.PatNum=patNum;
			disease.DiseaseDefNum=dto.DiseaseDefNum;
			disease.PatNote=dto.PatNote??"";
			disease.ProbStatus=Enum.TryParse(dto.ProbStatus,out ProblemStatus probStatus)?probStatus:ProblemStatus.Active;
			disease.DateStart=DateTime.TryParse(dto.DateStart,out DateTime dateStart)?dateStart:DateTime.MinValue;
			disease.DateStop=DateTime.TryParse(dto.DateStop,out DateTime dateStop)?dateStop:DateTime.MinValue;
			long diseaseNum=Diseases.Insert(disease);
			return Results.Json(new { diseaseNum });
		});

		app.MapPut("/api/problems/{diseaseNum:long}",(long diseaseNum,ProblemDto dto) => {
			Disease disease=Diseases.GetOne(diseaseNum);
			if(disease==null) {
				return Results.NotFound();
			}
			if(dto.DiseaseDefNum!=0) {
				disease.DiseaseDefNum=dto.DiseaseDefNum;
			}
			disease.PatNote=dto.PatNote??"";
			if(Enum.TryParse(dto.ProbStatus,out ProblemStatus probStatus)) {
				disease.ProbStatus=probStatus;
			}
			disease.DateStart=DateTime.TryParse(dto.DateStart,out DateTime dateStart)?dateStart:DateTime.MinValue;
			disease.DateStop=DateTime.TryParse(dto.DateStop,out DateTime dateStop)?dateStop:DateTime.MinValue;
			//Mirror FormDiseaseEdit: resolving/inactivating without a stop date stamps today.
			if(disease.ProbStatus!=ProblemStatus.Active && disease.DateStop.Year<1880) {
				disease.DateStop=DateTime.Today;
			}
			Diseases.Update(disease);
			return Results.Json(new { diseaseNum=disease.DiseaseNum });
		});

		//---------- medications (patient orders) ----------
		app.MapGet("/api/patients/{patNum:long}/medications",(long patNum,bool includeDiscontinued=false) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<MedicationPat> listMedicationPats=MedicationPats.Refresh(patNum,includeDiscontinued);
			return Results.Json(listMedicationPats.OrderByDescending(x => MedicationPats.IsMedActive(x)).ThenBy(x => GetMedName(x)).Select(x => new {
				medicationPatNum=x.MedicationPatNum,
				medicationNum=x.MedicationNum,
				medName=GetMedName(x),
				patNote=x.PatNote,
				isActive=MedicationPats.IsMedActive(x),
				dateStart=x.DateStart.Year>1880?x.DateStart.ToString("yyyy-MM-dd"):"",
				dateStop=x.DateStop.Year>1880?x.DateStop.ToString("yyyy-MM-dd"):"",
			}));
		});

		app.MapPost("/api/patients/{patNum:long}/medications",(long patNum,MedicationPatDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			Medication medication=dto.MedicationNum==0?null:Medications.GetMedication(dto.MedicationNum);
			if(medication==null && !string.IsNullOrWhiteSpace(dto.NewMedName)) {
				//Patfam-medical "new medication" flow: create the Medication first, the way
				//FormMedicationEdit does for a new generic (GenericNum points at itself).
				medication=new Medication();
				medication.MedName=dto.NewMedName.Trim();
				Medications.Insert(medication);
				medication.GenericNum=medication.MedicationNum;
				Medications.Update(medication);
				Cache.Refresh(InvalidType.Medications);
			}
			if(medication==null && string.IsNullOrWhiteSpace(dto.MedDescript)) {
				return Results.Text("Pick a medication or type a description",statusCode:400);
			}
			MedicationPat medicationPat=new MedicationPat();
			medicationPat.PatNum=patNum;
			medicationPat.MedicationNum=medication?.MedicationNum??0;
			medicationPat.MedDescript=medication==null?dto.MedDescript.Trim():"";//same rule as FormMedPat: descript only for free-text meds
			medicationPat.RxCui=medication?.RxCui??0;
			medicationPat.PatNote=dto.PatNote??"";
			medicationPat.DateStart=DateTime.TryParse(dto.DateStart,out DateTime dateStart)?dateStart:DateTime.MinValue;
			medicationPat.DateStop=DateTime.TryParse(dto.DateStop,out DateTime dateStop)?dateStop:DateTime.MinValue;
			medicationPat.ProvNum=pat.PriProv;
			long medicationPatNum=MedicationPats.Insert(medicationPat);
			return Results.Json(new { medicationPatNum });
		});

		app.MapPut("/api/medications/{medicationPatNum:long}",(long medicationPatNum,MedicationPatDto dto) => {
			MedicationPat medicationPat=MedicationPats.GetOne(medicationPatNum);
			if(medicationPat==null) {
				return Results.NotFound();
			}
			medicationPat.PatNote=dto.PatNote??"";
			medicationPat.DateStart=DateTime.TryParse(dto.DateStart,out DateTime dateStart)?dateStart:medicationPat.DateStart;
			medicationPat.DateStop=DateTime.TryParse(dto.DateStop,out DateTime dateStop)?dateStop:DateTime.MinValue;
			//Discontinue toggle: IsMedActive is DateStop unset or >= today, so stamp yesterday to stop, clear to resume.
			if(dto.Discontinued && medicationPat.DateStop.Year<1880) {
				medicationPat.DateStop=DateTime.Today.AddDays(-1);
			}
			if(!dto.Discontinued) {
				medicationPat.DateStop=DateTime.MinValue;
			}
			MedicationPats.Update(medicationPat);
			return Results.Json(new { medicationPatNum=medicationPat.MedicationPatNum });
		});
	}

	static string GetMedName(MedicationPat medicationPat) {
		if(medicationPat.MedicationNum==0) {
			return medicationPat.MedDescript??"";
		}
		return Medications.GetMedication(medicationPat.MedicationNum)?.MedName??medicationPat.MedDescript??"";
	}
}

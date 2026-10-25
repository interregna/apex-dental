/*
 * Periodontal exam ENTRY (the data side of FormPerio; display already exists via the
 * ported ToothChartHelper at /api/patients/{patNum}/toothchart.png?perio=true).
 * An exam is a PerioExam row; every measurement row is a PerioMeasure keyed by
 * exam + IntTooth + SequenceType (Probing, GingMargin, MGJ, Furcation,
 * BleedSupPlaqCalc, Mobility) carrying six per-site values MB/B/DB + ML/L/DL
 * (-1 = no value) or a whole-tooth ToothValue (Mobility). SkipTooth rows mark
 * skipped teeth and are managed through PerioMeasures.SetSkipped/GetSkipped.
 * Writes use the same business calls FormPerio makes: PerioExams.CreateNewExam
 * (seeds default skipped teeth), PerioMeasures.Insert/Update/Delete,
 * PerioExams.Update/Delete, each paired with a SecurityLogs PerioEdit entry.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class ClinicalPerioEntryModule {
	record MeasureDto(int IntTooth,string SequenceType,int Mb,int B,int Db,int Ml,int L,int Dl,int ToothValue);
	record MeasuresBatchDto(List<MeasureDto> Measures);
	record NewExamDto(bool CopyForward,string Note);
	record ExamEditDto(string Note);
	record SkippedDto(List<int> Teeth);

	//Sequences the entry grid edits. SkipTooth is managed via /skipped; CAL is never
	//stored (the desktop computes it on the fly from Probing + GingMargin).
	static readonly PerioSequenceType[] _editableSequences=new[] {
		PerioSequenceType.Probing,PerioSequenceType.GingMargin,PerioSequenceType.MGJ,
		PerioSequenceType.Furcation,PerioSequenceType.BleedSupPlaqCalc,PerioSequenceType.Mobility,
	};

	public static void Map(WebApplication app) {
		//Exam list for the patient plus one exam's measures (examNum query param selects;
		//defaults to the most recent). Skipped teeth come back as plain tooth numbers.
		app.MapGet("/api/patients/{patNum:long}/perio",(long patNum,long examNum=0) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<PerioExam> listExams=PerioExams.Refresh(patNum);//ordered by ExamDate
			PerioExam examSel=examNum>0
				?listExams.FirstOrDefault(x => x.PerioExamNum==examNum)
				:listExams.LastOrDefault();
			List<PerioMeasure> listMeasures=examSel==null
				?new List<PerioMeasure>()
				:PerioMeasures.GetAllForExam(examSel.PerioExamNum);
			return Results.Json(new {
				exams=listExams.Select(x => new {
					perioExamNum=x.PerioExamNum,
					examDate=x.ExamDate.ToString("yyyy-MM-dd"),
					provAbbr=Providers.GetAbbr(x.ProvNum),
					note=x.Note??"",
				}),
				selectedExamNum=examSel?.PerioExamNum??0,
				note=examSel?.Note??"",
				skipped=examSel==null?new List<int>():PerioMeasures.GetSkipped(examSel.PerioExamNum),
				measures=listMeasures.Where(x => x.SequenceType!=PerioSequenceType.SkipTooth).Select(x => new {
					intTooth=x.IntTooth,
					sequenceType=x.SequenceType.ToString(),
					mb=x.MBvalue,b=x.Bvalue,db=x.DBvalue,
					ml=x.MLvalue,l=x.Lvalue,dl=x.DLvalue,
					toothValue=x.ToothValue,
				}),
			});
		});

		//Create a new exam. PerioExams.CreateNewExam handles the default skip set (previous
		//exam's skips, or missing teeth on a first exam). copyForward additionally mirrors
		//the desktop "Copy Previous" button: every measure from the latest prior exam is
		//re-inserted under the new exam (SkipTooth rows excluded — CreateNewExam did those).
		app.MapPost("/api/patients/{patNum:long}/perio",(long patNum,NewExamDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<PerioExam> listExamsBefore=PerioExams.Refresh(patNum);
			PerioExam examNew=PerioExams.CreateNewExam(pat);
			if(!string.IsNullOrEmpty(dto?.Note)) {
				examNew.Note=dto.Note;
				PerioExams.Update(examNew);
			}
			if(dto!=null && dto.CopyForward && listExamsBefore.Count>0) {
				List<PerioMeasure> listPrev=PerioMeasures.GetAllForExam(listExamsBefore.Last().PerioExamNum)
					.Where(x => x.SequenceType!=PerioSequenceType.SkipTooth).ToList();
				foreach(PerioMeasure measure in listPrev) {
					measure.PerioMeasureNum=0;
					measure.PerioExamNum=examNew.PerioExamNum;
					PerioMeasures.Insert(measure);
				}
				SecurityLogs.MakeLogEntry(EnumPermType.PerioEdit,patNum,"Perio exam copied.");
			}
			else {
				SecurityLogs.MakeLogEntry(EnumPermType.PerioEdit,patNum,"Perio exam created");
			}
			return Results.Json(new { perioExamNum=examNew.PerioExamNum });
		});

		//Batch upsert of measures for one exam. Each item addresses tooth + sequence; a row
		//whose values are all -1 deletes the stored measure (the desktop clears cells the
		//same way). Bumps PerioExam.DateTMeasureEdit like ContrPerio.SaveCurExam does.
		app.MapPut("/api/perio/{perioExamNum:long}/measures",(long perioExamNum,MeasuresBatchDto dto) => {
			PerioExam exam=PerioExams.GetOnePerioExam(perioExamNum);
			if(exam==null) {
				return Results.NotFound();
			}
			if(dto?.Measures==null || dto.Measures.Count==0) {
				return Results.Text("No measures supplied.",statusCode:400);
			}
			List<PerioMeasure> listExisting=PerioMeasures.GetAllForExam(perioExamNum);
			int inserted=0,updated=0,deleted=0;
			foreach(MeasureDto m in dto.Measures) {
				if(!Enum.TryParse(m.SequenceType,ignoreCase:true,out PerioSequenceType seq)
					|| !_editableSequences.Contains(seq)) {
					return Results.Text($"Unsupported sequence type: {m.SequenceType}",statusCode:400);
				}
				if(m.IntTooth<1 || m.IntTooth>32) {
					return Results.Text($"Invalid tooth number: {m.IntTooth}",statusCode:400);
				}
				bool isEmpty=m.Mb==-1 && m.B==-1 && m.Db==-1 && m.Ml==-1 && m.L==-1 && m.Dl==-1 && m.ToothValue==-1;
				PerioMeasure existing=listExisting.FirstOrDefault(x => x.IntTooth==m.IntTooth && x.SequenceType==seq);
				if(existing==null) {
					if(isEmpty) {
						continue;//nothing stored, nothing sent — no-op
					}
					PerioMeasures.Insert(new PerioMeasure() {
						PerioExamNum=perioExamNum,IntTooth=m.IntTooth,SequenceType=seq,
						MBvalue=m.Mb,Bvalue=m.B,DBvalue=m.Db,
						MLvalue=m.Ml,Lvalue=m.L,DLvalue=m.Dl,ToothValue=m.ToothValue,
					});
					inserted++;
				}
				else if(isEmpty) {
					PerioMeasures.Delete(existing);
					deleted++;
				}
				else {
					existing.MBvalue=m.Mb;existing.Bvalue=m.B;existing.DBvalue=m.Db;
					existing.MLvalue=m.Ml;existing.Lvalue=m.L;existing.DLvalue=m.Dl;
					existing.ToothValue=m.ToothValue;
					PerioMeasures.Update(existing);
					updated++;
				}
			}
			exam.DateTMeasureEdit=MiscData.GetNowDateTime();
			PerioExams.Update(exam);
			SecurityLogs.MakeLogEntry(EnumPermType.PerioEdit,exam.PatNum,"Perio measurements edited.");
			return Results.Json(new { inserted,updated,deleted });
		});

		//Replace the exam's skipped-teeth set (PerioMeasures.SetSkipped clears and rewrites
		//the SkipTooth rows, exactly what the desktop skip-tooth toggle persists).
		app.MapPut("/api/perio/{perioExamNum:long}/skipped",(long perioExamNum,SkippedDto dto) => {
			PerioExam exam=PerioExams.GetOnePerioExam(perioExamNum);
			if(exam==null) {
				return Results.NotFound();
			}
			List<int> listTeeth=(dto?.Teeth??new List<int>()).Where(x => x>=1 && x<=32).Distinct().ToList();
			PerioMeasures.SetSkipped(perioExamNum,listTeeth);
			SecurityLogs.MakeLogEntry(EnumPermType.PerioEdit,exam.PatNum,"Perio skipped teeth edited.");
			return Results.Ok();
		});

		//Exam note edit (the note box at the bottom of FormPerio).
		app.MapPut("/api/perio/{perioExamNum:long}",(long perioExamNum,ExamEditDto dto) => {
			PerioExam exam=PerioExams.GetOnePerioExam(perioExamNum);
			if(exam==null) {
				return Results.NotFound();
			}
			exam.Note=dto?.Note??"";
			PerioExams.Update(exam);
			SecurityLogs.MakeLogEntry(EnumPermType.PerioEdit,exam.PatNum,"Perio exam note edited.");
			return Results.Ok();
		});

		//Delete an exam and all its measures (PerioExams.Delete removes both tables).
		app.MapDelete("/api/perio/{perioExamNum:long}",(long perioExamNum) => {
			PerioExam exam=PerioExams.GetOnePerioExam(perioExamNum);
			if(exam==null) {
				return Results.NotFound();
			}
			PerioExams.Delete(exam);
			SecurityLogs.MakeLogEntry(EnumPermType.PerioEdit,exam.PatNum,"Perio exam deleted");
			return Results.Ok();
		});
	}
}

/*
 * Blockout CRUD on the schedule grid (FormScheduleBlockEdit / FormBlockoutCutCopyPaste).
 * Blockouts are Schedule rows with SchedType=Blockout, colored/named by the practice's
 * DefCat.BlockoutTypes definitions and attached to operatories via ScheduleOps.
 * Reads ride Schedules.RefreshPeriodBlockouts; writes go through the same business
 * calls the desktop dialog makes: Schedules.Overlaps guard, Schedules.Insert(validate:true),
 * Schedules.Update, Schedules.Delete, Schedules.ClearBlockoutsForDay/ClearBlockoutsForOp,
 * each paired with Schedules.BlockoutLogHelper for the security log.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SchedBlockoutsModule {
	record BlockoutDto(string Date,string StartTime,string StopTime,long BlockoutType,string Note,List<long> Ops,long ClinicNum);
	record ClearDto(string Date,long OpNum);

	static string Hex(System.Drawing.Color c) => (c.ToArgb()==0 || (c.R==0 && c.G==0 && c.B==0))?"":$"#{c.R:X2}{c.G:X2}{c.B:X2}";

	public static void Map(WebApplication app) {
		//Day snapshot: blockouts for every visible operatory, plus the operatory columns
		//and the practice's blockout-type definitions (name + color) in one payload.
		app.MapGet("/api/blockouts",(DateTime? date) => {
			DateTime day=(date??DateTime.Today).Date;
			Operatories.RefreshCache();//operatory cache is not part of the startup Cache.Refresh set
			List<Operatory> listOps=Operatories.GetDeepCopy(true);//isShort=true excludes hidden
			List<Def> listDefs=Defs.GetDefsForCategory(DefCat.BlockoutTypes,true);
			List<Schedule> listBlockouts=Schedules.RefreshPeriodBlockouts(day,day,listOps.Select(x => x.OperatoryNum).ToList());
			return Results.Json(new {
				date=day.ToString("yyyy-MM-dd"),
				ops=listOps.Select(x => new {
					opNum=x.OperatoryNum,
					name=string.IsNullOrEmpty(x.Abbrev)?x.OpName:x.Abbrev,
				}),
				types=listDefs.Select(x => new {
					defNum=x.DefNum,
					name=x.ItemName,
					color=Hex(x.ItemColor),
					isAppointmentBlocking=Schedules.IsAppointmentBlocking(x.DefNum),
				}),
				blockouts=listBlockouts.OrderBy(x => x.StartTime).Select(x => {
					Def def=listDefs.FirstOrDefault(y => y.DefNum==x.BlockoutType);
					return new {
						scheduleNum=x.ScheduleNum,
						startTime=x.StartTime.ToString(@"hh\:mm"),
						stopTime=x.StopTime.ToString(@"hh\:mm"),
						startMinutes=(int)x.StartTime.TotalMinutes,
						stopMinutes=(int)x.StopTime.TotalMinutes,
						blockoutType=x.BlockoutType,
						typeName=def?.ItemName??Defs.GetName(DefCat.BlockoutTypes,x.BlockoutType),
						color=def==null?"":Hex(def.ItemColor),
						note=x.Note,
						ops=x.Ops,
					};
				}),
			});
		});

		//Create a blockout, mirroring FormScheduleBlockEdit's save: ops required,
		//Overlaps guard (blockouts may not overlap), Insert(validate:true), security log.
		app.MapPost("/api/blockouts",(BlockoutDto dto) => {
			if(!DateTime.TryParse(dto.Date,out DateTime day)) {
				return Results.Text("Invalid date",statusCode:400);
			}
			if(dto.Ops==null || dto.Ops.Count==0) {
				return Results.Text("Please select at least one operatory first.",statusCode:400);
			}
			if(!TimeSpan.TryParse(dto.StartTime,out TimeSpan timeStart) || !TimeSpan.TryParse(dto.StopTime,out TimeSpan timeStop)) {
				return Results.Text("Incorrect time format",statusCode:400);
			}
			Schedule schedule=new Schedule();
			schedule.SchedDate=day.Date;
			schedule.StartTime=timeStart;
			schedule.StopTime=timeStop;
			schedule.SchedType=ScheduleType.Blockout;
			schedule.Status=SchedStatus.Open;
			schedule.BlockoutType=dto.BlockoutType;
			schedule.Note=dto.Note??"";
			schedule.Ops=dto.Ops;
			schedule.ClinicNum=dto.ClinicNum;
			if(Schedules.Overlaps(schedule)) {
				return Results.Text("Blockouts not allowed to overlap.",statusCode:409);
			}
			try {
				Schedules.Insert(schedule,validate:true);
			}
			catch(Exception ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
			Schedules.BlockoutLogHelper(BlockoutAction.Create,schedule);
			return Results.Json(new { scheduleNum=schedule.ScheduleNum });
		});

		//Edit a blockout. Like the desktop dialog, the date is fixed — dto.Date locates
		//the existing row (there is no fetch-by-ScheduleNum business method; GetDayList is
		//the same day-scoped read the desktop uses). Times/type/note/ops are editable.
		app.MapPut("/api/blockouts/{scheduleNum:long}",(long scheduleNum,BlockoutDto dto) => {
			if(!DateTime.TryParse(dto.Date,out DateTime day)) {
				return Results.Text("Invalid date",statusCode:400);
			}
			Schedule schedule=Schedules.GetDayList(day.Date).FirstOrDefault(x => x.ScheduleNum==scheduleNum && x.SchedType==ScheduleType.Blockout);
			if(schedule==null) {
				return Results.NotFound();
			}
			if(dto.Ops==null || dto.Ops.Count==0) {
				return Results.Text("Please select at least one operatory first.",statusCode:400);
			}
			if(!TimeSpan.TryParse(dto.StartTime,out TimeSpan timeStart) || !TimeSpan.TryParse(dto.StopTime,out TimeSpan timeStop)) {
				return Results.Text("Incorrect time format",statusCode:400);
			}
			schedule.StartTime=timeStart;
			schedule.StopTime=timeStop;
			schedule.BlockoutType=dto.BlockoutType;
			schedule.Note=dto.Note??"";
			schedule.Ops=dto.Ops;
			if(Schedules.Overlaps(schedule)) {//excludes this ScheduleNum itself
				return Results.Text("Blockouts not allowed to overlap.",statusCode:409);
			}
			try {
				Schedules.Update(schedule);//validates times, rewrites scheduleops, fires the invalid-sched signal
			}
			catch(Exception ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
			Schedules.BlockoutLogHelper(BlockoutAction.Edit,schedule);
			return Results.Ok();
		});

		//Delete one blockout (the dialog's Delete button): Schedules.Delete + security log.
		app.MapDelete("/api/blockouts/{scheduleNum:long}",(long scheduleNum,DateTime? date) => {
			DateTime day=(date??DateTime.Today).Date;
			Schedule schedule=Schedules.GetDayList(day).FirstOrDefault(x => x.ScheduleNum==scheduleNum && x.SchedType==ScheduleType.Blockout);
			if(schedule==null) {
				return Results.NotFound();
			}
			Schedules.Delete(schedule,hasSignal:true);
			Schedules.BlockoutLogHelper(BlockoutAction.Delete,schedule);
			return Results.Ok();
		});

		//Bulk clear (the clear half of FormBlockoutCutCopyPaste): opNum=0 clears the
		//whole day, otherwise just that operatory's blockouts for the day.
		app.MapPost("/api/blockouts/clear",(ClearDto dto) => {
			if(!DateTime.TryParse(dto.Date,out DateTime day)) {
				return Results.Text("Invalid date",statusCode:400);
			}
			if(dto.OpNum>0) {
				Schedules.ClearBlockoutsForOp(dto.OpNum,day.Date);
				Schedules.BlockoutLogHelper(BlockoutAction.Clear,dateTime:day.Date,opNum:dto.OpNum);
			}
			else {
				Schedules.ClearBlockoutsForDay(day.Date);
				Schedules.BlockoutLogHelper(BlockoutAction.Clear,dateTime:day.Date);
			}
			return Results.Ok();
		});
	}
}

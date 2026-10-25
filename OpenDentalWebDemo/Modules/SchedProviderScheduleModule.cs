/*
 * Provider working-hours editor — web replacement for FormSchedule's day editing
 * (FormScheduleDayEdit + FormScheduleEdit, the Provider half). Provider schedules are
 * Schedule rows with SchedType=Provider and Status Open/Closed/Holiday, attached to
 * operatories through ScheduleOps (Schedule.Ops). Reads ride Schedules.GetDayList (which
 * fills each row's Ops from scheduleop, same day-scoped read the desktop day editor uses).
 * Writes go through the same business calls the desktop dialog makes: Schedules.Insert
 * (validate:true), Schedules.Update(new,old,validate), Schedules.Delete — each paired with
 * the FormScheduleDayEdit security-log text (EnumPermType.Schedules) and an explicit
 * Signalods.SetInvalid(InvalidType.Schedules). Overlap detection mirrors FormScheduleEdit's
 * pre-emptive check (Schedules.GetOverlappingSchedProvNums): a soft warning the desktop lets
 * you override — surfaced here as 409 unless the caller passes force=true.
 *
 * Seeding provider schedules here is what gives the read-only Slot Search (/api/slotsearch)
 * real Open provider hours to compute against.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SchedProviderScheduleModule {
	record ProviderScheduleDto(string Date,long ProvNum,string StartTime,string StopTime,int Status,List<long> Ops,bool Force);

	static object SchedRow(Schedule schedule) => new {
		scheduleNum=schedule.ScheduleNum,
		provNum=schedule.ProvNum,
		provAbbr=schedule.ProvNum==0?"":Providers.GetAbbr(schedule.ProvNum),
		startTime=schedule.StartTime.ToString(@"hh\:mm"),
		stopTime=schedule.StopTime.ToString(@"hh\:mm"),
		status=(int)schedule.Status,
		statusName=schedule.Status.ToString(),
		ops=schedule.Ops,
	};

	public static void Map(WebApplication app) {
		//Day snapshot of one day's provider schedule rows (optionally one provider), plus the
		//operatory columns available for assignment. GetDayList fills each row's Ops.
		app.MapGet("/api/provider-schedules",(DateTime? date,long? provNum) => {
			DateTime day=(date??DateTime.Today).Date;
			Operatories.RefreshCache();//operatory cache is not part of the startup Cache.Refresh set
			List<Operatory> listOps=Operatories.GetDeepCopy(true);//isShort=true excludes hidden
			List<Schedule> listScheds=Schedules.GetDayList(day)
				.FindAll(x => x.SchedType==ScheduleType.Provider && (provNum==null || provNum.Value<=0 || x.ProvNum==provNum.Value));
			return Results.Json(new {
				date=day.ToString("yyyy-MM-dd"),
				provNum=provNum??0,
				ops=listOps.Select(x => new {
					opNum=x.OperatoryNum,
					name=string.IsNullOrEmpty(x.Abbrev)?x.OpName:x.Abbrev,
				}),
				schedules=listScheds
					.OrderBy(x => x.ProvNum==0?"":Providers.GetAbbr(x.ProvNum))
					.ThenBy(x => x.StartTime)
					.Select(SchedRow),
			});
		});

		//Create a provider working-hours block (FormScheduleEdit's save for a Provider row):
		//Insert(validate:true) plus ScheduleOps, with the pre-emptive overlap warning.
		app.MapPost("/api/provider-schedules",(ProviderScheduleDto dto) => {
			if(!DateTime.TryParse(dto.Date,out DateTime day)) {
				return Results.Text("Invalid date",statusCode:400);
			}
			if(dto.ProvNum<=0) {
				return Results.Text("Please select a provider first.",statusCode:400);
			}
			if(!TimeSpan.TryParse(dto.StartTime,out TimeSpan timeStart) || !TimeSpan.TryParse(dto.StopTime,out TimeSpan timeStop)) {
				return Results.Text("Incorrect time format",statusCode:400);
			}
			if(!Enum.IsDefined(typeof(SchedStatus),dto.Status)) {
				return Results.Text("Invalid status",statusCode:400);
			}
			Schedule schedule=new Schedule();
			schedule.SchedDate=day.Date;
			schedule.StartTime=timeStart;
			schedule.StopTime=timeStop;
			schedule.SchedType=ScheduleType.Provider;
			schedule.Status=(SchedStatus)dto.Status;
			schedule.ProvNum=dto.ProvNum;
			schedule.Ops=dto.Ops??new List<long>();
			schedule.IsNew=true;
			//Pre-emptive overlap check against this provider's existing schedules for the day —
			//the desktop's "Overlapping provider schedules detected, continue anyway?" prompt.
			if(!dto.Force) {
				List<Schedule> listSchedulesProvOnly=Schedules.GetDayList(day.Date).FindAll(x => x.SchedType==ScheduleType.Provider);
				List<long> listProvNumsOverlap=Schedules.GetOverlappingSchedProvNums(new List<long>{ dto.ProvNum },schedule,listSchedulesProvOnly,schedule.Ops);
				if(listProvNumsOverlap.Count>0) {
					return Results.Text("Overlapping provider schedules detected. Save again to continue anyway.",statusCode:409);
				}
			}
			try {
				Schedules.Insert(schedule,validate:true);//validates times, inserts scheduleops, fires invalid-sched signal
			}
			catch(Exception ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
			SecurityLogs.MakeLogEntry(EnumPermType.Schedules,0,"Schedule Added for "+Providers.GetFormalName(dto.ProvNum)+" on "+day.ToShortDateString());
			Signalods.SetInvalid(InvalidType.Schedules);
			return Results.Json(new { scheduleNum=schedule.ScheduleNum });
		});

		//Edit times/status/ops of one provider block. Like the desktop day editor, the date
		//locates the row (GetDayList is the same day-scoped read); ScheduleNum + SchedType pin it.
		app.MapPut("/api/provider-schedules/{scheduleNum:long}",(long scheduleNum,ProviderScheduleDto dto) => {
			if(!DateTime.TryParse(dto.Date,out DateTime day)) {
				return Results.Text("Invalid date",statusCode:400);
			}
			Schedule scheduleOld=Schedules.GetDayList(day.Date).FirstOrDefault(x => x.ScheduleNum==scheduleNum && x.SchedType==ScheduleType.Provider);
			if(scheduleOld==null) {
				return Results.NotFound();
			}
			if(!TimeSpan.TryParse(dto.StartTime,out TimeSpan timeStart) || !TimeSpan.TryParse(dto.StopTime,out TimeSpan timeStop)) {
				return Results.Text("Incorrect time format",statusCode:400);
			}
			if(!Enum.IsDefined(typeof(SchedStatus),dto.Status)) {
				return Results.Text("Invalid status",statusCode:400);
			}
			Schedule scheduleNew=scheduleOld.Copy();
			scheduleNew.StartTime=timeStart;
			scheduleNew.StopTime=timeStop;
			scheduleNew.Status=(SchedStatus)dto.Status;
			scheduleNew.Ops=dto.Ops??new List<long>();
			scheduleNew.IsNew=false;
			//Overlap check against the provider's OTHER schedule rows for the day (exclude self).
			if(!dto.Force) {
				List<Schedule> listSchedulesProvOnly=Schedules.GetDayList(day.Date)
					.FindAll(x => x.SchedType==ScheduleType.Provider && x.ScheduleNum!=scheduleNum);
				List<long> listProvNumsOverlap=Schedules.GetOverlappingSchedProvNums(new List<long>{ scheduleNew.ProvNum },scheduleNew,listSchedulesProvOnly,scheduleNew.Ops);
				if(listProvNumsOverlap.Count>0) {
					return Results.Text("Overlapping provider schedules detected. Save again to continue anyway.",statusCode:409);
				}
			}
			try {
				Schedules.Update(scheduleNew,scheduleOld,validate:true);//validates times, rewrites scheduleops, fires invalid-sched signal
			}
			catch(Exception ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
			SecurityLogs.MakeLogEntry(EnumPermType.Schedules,0,"Schedule edited for "+Providers.GetFormalName(scheduleNew.ProvNum)+" on "+day.ToShortDateString());
			Signalods.SetInvalid(InvalidType.Schedules);
			return Results.Ok();
		});

		//Delete one provider block (the day editor's Delete): Schedules.Delete + security log.
		app.MapDelete("/api/provider-schedules/{scheduleNum:long}",(long scheduleNum,DateTime? date) => {
			DateTime day=(date??DateTime.Today).Date;
			Schedule schedule=Schedules.GetDayList(day).FirstOrDefault(x => x.ScheduleNum==scheduleNum && x.SchedType==ScheduleType.Provider);
			if(schedule==null) {
				return Results.NotFound();
			}
			Schedules.Delete(schedule,hasSignal:true);
			SecurityLogs.MakeLogEntry(EnumPermType.Schedules,0,"Schedule Removed for "+Providers.GetFormalName(schedule.ProvNum)+" on "+day.ToShortDateString());
			Signalods.SetInvalid(InvalidType.Schedules);
			return Results.Ok();
		});
	}
}

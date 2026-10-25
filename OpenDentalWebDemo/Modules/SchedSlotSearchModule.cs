/*
 * Open-slot search — web replacement for FormApptSearch ("Search for Appointment").
 *
 * Path chosen: slots are COMPUTED here rather than through ApptSearch.GetSearchResults.
 * That method is headless, but it is built around a persisted pinboard appointment:
 * ApptSearch.GetDataForSearch loads Appointments.GetOneApt(aptNum) and returns nothing
 * without a real appointment row, the slot length comes from that appointment's 5-minute
 * time Pattern, and it intentionally yields only the FIRST opening per day (the desktop
 * grid shows one row per day).  ApptSearch.GetApiSlots is minutes-based and headless but
 * is single-provider and explicitly ignores blockouts.  This endpoint takes a length in
 * minutes with no appointment row and returns many candidate slots, so the 5-minute-grid
 * computation is done here — on the SAME data reads the desktop search performs (see
 * ApptSearch.GetDataForSearch): Schedules.GetSchedulesForAppointmentSearch (provider
 * schedules plus only the appointment-blocking blockouts, ScheduleOps filled into
 * Schedule.Ops) and Appointments.GetForPeriodList (scheduled appointments in the searched
 * operatories, excluding UnschedList/Broken/Planned).  Schedules.IsAppointmentBlocking
 * double-checks the blockout filter, same as SchedBlockoutsModule.
 *
 * Semantics mirrored from ApptSearch: 288 five-minute blocks per day; a slot is offered
 * only where a SchedStatus.Open provider schedule covers the whole length, the operatory
 * has no appointment and no appointment-blocking blockout in any covered block, and the
 * provider (ProvNum or ProvHyg) has no appointment in ANY operatory during the slot.
 * afterTime/beforeTime restrict the slot START time, matching the desktop's
 * AddTimeToSearchResultsHelper.  Dynamic provider schedules (no scheduleops) fall back to
 * the operatories assigned to the provider, then to unassigned operatories, mirroring
 * ApptSearch.GetAllForDate's dictProvNumsInOpsByOp logic.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SchedSlotSearchModule {
	const int BlocksPerDay=288;//5-minute increments, same granularity as ApptSearch
	const int MaxResults=50;

	static void MarkBusy(bool[] arrayBusy,int idxStart,int idxStop) {
		for(int i=Math.Max(0,idxStart);i<Math.Min(arrayBusy.Length,idxStop);i++) {
			arrayBusy[i]=true;
		}
	}

	///<summary>Operatories a dynamic schedule (no scheduleops) can offer: ops assigned to
	///the provider, else unassigned ops — mirrors ApptSearch.GetAllForDate.</summary>
	static List<long> GetDynamicOpNums(List<Operatory> listOps,long provNum) {
		List<long> listAssigned=listOps.FindAll(x => x.ProvDentist==provNum || x.ProvHygienist==provNum)
			.Select(x => x.OperatoryNum).ToList();
		if(listAssigned.Count>0) {
			return listAssigned;
		}
		return listOps.FindAll(x => x.ProvDentist==0 && x.ProvHygienist==0).Select(x => x.OperatoryNum).ToList();
	}

	public static void Map(WebApplication app) {
		//GET /api/slotsearch?providers=1,2&minutes=60&from=YYYY-MM-DD&days=14&afterTime=HH:mm&beforeTime=HH:mm
		//providers optional (default all non-hidden); from defaults to tomorrow, like the
		//desktop search which always starts the day after its "after" date.
		app.MapGet("/api/slotsearch",(string? providers,int? minutes,string? from,int? days,string? afterTime,string? beforeTime) => {
			int lengthMinutes=minutes??60;
			if(lengthMinutes<5 || lengthMinutes>480) {
				return Results.Text("minutes must be between 5 and 480",statusCode:400);
			}
			int windowDays=Math.Clamp(days??14,1,60);
			DateTime dateStart=DateTime.Today.AddDays(1);
			if(!string.IsNullOrEmpty(from)) {
				if(!DateTime.TryParse(from,out dateStart)) {
					return Results.Text("Invalid from date",statusCode:400);
				}
			}
			dateStart=dateStart.Date;
			DateTime dateEnd=dateStart.AddDays(windowDays-1);
			TimeSpan timeAfter=TimeSpan.Zero;
			if(!string.IsNullOrEmpty(afterTime) && !TimeSpan.TryParse(afterTime,out timeAfter)) {
				return Results.Text("Incorrect afterTime format",statusCode:400);
			}
			TimeSpan timeBefore=TimeSpan.FromHours(24);
			if(!string.IsNullOrEmpty(beforeTime) && !TimeSpan.TryParse(beforeTime,out timeBefore)) {
				return Results.Text("Incorrect beforeTime format",statusCode:400);
			}
			if(timeBefore<=timeAfter) {
				return Results.Text("beforeTime must be later than afterTime",statusCode:400);
			}
			Operatories.RefreshCache();//operatory cache is not part of the startup Cache.Refresh set
			List<Operatory> listOps=Operatories.GetDeepCopy(true);//isShort=true excludes hidden
			List<Provider> listProvsAll=Providers.GetDeepCopy(false);
			List<long> listProvNums;
			if(string.IsNullOrWhiteSpace(providers)) {
				listProvNums=listProvsAll.FindAll(x => !x.IsHidden).Select(x => x.ProvNum).ToList();
			}
			else {
				listProvNums=new List<long>();
				foreach(string part in providers.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)) {
					if(!long.TryParse(part,out long provNumParsed) || !listProvsAll.Exists(x => x.ProvNum==provNumParsed)) {
						return Results.Text($"Unknown provider: {part}",statusCode:400);
					}
					if(!listProvNums.Contains(provNumParsed)) {
						listProvNums.Add(provNumParsed);
					}
				}
			}
			List<object> listSlots=new List<object>();
			if(listOps.Count==0 || listProvNums.Count==0) {
				return Results.Json(new { from=dateStart.ToString("yyyy-MM-dd"),to=dateEnd.ToString("yyyy-MM-dd"),minutes=lengthMinutes,slots=listSlots });
			}
			List<long> listOpNums=listOps.Select(x => x.OperatoryNum).ToList();
			//The desktop search's own reads (ApptSearch.GetDataForSearch).  Copies are passed
			//because GetSchedulesForAppointmentSearch mutates its prov/clinic list arguments.
			List<Schedule> listSchedules;
			try {
				listSchedules=Schedules.GetSchedulesForAppointmentSearch(dateStart,dateEnd,new List<long>(),
					new List<long>(listOpNums),new List<long>(listProvNums),new List<long> { 0 });
			}
			catch(Exception ex) {//"No operatories for these clinics." when clinics are enabled
				return Results.Text(ex.Message,statusCode:400);
			}
			List<Appointment> listAppointments=Appointments.GetForPeriodList(dateStart,dateEnd,listOpNums,new List<long>());
			Dictionary<long,Provider> dictProvs=listProvsAll.ToDictionary(x => x.ProvNum);
			int slotBlocks=(lengthMinutes+4)/5;
			int idxAfter=(int)Math.Ceiling(timeAfter.TotalMinutes/5);
			int idxBeforeStart=(int)Math.Floor(timeBefore.TotalMinutes/5);//latest allowed START block (desktop checks start time only)
			for(int d=0;d<windowDays && listSlots.Count<MaxResults;d++) {
				DateTime day=dateStart.AddDays(d);
				List<Schedule> listSchedsDay=listSchedules.FindAll(x => x.SchedDate.Date==day);
				if(listSchedsDay.Count==0) {
					continue;
				}
				List<Appointment> listApptsDay=listAppointments.FindAll(x => x.AptDateTime.Date==day);
				//Operatory busy map: existing appointments + appointment-blocking blockouts.
				Dictionary<long,bool[]> dictOpBusy=listOps.ToDictionary(x => x.OperatoryNum,x => new bool[BlocksPerDay]);
				foreach(Appointment appt in listApptsDay) {
					if(!dictOpBusy.TryGetValue(appt.Op,out bool[] arrayOpBusy)) {
						continue;
					}
					MarkBusy(arrayOpBusy,(int)(appt.AptDateTime.TimeOfDay.TotalMinutes/5),
						(int)Math.Ceiling(appt.EndTime.TimeOfDay.TotalMinutes/5.0));
				}
				foreach(Schedule blockout in listSchedsDay.FindAll(x => x.SchedType==ScheduleType.Blockout && Schedules.IsAppointmentBlocking(x.BlockoutType))) {
					foreach(long opNum in blockout.Ops) {
						if(dictOpBusy.TryGetValue(opNum,out bool[] arrayOpBusy)) {
							MarkBusy(arrayOpBusy,(int)(blockout.StartTime.TotalMinutes/5),
								(int)Math.Ceiling(blockout.StopTime.TotalMinutes/5.0));
						}
					}
				}
				List<(int idxStart,long provNum,long opNum)> listDaySlots=new List<(int,long,long)>();
				HashSet<(long,long,int)> hashSeen=new HashSet<(long,long,int)>();
				foreach(long provNum in listProvNums) {
					//Provider busy map: the provider's appointments in ANY operatory (mirrors the
					//desktop pulling appts for all searched ops to catch multi-op conflicts).
					bool[] arrayProvBusy=new bool[BlocksPerDay];
					foreach(Appointment appt in listApptsDay.FindAll(x => x.ProvNum==provNum || x.ProvHyg==provNum)) {
						MarkBusy(arrayProvBusy,(int)(appt.AptDateTime.TimeOfDay.TotalMinutes/5),
							(int)Math.Ceiling(appt.EndTime.TimeOfDay.TotalMinutes/5.0));
					}
					foreach(Schedule sched in listSchedsDay.FindAll(x => x.SchedType==ScheduleType.Provider && x.ProvNum==provNum && x.Status==SchedStatus.Open)) {
						List<long> listSchedOpNums=sched.Ops.Count>0?sched.Ops:GetDynamicOpNums(listOps,provNum);
						int idxWinStart=Math.Max((int)Math.Ceiling(sched.StartTime.TotalMinutes/5.0),idxAfter);
						int idxWinStop=Math.Min((int)(sched.StopTime.TotalMinutes/5),BlocksPerDay);
						foreach(long opNum in listSchedOpNums.Distinct()) {
							if(!dictOpBusy.TryGetValue(opNum,out bool[] arrayOpBusy)) {
								continue;//hidden operatory
							}
							int idx=idxWinStart;
							while(idx+slotBlocks<=idxWinStop && idx<=idxBeforeStart) {
								bool isFree=true;
								int idxConflict=idx;
								for(int k=idx;k<idx+slotBlocks;k++) {
									if(arrayOpBusy[k] || arrayProvBusy[k]) {
										isFree=false;
										idxConflict=k;
										break;
									}
								}
								if(!isFree) {
									idx=idxConflict+1;//resume scanning just past the conflict
									continue;
								}
								if(hashSeen.Add((provNum,opNum,idx))) {
									listDaySlots.Add((idx,provNum,opNum));
								}
								idx+=slotBlocks;//step through a long open run in whole-slot increments
							}
						}
					}
				}
				foreach((int idxStart,long provNum,long opNum) in listDaySlots.OrderBy(x => x.idxStart).ThenBy(x => x.provNum).ThenBy(x => x.opNum)) {
					if(listSlots.Count>=MaxResults) {
						break;
					}
					Operatory op=listOps.First(x => x.OperatoryNum==opNum);
					dictProvs.TryGetValue(provNum,out Provider prov);
					listSlots.Add(new {
						date=day.ToString("yyyy-MM-dd"),
						dayOfWeek=day.ToString("ddd"),
						startTime=TimeSpan.FromMinutes(idxStart*5).ToString(@"hh\:mm"),
						stopTime=TimeSpan.FromMinutes((idxStart+slotBlocks)*5).ToString(@"hh\:mm"),
						provNum,
						provAbbr=prov?.Abbr??("#"+provNum),
						opNum,
						opName=string.IsNullOrEmpty(op.Abbrev)?op.OpName:op.Abbrev,
					});
				}
			}
			return Results.Json(new {
				from=dateStart.ToString("yyyy-MM-dd"),
				to=dateEnd.ToString("yyyy-MM-dd"),
				minutes=lengthMinutes,
				slots=listSlots,
			});
		});
	}
}

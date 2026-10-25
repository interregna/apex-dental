/*
 * Employees and time clock: roster with add/edit (Employees.Insert/Update),
 * live clock in/out per employee (ClockEvents.ClockIn/ClockOut, audit-logged
 * via SecurityLogs inside the business layer), and a per-employee timecard
 * over a date range with inline clock-event correction (ClockEvents.Update).
 * Replaces FormEmployeeSelect, FormEmployeeEdit, FormTimeCard,
 * FormClockEventEdit, FormTimeCardManage.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SetupEmployeesTimeclockModule {
	record EmployeeDto(string LName,string FName,string MiddleI,string WirelessPhone,string EmailWork,bool IsHidden);
	record ClockInDto(bool IsAtHome);
	record ClockOutDto(string Status);
	record ClockEventEditDto(string TimeIn,string TimeOut,string Status,string Note);

	public static void Map(WebApplication app) {
		//Sentinel: TimeDisplayed2 before 1880 means "still clocked in" (same convention as the desktop).
		string TimeOrEmpty(DateTime dateTime) => dateTime.Year<1880?"":dateTime.ToString("yyyy-MM-dd HH:mm");

		app.MapGet("/api/setup/employees",() => {
			List<Employee> listEmployees=Employees.GetDeepCopy(false);
			return Results.Json(listEmployees.OrderBy(x => x.LName).ThenBy(x => x.FName).Select(x => new {
				employeeNum=x.EmployeeNum,
				lName=x.LName,
				fName=x.FName,
				middleI=x.MiddleI,
				wirelessPhone=x.WirelessPhone,
				emailWork=x.EmailWork,
				isHidden=x.IsHidden,
				clockStatus=x.ClockStatus,
				isClockedIn=ClockEvents.IsClockedIn(x.EmployeeNum),
			}));
		});

		app.MapPost("/api/setup/employees",(EmployeeDto dto) => {
			if(string.IsNullOrWhiteSpace(dto.LName) && string.IsNullOrWhiteSpace(dto.FName)) {
				return Results.Text("Must include either first name or last name",statusCode:400);
			}
			Employee employee=new Employee();
			employee.LName=dto.LName??"";
			employee.FName=dto.FName??"";
			employee.MiddleI=dto.MiddleI??"";
			employee.WirelessPhone=dto.WirelessPhone??"";
			employee.EmailWork=dto.EmailWork??"";
			employee.EmailPersonal="";
			employee.PayrollID="";
			employee.IsHidden=dto.IsHidden;
			//Same default the desktop shows for an employee who has never clocked in.
			employee.ClockStatus=TimeClockStatus.Home.ToString();
			try {
				Employees.Insert(employee);
			}
			catch(Exception ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
			Employees.RefreshCache();
			return Results.Json(new { employeeNum=employee.EmployeeNum });
		});

		app.MapPut("/api/setup/employees/{empNum:long}",(long empNum,EmployeeDto dto) => {
			Employee employee=Employees.GetEmpFromDB(empNum);
			if(employee==null) {
				return Results.NotFound();
			}
			employee.LName=dto.LName??"";
			employee.FName=dto.FName??"";
			employee.MiddleI=dto.MiddleI??"";
			employee.WirelessPhone=dto.WirelessPhone??"";
			employee.EmailWork=dto.EmailWork??"";
			employee.IsHidden=dto.IsHidden;
			try {
				Employees.Update(employee);
			}
			catch(Exception ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
			Employees.RefreshCache();
			return Results.Json(new { employeeNum=employee.EmployeeNum });
		});

		app.MapPost("/api/setup/employees/{empNum:long}/clockin",(long empNum,ClockInDto dto) => {
			if(Employees.GetEmpFromDB(empNum)==null) {
				return Results.NotFound();
			}
			try {
				ClockEvents.ClockIn(empNum,dto?.IsAtHome??false);
			}
			catch(Exception ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
			Employees.UpdateClockStatus(empNum);//keeps employee.ClockStatus in sync, same as the desktop staff bar
			return Results.Json(new { isClockedIn=ClockEvents.IsClockedIn(empNum) });
		});

		app.MapPost("/api/setup/employees/{empNum:long}/clockout",(long empNum,ClockOutDto dto) => {
			if(Employees.GetEmpFromDB(empNum)==null) {
				return Results.NotFound();
			}
			//Home / Lunch / Break — same three choices as the desktop time clock.
			if(!Enum.TryParse(dto?.Status,out TimeClockStatus timeClockStatus)) {
				return Results.Text("Status must be Home, Lunch, or Break",statusCode:400);
			}
			try {
				ClockEvents.ClockOut(empNum,timeClockStatus);
			}
			catch(Exception ex) {
				return Results.Text(ex.Message,statusCode:400);
			}
			Employees.UpdateClockStatus(empNum);
			return Results.Json(new { isClockedIn=ClockEvents.IsClockedIn(empNum) });
		});

		app.MapGet("/api/setup/employees/{empNum:long}/timecard",(long empNum,string from=null,string to=null) => {
			Employee employee=Employees.GetEmpFromDB(empNum);
			if(employee==null) {
				return Results.NotFound();
			}
			DateTime dateStop=DateTime.TryParse(to,out DateTime dateTo)?dateTo:DateTime.Today;
			DateTime dateStart=DateTime.TryParse(from,out DateTime dateFrom)?dateFrom:dateStop.AddDays(-14);
			List<ClockEvent> listClockEvents=ClockEvents.GetSimpleList(empNum,dateStart,dateStop);
			TimeSpan timeSpanTotal=TimeSpan.Zero;
			foreach(ClockEvent clockEvent in listClockEvents) {
				if(clockEvent.ClockStatus!=TimeClockStatus.Break && clockEvent.TimeDisplayed2.Year>1880) {
					timeSpanTotal+=clockEvent.TimeDisplayed2-clockEvent.TimeDisplayed1;
				}
			}
			return Results.Json(new {
				employeeNum=employee.EmployeeNum,
				name=(employee.FName+" "+employee.LName).Trim(),
				from=dateStart.ToString("yyyy-MM-dd"),
				to=dateStop.ToString("yyyy-MM-dd"),
				totalHours=ClockEvents.Format(timeSpanTotal),
				events=listClockEvents.Select(x => new {
					clockEventNum=x.ClockEventNum,
					date=x.TimeDisplayed1.ToString("yyyy-MM-dd"),
					timeIn=TimeOrEmpty(x.TimeDisplayed1),
					timeOut=TimeOrEmpty(x.TimeDisplayed2),
					status=x.ClockStatus.ToString(),
					isWorkingHome=x.IsWorkingHome,
					note=x.Note,
					hours=(x.TimeDisplayed1.Year>1880 && x.TimeDisplayed2.Year>1880)
						?ClockEvents.Format(x.TimeDisplayed2-x.TimeDisplayed1):"",
				}),
			});
		});

		//Inline clock-event correction (FormClockEventEdit): edits the displayed times only;
		//the original entered times are preserved by the business layer for auditing.
		app.MapPut("/api/setup/clockevents/{clockEventNum:long}",(long clockEventNum,ClockEventEditDto dto) => {
			ClockEvent clockEvent=ClockEvents.GetOne(clockEventNum);
			if(clockEvent==null) {
				return Results.NotFound();
			}
			if(!DateTime.TryParse(dto.TimeIn,out DateTime dateTimeIn)) {
				return Results.Text("Invalid time in",statusCode:400);
			}
			DateTime dateTimeOut=DateTime.MinValue;//blank time out means still clocked in
			if(!string.IsNullOrWhiteSpace(dto.TimeOut) && !DateTime.TryParse(dto.TimeOut,out dateTimeOut)) {
				return Results.Text("Invalid time out",statusCode:400);
			}
			if(dateTimeOut.Year>1880 && dateTimeOut<dateTimeIn) {
				return Results.Text("Time out cannot be before time in",statusCode:400);
			}
			if(!Enum.TryParse(dto.Status,out TimeClockStatus timeClockStatus)) {
				return Results.Text("Status must be Home, Lunch, or Break",statusCode:400);
			}
			clockEvent.TimeDisplayed1=dateTimeIn;
			clockEvent.TimeDisplayed2=dateTimeOut;
			clockEvent.ClockStatus=timeClockStatus;
			clockEvent.Note=dto.Note??"";
			ClockEvents.Update(clockEvent);
			Employees.UpdateClockStatus(clockEvent.EmployeeNum);
			return Results.Json(new { clockEventNum=clockEvent.ClockEventNum });
		});
	}
}

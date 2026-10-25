/*
 * Tasks and Task Lists browser: read slice of the Tasks module.
 * Retires FormTasks / FormTaskListSelect / FormTaskEdit (browse portions).
 * All data flows through the real business layer: TaskLists.GetAll for the
 * tree, Tasks.RefreshChildren for the tasks in one list, Tasks.GetOne plus
 * TaskNotes.GetForTask for the detail pane, and Tasks.RefreshPatientTickets
 * for the per-patient ticket view.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;
//ImplicitUsings pulls in System.Threading.Tasks, so disambiguate the OD Task entity.
using Task=OpenDentBusiness.Task;

namespace OpenDentalWebDemo.Modules;

public static class CommTasksBrowserModule {

	public static void Map(WebApplication app) {
		string Hex(System.Drawing.Color c) => c.ToArgb()==0?"":$"#{c.R:X2}{c.G:X2}{c.B:X2}";
		string DateStr(DateTime dt) => dt.Year>1880?dt.ToString("yyyy-MM-dd HH:mm"):"";

		//Full task-list forest; the client builds the tree from the Parent links,
		//same data FormTaskListSelect walks.
		app.MapGet("/api/tasks/lists",() => {
			List<TaskList> listTaskLists=TaskLists.GetAll();
			return Results.Json(listTaskLists.Select(x => new {
				taskListNum=x.TaskListNum,
				descript=x.Descript,
				parent=x.Parent,
				dateType=x.DateType.ToString(),
				objectType=x.ObjectType.ToString(),
				isRepeating=x.IsRepeating,
				isArchived=x.TaskListStatus==TaskListStatusEnum.Archived,
				newTaskCount=x.NewTaskCount,
			}));
		});

		//Tasks inside one list, mirroring the FormTasks grid.  showDone widens the
		//query to include tasks finished within the last week, like the desktop
		//"Show Done" checkbox with its default date window.
		app.MapGet("/api/tasks/lists/{taskListNum:long}",(long taskListNum,bool showDone=false) => {
			TaskList taskList=TaskLists.GetOne(taskListNum);
			if(taskList==null) {
				return Results.NotFound();
			}
			long userNum=Security.CurUser?.UserNum??0;
			//If this list is someone's inbox, unread flags are tracked against that user.
			long userNumInbox=TaskLists.GetMailboxUserNum(taskListNum);
			DateTime dateStart=showDone?DateTime.Today.AddDays(-7):DateTime.MinValue;
			List<Task> listTasks=Tasks.RefreshChildren(taskListNum,showDone,dateStart,userNum,userNumInbox,TaskType.All);
			List<Def> listDefsPriority=Defs.GetDefsForCategory(DefCat.TaskPriorities);
			return Results.Json(new {
				taskListNum=taskList.TaskListNum,
				descript=taskList.Descript,
				parent=taskList.Parent,
				tasks=listTasks.Select(x => {
					Def defPriority=listDefsPriority.FirstOrDefault(y => y.DefNum==x.PriorityDefNum);
					return new {
						taskNum=x.TaskNum,
						descript=string.IsNullOrEmpty(x.DescriptOverride)?x.Descript:x.DescriptOverride,
						status=x.TaskStatus.ToString(),
						isDone=x.TaskStatus==TaskStatusEnum.Done,
						isUnread=x.IsUnread,
						isRepeating=x.IsRepeating,
						objectType=x.ObjectType.ToString(),
						patientName=x.PatientName??"",
						patNum=x.ObjectType==TaskObjectType.Patient?x.KeyNum:0,
						priority=defPriority?.ItemName??"",
						priorityColor=defPriority==null?"":Hex(defPriority.ItemColor),
						author=x.UserNum==0?"":Userods.GetName(x.UserNum),
						dateTimeEntry=DateStr(x.DateTimeEntry),
						dateTimeFinished=DateStr(x.DateTimeFinished),
					};
				}),
			});
		});

		//One task with its full note thread: the read side of FormTaskEdit.
		app.MapGet("/api/tasks/{taskNum:long}",(long taskNum) => {
			Task task=Tasks.GetOne(taskNum);
			if(task==null) {
				return Results.NotFound();
			}
			List<TaskNote> listTaskNotes=TaskNotes.GetForTask(taskNum);
			List<Def> listDefsPriority=Defs.GetDefsForCategory(DefCat.TaskPriorities);
			Def defPriority=listDefsPriority.FirstOrDefault(x => x.DefNum==task.PriorityDefNum);
			TaskList taskListParent=task.TaskListNum==0?null:TaskLists.GetOne(task.TaskListNum);
			return Results.Json(new {
				taskNum=task.TaskNum,
				taskListNum=task.TaskListNum,
				taskListDescript=taskListParent?.Descript??"",
				descript=task.Descript,
				descriptOverride=task.DescriptOverride??"",
				status=task.TaskStatus.ToString(),
				isRepeating=task.IsRepeating,
				objectType=task.ObjectType.ToString(),
				patNum=task.ObjectType==TaskObjectType.Patient?task.KeyNum:0,
				priority=defPriority?.ItemName??"",
				priorityColor=defPriority==null?"":Hex(defPriority.ItemColor),
				author=task.UserNum==0?"":Userods.GetName(task.UserNum),
				dateTimeEntry=DateStr(task.DateTimeEntry),
				dateTimeFinished=DateStr(task.DateTimeFinished),
				notes=listTaskNotes.OrderBy(x => x.DateTimeNote).Select(x => new {
					taskNoteNum=x.TaskNoteNum,
					user=x.UserNum==0?"":Userods.GetName(x.UserNum),
					dateTimeNote=DateStr(x.DateTimeNote),
					note=x.Note,
				}),
			});
		});

		//Open tickets attached to one patient, same rows the desktop patient-tasks
		//tab shows via Tasks.RefreshPatientTickets.
		app.MapGet("/api/tasks/patient/{patNum:long}",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			long userNum=Security.CurUser?.UserNum??0;
			List<Task> listTasks=Tasks.RefreshPatientTickets(patNum,userNum);
			List<TaskList> listTaskLists=TaskLists.GetAll();
			List<Def> listDefsPriority=Defs.GetDefsForCategory(DefCat.TaskPriorities);
			return Results.Json(listTasks.Select(x => {
				Def defPriority=listDefsPriority.FirstOrDefault(y => y.DefNum==x.PriorityDefNum);
				TaskList taskListParent=listTaskLists.FirstOrDefault(y => y.TaskListNum==x.TaskListNum);
				return new {
					taskNum=x.TaskNum,
					taskListNum=x.TaskListNum,
					taskListDescript=taskListParent?.Descript??"",
					descript=string.IsNullOrEmpty(x.DescriptOverride)?x.Descript:x.DescriptOverride,
					status=x.TaskStatus.ToString(),
					isDone=x.TaskStatus==TaskStatusEnum.Done,
					priority=defPriority?.ItemName??"",
					priorityColor=defPriority==null?"":Hex(defPriority.ItemColor),
					author=x.UserNum==0?"":Userods.GetName(x.UserNum),
					dateTimeEntry=DateStr(x.DateTimeEntry),
				};
			}));
		});
	}
}

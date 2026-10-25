/*
 * Security users and groups viewer (read-only): users list via Userods.GetUsers
 * with group memberships from UserGroupAttaches.GetForUser and linked
 * employee/provider names, plus user groups via UserGroups.GetList and each
 * group's EnumPermType permission grid via GroupPermissions.GetPerms with
 * date restrictions (NewerDate/NewerDays).
 * Replaces FormSecurity, FormUserEdit, FormGroupPermEdit (view portion).
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class SetupSecurityViewModule {
	public static void Map(WebApplication app) {
		app.MapGet("/api/setup/security/users",() => {
			List<Userod> listUsers=Userods.GetUsers();
			List<UserGroup> listUserGroups=UserGroups.GetList();
			return Results.Json(listUsers.OrderBy(x => x.UserName).Select(x => {
				List<long> listUserGroupNums=UserGroupAttaches.GetForUser(x.UserNum).Select(y => y.UserGroupNum).ToList();
				return new {
					userNum=x.UserNum,
					userName=x.UserName,
					isHidden=x.IsHidden,
					employeeNum=x.EmployeeNum,
					employeeName=x.EmployeeNum==0?"":Employees.GetNameFL(x.EmployeeNum),
					provNum=x.ProvNum,
					provAbbr=x.ProvNum==0?"":Providers.GetAbbr(x.ProvNum,includeHidden:true),
					clinicNum=x.ClinicNum,
					clinicIsRestricted=x.ClinicIsRestricted,
					isPasswordResetRequired=x.IsPasswordResetRequired,
					dateTLastLogin=x.DateTLastLogin.Year<1880?"":x.DateTLastLogin.ToString("yyyy-MM-dd HH:mm"),
					userGroups=listUserGroupNums
						.Select(y => listUserGroups.FirstOrDefault(z => z.UserGroupNum==y))
						.Where(y => y!=null)
						.Select(y => new { userGroupNum=y.UserGroupNum,description=y.Description }),
				};
			}));
		});

		app.MapGet("/api/setup/security/groups",() => {
			List<UserGroup> listUserGroups=UserGroups.GetList();
			return Results.Json(listUserGroups.OrderBy(x => x.Description).Select(x => new {
				userGroupNum=x.UserGroupNum,
				description=x.Description,
				countUsers=UserGroupAttaches.GetForUserGroup(x.UserGroupNum).Count,
				countPermissions=GroupPermissions.GetPerms(x.UserGroupNum).Count,
			}));
		});

		app.MapGet("/api/setup/security/groups/{userGroupNum:long}/permissions",(long userGroupNum) => {
			UserGroup userGroup=UserGroups.GetList().FirstOrDefault(x => x.UserGroupNum==userGroupNum);
			if(userGroup==null) {
				return Results.NotFound();
			}
			List<GroupPermission> listGroupPermissions=GroupPermissions.GetPerms(userGroupNum);
			return Results.Json(new {
				userGroupNum=userGroup.UserGroupNum,
				description=userGroup.Description,
				permissions=listGroupPermissions.OrderBy(x => GroupPermissions.GetDesc(x.PermType)).Select(x => new {
					groupPermNum=x.GroupPermNum,
					permType=x.PermType.ToString(),
					description=GroupPermissions.GetDesc(x.PermType),
					fKey=x.FKey,
					//Same convention as the desktop grid: a permission may be limited to records
					//newer than a fixed date, or newer than a rolling number of days.
					newerDate=x.NewerDate.Year<1880?"":x.NewerDate.ToString("yyyy-MM-dd"),
					newerDays=x.NewerDays,
					dateRestriction=x.NewerDate.Year>1880
						?("After "+x.NewerDate.ToString("yyyy-MM-dd"))
						:(x.NewerDays>0?("Last "+x.NewerDays+" days"):""),
				}),
			});
		});
	}
}

/* Security users & groups viewer — read-only two-pane view: users with hidden
   flag, linked employee/provider, and group memberships; user groups expanding
   into their EnumPermType permission grid with date restrictions.
   Replaces FormSecurity / FormUserEdit / FormGroupPermEdit (view) over the
   real Userods / UserGroups / GroupPermissions / UserGroupAttaches layer. */
"use strict";
(()=>{
const {api,esc,$}=window.ODM;

registerODModule({
  id:"setup-security-view",
  label:"Security",
  group:"Practice",
  html:`
  <div class="mod-head"><h1>Security — users &amp; groups</h1>
    <span class="sub">read-only · user accounts · group permissions</span></div>
  <div class="cols c2">
    <div class="card">
      <h2>Users <span class="msg" id="secv-users-msg"></span></h2>
      <div class="gridwrap scrolly"><table class="grid" id="secv-users">
        <thead><tr><th>#</th><th>Username</th><th>Linked to</th><th>Groups</th><th>Last login</th></tr></thead>
        <tbody></tbody>
      </table></div>
    </div>
    <div class="card">
      <h2>User groups <span class="msg" id="secv-groups-msg"></span></h2>
      <div class="gridwrap scrolly"><table class="grid" id="secv-groups">
        <thead><tr><th>#</th><th>Group</th><th>Users</th><th>Perms</th></tr></thead>
        <tbody></tbody>
      </table></div>
      <h2 style="margin-top:14px" id="secv-perms-title">Permissions</h2>
      <div class="gridwrap scrolly"><table class="grid" id="secv-perms">
        <thead><tr><th>Permission</th><th>Date restriction</th></tr></thead>
        <tbody><tr><td colspan="2"><div class="empty"><b>No group selected</b>Click a user group above to see its permissions.</div></td></tr></tbody>
      </table></div>
    </div>
  </div>`,
  init(){
    let selectedGroup=null;

    async function loadUsers(){
      try{
        const users=await api("/api/setup/security/users");
        $("secv-users").querySelector("tbody").innerHTML=users.map(u=>{
          const linked=[
            u.employeeName?`Emp: ${esc(u.employeeName)}`:"",
            u.provAbbr?`Prov: ${esc(u.provAbbr)}`:"",
          ].filter(Boolean).join(" · ")||'<span class="sub">none</span>';
          const groups=(u.userGroups||[]).map(g=>
            `<span class="tag" data-group="${g.userGroupNum}" style="cursor:pointer">${esc(g.description)}</span>`).join(" ")
            ||'<span class="sub">no groups</span>';
          return `<tr><td class="num">${u.userNum}</td>`+
            `<td>${esc(u.userName)}${u.isHidden?' <span class="tag">hidden</span>':""}`+
            `${u.isPasswordResetRequired?' <span class="tag">pw reset</span>':""}</td>`+
            `<td>${linked}</td><td>${groups}</td>`+
            `<td class="mono">${esc(u.dateTLastLogin||"")}</td></tr>`;
        }).join("")
          ||`<tr><td colspan="5"><div class="empty"><b>No users</b>No security users are defined.</div></td></tr>`;
        $("secv-users-msg").textContent="";
        //Clicking a membership tag jumps to that group's permission grid.
        $("secv-users").querySelectorAll("[data-group]").forEach(t=>
          t.addEventListener("click",()=>selectGroup(t.dataset.group,t.textContent)));
      }catch(err){$("secv-users-msg").textContent="users endpoint unavailable — "+err.message.slice(0,60)}
    }

    async function loadGroups(){
      try{
        const groups=await api("/api/setup/security/groups");
        $("secv-groups").querySelector("tbody").innerHTML=groups.map(g=>
          `<tr class="rowsel" data-group="${g.userGroupNum}" data-desc="${esc(g.description)}">`+
          `<td class="num">${g.userGroupNum}</td><td>${esc(g.description)}</td>`+
          `<td class="num">${g.countUsers}</td><td class="num">${g.countPermissions}</td></tr>`).join("")
          ||`<tr><td colspan="4"><div class="empty"><b>No user groups</b>No security groups are defined.</div></td></tr>`;
        $("secv-groups-msg").textContent="";
        $("secv-groups").querySelectorAll("tr.rowsel").forEach(tr=>
          tr.addEventListener("click",()=>selectGroup(tr.dataset.group,tr.dataset.desc)));
      }catch(err){$("secv-groups-msg").textContent="groups endpoint unavailable — "+err.message.slice(0,60)}
    }

    async function selectGroup(userGroupNum,description){
      selectedGroup=userGroupNum;
      $("secv-perms-title").textContent=`Permissions — ${description}`;
      $("secv-groups").querySelectorAll("tr.rowsel").forEach(tr=>
        tr.classList.toggle("sel",tr.dataset.group==userGroupNum));
      try{
        const g=await api(`/api/setup/security/groups/${userGroupNum}/permissions`);
        if(selectedGroup!=userGroupNum)return;//stale response after a faster click elsewhere
        $("secv-perms").querySelector("tbody").innerHTML=g.permissions.map(p=>
          `<tr><td>${esc(p.description)}</td>`+
          `<td>${p.dateRestriction?`<span class="tag">${esc(p.dateRestriction)}</span>`:""}</td></tr>`).join("")
          ||`<tr><td colspan="2"><div class="empty"><b>No permissions</b>This group grants nothing.</div></td></tr>`;
        $("secv-groups-msg").textContent="";
      }catch(err){$("secv-groups-msg").textContent="permissions unavailable — "+err.message.slice(0,60)}
    }

    loadUsers();
    loadGroups();
  }
});
})();

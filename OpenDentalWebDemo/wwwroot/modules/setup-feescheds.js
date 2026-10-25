/* Fee schedules & fee editing — schedule picker with inline new/edit form
   (rename, type, hidden, global), plus a searchable fee grid where amount
   cells save on blur/Enter (blank = delete the fee row, matching desktop).
   Replaces FormFeeScheds / FrmFeeSchedEdit / FormFeeEdit. */
"use strict";
(()=>{
const {api,esc,$}=window.ODM;

const money=v=>"$"+Number(v).toLocaleString(undefined,{minimumFractionDigits:2,maximumFractionDigits:2});
const TYPES=["Normal","CoPay","OutNetwork","FixedBenefit","ManualBlueBook"];

registerODModule({
  id:"setup-feescheds",
  label:"Fee Scheds",
  group:"Practice",
  html:`
  <div class="mod-head"><h1>Fee schedules</h1>
    <span class="sub">schedule setup · per-procedure fee editing</span></div>
  <div class="card">
    <h2>Schedules <span class="msg" id="fs-msg"></span></h2>
    <div class="row" style="display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-bottom:8px">
      <select id="fs-pick" style="min-width:260px"></select>
      <button type="button" class="quiet" id="fs-edit">Edit selected</button>
      <button type="button" class="quiet" id="fs-hidebtn">Hide</button>
      <label style="font-size:13px"><input type="checkbox" id="fs-showhidden"> show hidden</label>
      <span class="msg" id="fs-pick-msg"></span>
    </div>
    <h2 id="fs-form-title">Add fee schedule</h2>
    <form class="row" id="fs-form">
      <input name="description" placeholder="Schedule name" style="width:220px" required>
      <select name="feeSchedType" title="Type (cannot be changed casually once in use)">
        <option value="Normal">Normal</option>
        <option value="CoPay">CoPay</option>
        <option value="OutNetwork">Out of network</option>
        <option value="FixedBenefit">Fixed benefit</option>
        <option value="ManualBlueBook">Manual blue book</option>
      </select>
      <label style="font-size:13px"><input type="checkbox" name="isGlobal"> global</label>
      <label style="font-size:13px"><input type="checkbox" name="isHidden"> hidden</label>
      <button class="action" id="fs-save">Add schedule</button>
      <button type="button" class="quiet" id="fs-cancel" hidden>Cancel edit</button>
      <span class="msg" id="fs-form-msg"></span>
    </form>
  </div>
  <div class="card">
    <h2>Fees <span class="msg" id="fee-count"></span></h2>
    <div class="row" style="margin-bottom:8px">
      <input id="fee-search" placeholder="Search code or description" style="width:280px">
    </div>
    <div class="gridwrap scrolly"><table class="grid" id="fee-grid">
      <thead><tr><th>Code</th><th>Abbr</th><th>Description</th><th style="text-align:right">Fee</th><th style="width:110px"></th></tr></thead>
      <tbody></tbody>
    </table></div>
    <p class="msg" style="margin:8px 0 0">Edit an amount then press Enter or click away to save. Blank the amount to remove the fee (no fee for that code).</p>
  </div>`,
  init(){
    let scheds=[],editing=null,searchTimer=null;

    //---------- schedules ----------
    function selectedSched(){
      return scheds.find(s=>s.feeSchedNum==$("fs-pick").value)||null;
    }

    function fillPicker(keepNum){
      const showHidden=$("fs-showhidden").checked;
      const prev=keepNum||$("fs-pick").value;
      const list=scheds.filter(s=>showHidden||!s.isHidden);
      $("fs-pick").innerHTML=list.map(s=>
        `<option value="${s.feeSchedNum}">${esc(s.description)} (${esc(s.feeSchedType)})`+
        `${s.isGlobal?" [global]":""}${s.isHidden?" — hidden":""}</option>`).join("")
        ||`<option value="">(no fee schedules)</option>`;
      if(prev&&list.some(s=>s.feeSchedNum==prev))$("fs-pick").value=prev;
      const s=selectedSched();
      $("fs-hidebtn").textContent=s&&s.isHidden?"Unhide":"Hide";
    }

    async function loadScheds(keepNum){
      try{
        scheds=await api("/api/feescheds");
        $("fs-msg").textContent="";
        fillPicker(keepNum);
        loadFees();
      }catch(err){$("fs-msg").textContent="feescheds endpoint unavailable — "+err.message.slice(0,60)}
    }

    function resetForm(){
      editing=null;$("fs-form").reset();
      $("fs-form-title").textContent="Add fee schedule";
      $("fs-save").textContent="Add schedule";$("fs-cancel").hidden=true;
      $("fs-form-msg").textContent="";
    }
    $("fs-cancel").addEventListener("click",resetForm);

    $("fs-edit").addEventListener("click",()=>{
      const s=selectedSched();if(!s)return;
      editing=s.feeSchedNum;
      const f=$("fs-form");
      f.description.value=s.description||"";
      f.feeSchedType.value=TYPES.includes(s.feeSchedType)?s.feeSchedType:"Normal";
      f.isGlobal.checked=!!s.isGlobal;
      f.isHidden.checked=!!s.isHidden;
      $("fs-form-title").textContent=`Edit fee schedule #${s.feeSchedNum}`;
      $("fs-save").textContent="Save changes";$("fs-cancel").hidden=false;
    });

    $("fs-hidebtn").addEventListener("click",async()=>{
      const s=selectedSched();if(!s)return;
      $("fs-pick-msg").textContent=s.isHidden?"Unhiding…":"Hiding…";
      try{
        await api(`/api/feescheds/${s.feeSchedNum}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},
          body:JSON.stringify({description:s.description,feeSchedType:s.feeSchedType,
            isGlobal:!!s.isGlobal,isHidden:!s.isHidden})});
        $("fs-pick-msg").textContent="";
        loadScheds(s.feeSchedNum);
      }catch(err){$("fs-pick-msg").textContent="Failed: "+err.message.slice(0,100)}
    });

    $("fs-form").addEventListener("submit",async e=>{
      e.preventDefault();const f=e.target;
      const body={description:f.description.value.trim(),feeSchedType:f.feeSchedType.value,
        isGlobal:f.isGlobal.checked,isHidden:f.isHidden.checked};
      if(!body.description){$("fs-form-msg").textContent="Name is required";return}
      $("fs-form-msg").textContent="Saving…";
      try{
        let keepNum=editing;
        if(editing)await api(`/api/feescheds/${editing}`,{method:"PUT",
          headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
        else{
          const res=await api("/api/feescheds",{method:"POST",
            headers:{"Content-Type":"application/json"},body:JSON.stringify(body)});
          keepNum=res.feeSchedNum;
        }
        resetForm();loadScheds(keepNum);
      }catch(err){$("fs-form-msg").textContent="Save failed: "+err.message.slice(0,120)}
    });

    $("fs-pick").addEventListener("change",()=>{
      const s=selectedSched();
      $("fs-hidebtn").textContent=s&&s.isHidden?"Unhide":"Hide";
      loadFees();
    });
    $("fs-showhidden").addEventListener("change",()=>{fillPicker();loadFees()});

    //---------- fees ----------
    async function loadFees(){
      const tbody=$("fee-grid").querySelector("tbody");
      const s=selectedSched();
      if(!s){tbody.innerHTML="";$("fee-count").textContent="";return}
      const q=$("fee-search").value.trim();
      try{
        const data=await api(`/api/feescheds/${s.feeSchedNum}/fees?search=${encodeURIComponent(q)}`);
        $("fee-count").textContent=`${esc(data.description)} — ${data.feeCount} fee(s) set · showing `+
          `${Math.min(data.totalMatches,data.limit)} of ${data.totalMatches} code(s)`;
        tbody.innerHTML=data.fees.map(x=>
          `<tr><td class="mono">${esc(x.procCode)}</td>`+
          `<td>${esc(x.abbrDesc||"")}</td>`+
          `<td>${esc(x.descript||"")}</td>`+
          `<td style="text-align:right"><input class="fee-amt" data-codenum="${x.codeNum}" `+
            `value="${x.amount==null?"":esc(Number(x.amount).toFixed(2))}" `+
            `style="width:90px;text-align:right" placeholder="(no fee)"></td>`+
          `<td class="msg" data-feestatus="${x.codeNum}">${x.amount==null?"":esc(money(x.amount))}</td></tr>`).join("")
          ||`<tr><td colspan="5"><div class="empty"><b>No matching codes</b>Try a different search.</div></td></tr>`;
        wireFeeInputs(s.feeSchedNum);
      }catch(err){
        tbody.innerHTML="";
        $("fee-count").textContent="fees endpoint unavailable — "+err.message.slice(0,60);
      }
    }

    function statusCell(codeNum){
      return $("fee-grid").querySelector(`[data-feestatus="${codeNum}"]`);
    }

    function wireFeeInputs(feeSchedNum){
      $("fee-grid").querySelectorAll(".fee-amt").forEach(inp=>{
        inp.addEventListener("focus",()=>{inp.dataset.orig=inp.value.trim()});
        inp.addEventListener("keydown",e=>{
          if(e.key==="Enter"){e.preventDefault();inp.blur()}
          else if(e.key==="Escape"){inp.value=inp.dataset.orig??inp.value;inp.blur()}
        });
        inp.addEventListener("blur",async()=>{
          const val=inp.value.trim();
          if(val===(inp.dataset.orig??val))return;//unchanged
          if(val!==""&&(isNaN(Number(val.replace(/[$,]/g,"")))||Number(val.replace(/[$,]/g,""))<0)){
            statusCell(inp.dataset.codenum).textContent="invalid amount";
            return;
          }
          const cell=statusCell(inp.dataset.codenum);
          cell.textContent="saving…";
          try{
            const res=await api(`/api/feescheds/${feeSchedNum}/fees/${inp.dataset.codenum}`,
              {method:"PUT",headers:{"Content-Type":"application/json"},
               body:JSON.stringify({amount:val})});
            if(res.amount==null){inp.value="";cell.textContent="✓ removed"}
            else{inp.value=Number(res.amount).toFixed(2);cell.textContent="✓ "+money(res.amount)}
            inp.dataset.orig=inp.value;
          }catch(err){cell.textContent="save failed: "+err.message.slice(0,60)}
        });
      });
    }

    $("fee-search").addEventListener("input",()=>{
      clearTimeout(searchTimer);
      searchTimer=setTimeout(loadFees,300);
    });

    loadScheds();
  }
});
})();

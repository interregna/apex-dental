/* Interactive, standards-based tooth charting for the browser.
 *
 * Replaces the flat server-rendered <img> with labeled, hoverable SVG that
 * follows the conventions a clinician already reads elsewhere:
 *
 *  Restorative odontogram — Universal (1-32) with an FDI (ISO 3950) toggle,
 *    the 5-surface box glyph (M/O/D/F/L or I), and the standard status colors
 *    (treatment-planned = red, completed = blue, existing = green, referred =
 *    grey) each ALSO carrying a letter badge so meaning never rests on color
 *    alone. Whole-tooth conditions (crown, RCT, implant, extraction, sealant,
 *    missing) draw as glyphs.
 *
 *  Perio chart — the "unrolled" four-panel layout (Maxillary Facial/Palatal,
 *    Mandibular Lingual/Facial) used by Florida Probe and most digital charts.
 *    Six sites per tooth (MB·B·DB / ML·L·DL). Probing depth and gingival
 *    margin are printed AND traced as two thin lines over an mm ruler so pocket
 *    topography reads as a shape; the pocket band shades by depth. Depths >=4mm
 *    are amber, >=6mm crimson (CVD-checked pair; the mm numeral is always the
 *    primary signal). Bleeding = red dot, plus mobility and furcation.
 *
 * Tufte posture throughout: schematic over pseudo-3D, small-multiple tooth
 * cells, thin data marks, recessive rulers, a real key, hover for full detail.
 *
 * Exposes window.ODChart.render(patNum, mode, opts). Depends only on
 * window.ODM (api, esc) + a shared floating tooltip built here.
 */
"use strict";
window.ODChart=(function(){
  const SVGNS="http://www.w3.org/2000/svg";
  const {api,esc}=window.ODM;

  /* ---- numbering ---------------------------------------------------- */
  const MAX=[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16];         //maxillary, patient upper-right -> upper-left
  const MAND=[32,31,30,29,28,27,26,25,24,23,22,21,20,19,18,17];//mandibular, under the tooth above it
  const ANTERIOR=new Set([6,7,8,9,10,11,22,23,24,25,26,27]);
  function fromMidline(t){ return t<=8?9-t:t<=16?t-8:t<=24?25-t:t-24; }
  function fdi(t){ const q=t<=8?1:t<=16?2:t<=24?3:4; return q+""+fromMidline(t); }
  function label(t,sys){ return sys==="fdi"?fdi(t):String(t); }
  //Mesial is toward the midline: right of the box for the mouth's right side, left for the left side.
  function mesialIsLeft(t){ return (t>=9&&t<=16)||(t>=17&&t<=24); }

  /* ---- small DOM/SVG helpers --------------------------------------- */
  function E(tag,attrs,kids){
    const n=document.createElementNS(SVGNS,tag);
    if(attrs)for(const k in attrs){ if(attrs[k]!=null)n.setAttribute(k,attrs[k]); }
    if(kids)for(const c of [].concat(kids))if(c)n.appendChild(c);
    return n;
  }
  function T(x,y,s,cls,anchor){
    const n=E("text",{x,y,class:cls||"",...(anchor?{"text-anchor":anchor}:{})});
    n.textContent=s; return n;
  }

  /* ---- shared floating tooltip ------------------------------------- */
  let tip;
  function tooltip(){
    if(tip)return tip;
    tip=document.createElement("div"); tip.className="odc-tip"; tip.style.display="none";
    document.body.appendChild(tip);
    return tip;
  }
  function bindTip(node,html){
    node.style.cursor="default";
    node.addEventListener("mouseenter",e=>{ const t=tooltip(); t.innerHTML=html; t.style.display="block"; move(e); });
    node.addEventListener("mousemove",move);
    node.addEventListener("mouseleave",()=>{ if(tip)tip.style.display="none"; });
    function move(e){ const t=tooltip(); const pad=14;
      let x=e.clientX+pad, y=e.clientY+pad;
      const r=t.getBoundingClientRect();
      if(x+r.width>window.innerWidth-8)x=e.clientX-r.width-pad;
      if(y+r.height>window.innerHeight-8)y=e.clientY-r.height-pad;
      t.style.left=x+"px"; t.style.top=y+"px";
    }
  }

  /* =================================================================== *
   *  RESTORATIVE ODONTOGRAM
   * =================================================================== */
  const STATUS={
    TP:{k:"TP",name:"Treatment planned",badge:"TP"},
    C:{k:"C",name:"Completed",badge:"C"},
    EC:{k:"EC",name:"Existing",badge:"E"},
    EO:{k:"EO",name:"Existing (other)",badge:"E"},
    R:{k:"R",name:"Referred out",badge:"R"},
  };
  function statusColor(colors,st){
    const m={TP:colors.tp,C:colors.c,EC:colors.ec,EO:colors.eo,R:"#6b7280"};
    return m[st]||colors.c||"#334155";
  }
  //Which triangle/region a surface letter paints, given the tooth's mesial side.
  function regionFor(letter,t){
    const mLeft=mesialIsLeft(t);
    switch(letter){
      case "O": case "I": return "center";
      case "B": case "F": case "V": return "top";
      case "L": return "bottom";
      case "M": return mLeft?"left":"right";
      case "D": return mLeft?"right":"left";
      default: return null;
    }
  }
  //Points for each region of an S-sized surface box at origin.
  function regionPts(region,S){
    const c=S/2, i0=S*0.32, i1=S*0.68;
    switch(region){
      case "top":    return `0,0 ${S},0 ${i1},${i0} ${i0},${i0}`;
      case "bottom": return `${i0},${i1} ${i1},${i1} ${S},${S} 0,${S}`;
      case "left":   return `0,0 ${i0},${i0} ${i0},${i1} 0,${S}`;
      case "right":  return `${S},0 ${S},${S} ${i1},${i1} ${i1},${i0}`;
      case "center": return `${i0},${i0} ${i1},${i0} ${i1},${i1} ${i0},${i1}`;
    }
    return "";
  }

  function paintKind(p){
    const pt=(p.paintType||"").toLowerCase(), code=(p.code||"").toUpperCase();
    if(pt.includes("extraction")||/^D71|^D72|^D73|^D74/.test(code))return "extraction";
    if(pt.includes("implant"))return "implant";
    if(pt.includes("crown")||pt.includes("bridgeabutment"))return "crown";
    if(pt.includes("pontic"))return "pontic";
    if(pt.includes("rootcanal")||pt.includes("rct")||/^D33/.test(code))return "rct";
    if(pt.includes("sealant"))return "sealant";
    if(pt.includes("veneer"))return "veneer";
    return "filling";
  }

  async function renderRestorative(host,patNum,state){
    const d=await api(`/api/patients/${patNum}/toothchart`);
    const colors=d.statusColors||{};
    const missing=new Set((d.missing||[]).map(String));
    //Group procedures by tooth.
    const byTooth={};
    (d.procs||[]).forEach(p=>{ (byTooth[p.toothNum]=byTooth[p.toothNum]||[]).push(p); });

    const sys=state.numbering;
    //Symmetric layout: caption on the outer edge of each arch, tooth numbers on
    //the inner (occlusal) edge so the two number rows sit together for comparison.
    const S=34, GAPX=8, PADX=16, CAPH=15, LABH=15, PADTOP=6, GAPY=16;
    const blockH=CAPH+LABH+S, perRow=16;
    const W=PADX*2+perRow*(S+GAPX)-GAPX;
    const H=PADTOP+blockH*2+GAPY+6;
    const svg=E("svg",{viewBox:`0 0 ${W} ${H}`,width:W,height:H,class:"odc-svg",role:"img",
      "aria-label":"Restorative odontogram"});

    function arch(teeth,rowIdx,archName){
      const yTop=PADTOP+rowIdx*(blockH+GAPY);
      //Maxillary: caption, box, numbers(inner). Mandibular: numbers(inner), box, caption.
      const boxY=rowIdx===0?yTop+CAPH:yTop+LABH;
      const numY=rowIdx===0?boxY+S+12:yTop+11;
      const capY=rowIdx===0?yTop+11:boxY+S+12;
      teeth.forEach((t,i)=>{
        const x=PADX+i*(S+GAPX);
        const g=E("g",{transform:`translate(${x},${boxY})`});
        const isMissing=missing.has(String(t));
        const procs=byTooth[t]||[];
        //Surface box.
        g.appendChild(E("rect",{x:0,y:0,width:S,height:S,rx:3,class:"odc-tooth"+(isMissing?" odc-miss":"")}));
        if(!isMissing){
          //Diagonals + center outline (faint) so the 5 surfaces are always legible.
          g.appendChild(E("line",{x1:0,y1:0,x2:S*0.32,y2:S*0.32,class:"odc-div"}));
          g.appendChild(E("line",{x1:S,y1:0,x2:S*0.68,y2:S*0.32,class:"odc-div"}));
          g.appendChild(E("line",{x1:0,y1:S,x2:S*0.32,y2:S*0.68,class:"odc-div"}));
          g.appendChild(E("line",{x1:S,y1:S,x2:S*0.68,y2:S*0.68,class:"odc-div"}));
          g.appendChild(E("rect",{x:S*0.32,y:S*0.32,width:S*0.36,height:S*0.36,class:"odc-div-c"}));
          //Paint surface fills from procedures.
          let ring=null,glyphs=[];
          procs.forEach(p=>{
            const col=statusColor(colors,p.status);
            const kind=paintKind(p);
            if(kind==="crown"||kind==="pontic"||kind==="implant"||kind==="veneer")ring={col,kind,p};
            if(kind==="extraction")glyphs.push({t:"x",col,p});
            if(kind==="rct")glyphs.push({t:"rct",col,p});
            if(kind==="sealant")glyphs.push({t:"sealant",col,p});
            //Surface shading.
            (p.surf||"").split("").forEach(ch=>{
              const region=regionFor(ch,t);
              if(region)g.appendChild(E("polygon",{points:regionPts(region,S),fill:col,"fill-opacity":0.85,class:"odc-surf"}));
            });
            //Anterior/whole-tooth codes with no surface still deserve a mark.
            if(!p.surf&&kind==="filling")g.appendChild(E("rect",{x:S*0.32,y:S*0.32,width:S*0.36,height:S*0.36,fill:col,"fill-opacity":0.85}));
          });
          //Whole-tooth glyphs on top.
          if(ring){
            if(ring.kind==="implant"){
              g.appendChild(E("line",{x1:S/2,y1:2,x2:S/2,y2:S-2,stroke:ring.col,"stroke-width":2}));
              for(let k=0;k<4;k++)g.appendChild(E("line",{x1:S*0.32,y1:6+k*6,x2:S*0.68,y2:6+k*6,stroke:ring.col,"stroke-width":1.4}));
            }else{
              g.appendChild(E("circle",{cx:S/2,cy:S/2,r:S/2-1,fill:"none",stroke:ring.col,"stroke-width":2,
                ...(ring.kind==="pontic"?{"stroke-dasharray":"3 2"}:{})}));
            }
          }
          glyphs.forEach(gl=>{
            if(gl.t==="x"){ g.appendChild(E("line",{x1:3,y1:3,x2:S-3,y2:S-3,stroke:gl.col,"stroke-width":2}));
              g.appendChild(E("line",{x1:S-3,y1:3,x2:3,y2:S-3,stroke:gl.col,"stroke-width":2})); }
            if(gl.t==="rct")g.appendChild(E("line",{x1:S/2,y1:S*0.34,x2:S/2,y2:S-2,stroke:gl.col,"stroke-width":2}));
            if(gl.t==="sealant")for(let k=0;k<3;k++)g.appendChild(E("circle",{cx:S*(0.35+0.15*k),cy:S/2,r:1.8,fill:gl.col}));
          });
        }else{
          g.appendChild(E("line",{x1:5,y1:5,x2:S-5,y2:S-5,class:"odc-miss-x"}));
          g.appendChild(E("line",{x1:S-5,y1:5,x2:5,y2:S-5,class:"odc-miss-x"}));
        }
        svg.appendChild(g);
        //Tooth number (both systems on hover).
        svg.appendChild(T(x+S/2,numY,label(t,sys),"odc-num"+(isMissing?" odc-num-miss":""),"middle"));
        //Hover target + tooltip.
        const hit=E("rect",{x,y:boxY,width:S,height:S,fill:"transparent",class:"odc-hit",rx:3});
        const rows=isMissing?"<div class='odc-tip-row'>Missing</div>":
          (procs.length?procs.map(p=>`<div class='odc-tip-row'><b>${esc(p.code)}</b> ${esc(p.surf||"")} `+
            `<span class='odc-chip' style='background:${statusColor(colors,p.status)}'>${STATUS[p.status]?STATUS[p.status].badge:p.status}</span><br>`+
            `<span class='odc-tip-sub'>${esc(p.descript||"")}</span></div>`).join(""):
            "<div class='odc-tip-row odc-tip-sub'>No charted procedures</div>");
        bindTip(hit,`<div class='odc-tip-h'>Tooth ${t} · Universal &nbsp;·&nbsp; ${fdi(t)} FDI</div>${rows}`);
        svg.appendChild(hit);
      });
      //Arch caption, centred over the arch on its outer edge.
      svg.appendChild(T(PADX+(perRow*(S+GAPX)-GAPX)/2,capY,archName,"odc-arch","middle"));
    }
    arch(MAX,0,"Maxillary");
    arch(MAND,1,"Mandibular");

    host.innerHTML="";
    host.appendChild(svg);
    host.appendChild(restorativeLegend(colors,sys));
  }

  function swatch(col,extra){ const s=E("svg",{width:16,height:16,class:"odc-key-sw"});
    s.appendChild(E("rect",{x:1,y:1,width:14,height:14,rx:2,fill:col,"fill-opacity":0.85,stroke:col})); if(extra)extra(s); return s; }

  function restorativeLegend(colors,sys){
    const wrap=document.createElement("div"); wrap.className="odc-key";
    function item(node,text){ const d=document.createElement("div"); d.className="odc-key-item";
      d.appendChild(node); const s=document.createElement("span"); s.textContent=text; d.appendChild(s); return d; }
    const g1=document.createElement("div"); g1.className="odc-key-group";
    g1.appendChild(Object.assign(document.createElement("h4"),{textContent:"Status"}));
    [["TP","Treatment planned",statusColor(colors,"TP")],["C","Completed",statusColor(colors,"C")],
     ["E","Existing",statusColor(colors,"EC")],["R","Referred out","#6b7280"]].forEach(([b,name,col])=>{
      const chip=document.createElement("span"); chip.className="odc-chip"; chip.style.background=col; chip.textContent=b;
      g1.appendChild(item(chip,name));
    });
    wrap.appendChild(g1);
    //Condition glyphs (drawn tiny).
    const g2=document.createElement("div"); g2.className="odc-key-group";
    g2.appendChild(Object.assign(document.createElement("h4"),{textContent:"Conditions"}));
    function glyph(draw){ const s=E("svg",{width:22,height:22,class:"odc-key-sw"});
      s.appendChild(E("rect",{x:2,y:2,width:18,height:18,rx:3,class:"odc-tooth"})); draw(s); return s; }
    g2.appendChild(item(glyph(s=>s.appendChild(E("circle",{cx:11,cy:11,r:8,fill:"none",stroke:"#334155","stroke-width":2}))),"Crown"));
    g2.appendChild(item(glyph(s=>{s.appendChild(E("line",{x1:11,y1:7,x2:11,y2:19,stroke:"#334155","stroke-width":2}));}),"Root canal"));
    g2.appendChild(item(glyph(s=>{for(let k=0;k<4;k++)s.appendChild(E("line",{x1:6,y1:6+k*3.5,x2:16,y2:6+k*3.5,stroke:"#334155","stroke-width":1.3}));}),"Implant"));
    g2.appendChild(item(glyph(s=>{s.appendChild(E("line",{x1:4,y1:4,x2:18,y2:18,stroke:"#334155","stroke-width":2}));s.appendChild(E("line",{x1:18,y1:4,x2:4,y2:18,stroke:"#334155","stroke-width":2}));}),"Extraction / missing"));
    wrap.appendChild(g2);
    //Surface-box map.
    const g3=document.createElement("div"); g3.className="odc-key-group";
    g3.appendChild(Object.assign(document.createElement("h4"),{textContent:"Surfaces"}));
    const map=E("svg",{width:70,height:70,class:"odc-key-map"}); const S=60,o=5;
    map.appendChild(E("rect",{x:o,y:o,width:S,height:S,rx:4,class:"odc-tooth"}));
    map.appendChild(E("line",{x1:o,y1:o,x2:o+S*0.32,y2:o+S*0.32,class:"odc-div"}));
    map.appendChild(E("line",{x1:o+S,y1:o,x2:o+S*0.68,y2:o+S*0.32,class:"odc-div"}));
    map.appendChild(E("line",{x1:o,y1:o+S,x2:o+S*0.32,y2:o+S*0.68,class:"odc-div"}));
    map.appendChild(E("line",{x1:o+S,y1:o+S,x2:o+S*0.68,y2:o+S*0.68,class:"odc-div"}));
    map.appendChild(E("rect",{x:o+S*0.32,y:o+S*0.32,width:S*0.36,height:S*0.36,class:"odc-div-c"}));
    const c=o+S/2;
    map.appendChild(T(c,o+9,"F","odc-map-l","middle"));
    map.appendChild(T(c,o+S-3,"L","odc-map-l","middle"));
    map.appendChild(T(o+7,c+3,"M","odc-map-l","middle"));
    map.appendChild(T(o+S-7,c+3,"D","odc-map-l","middle"));
    map.appendChild(T(c,c+3,"O","odc-map-l","middle"));
    g3.appendChild(map);
    const note=document.createElement("div"); note.className="odc-key-note";
    note.innerHTML="F facial · L lingual · M mesial (toward midline) · D distal · O occlusal / I incisal.<br>"+
      `Numbering: <b>${sys==="fdi"?"FDI (ISO 3950)":"Universal 1–32"}</b> — hover any tooth for both.`;
    g3.appendChild(note);
    wrap.appendChild(g3);
    return wrap;
  }

  /* =================================================================== *
   *  PERIO CHART
   * =================================================================== */
  const MMPX=3;              //px per millimetre
  function pdColor(v){ return v>=6?"var(--perio-serious)":v>=4?"var(--perio-caution)":"var(--perio-healthy)"; }
  function roman(n){ return ["","I","II","III","IV"][Math.max(0,Math.min(4,n))]; }

  async function renderPerio(host,patNum,state){
    const d=await api(`/api/patients/${patNum}/perio${state.examNum?`?examNum=${state.examNum}`:""}`);
    const exams=d.exams||[];
    //Index measures: tooth -> sequenceType -> row.
    const M={};
    (d.measures||[]).forEach(m=>{ (M[m.intTooth]=M[m.intTooth]||{})[m.sequenceType]=m; });

    const LEFT=52, TOOTHW=34, SITEW=TOOTHW/3, PADX=8, TITLEH=15;
    const W=LEFT+16*TOOTHW+PADX;
    //Panel bands: BOP, PD nums, topography (16px ceiling + pocket), GM nums.
    const BAND={bop:9,pd:13,topo:52,gm:12};
    const PANELH=TITLEH+BAND.bop+BAND.pd+BAND.topo+BAND.gm+6;
    const MOBH=14; //mobility/furcation strip on facial panels
    const NUMBANDH=16;
    const panels=[
      {arch:MAX, side:"B", title:"Maxillary — Facial (buccal)", facial:true},
      {arch:MAX, side:"L", title:"Maxillary — Palatal (lingual)", facial:false},
      {numbers:true},
      {arch:MAND,side:"L", title:"Mandibular — Lingual", facial:false},
      {arch:MAND,side:"B", title:"Mandibular — Facial (buccal)", facial:true},
    ];
    let H=8; panels.forEach(p=>{ H+=p.numbers?NUMBANDH:(PANELH+(p.facial?MOBH:0)); });
    const svg=E("svg",{viewBox:`0 0 ${W} ${H}`,width:W,height:H,class:"odc-svg odc-perio",role:"img","aria-label":"Periodontal chart"});

    let y=8;
    panels.forEach(p=>{
      if(p.numbers){ drawNumberBand(svg,y,LEFT,TOOTHW,MAX,state.numbering); y+=NUMBANDH; return; }
      drawPanel(p,y);
      y+=PANELH+(p.facial?MOBH:0);
    });

    function siteX(i,s){ return LEFT+i*TOOTHW+SITEW*(s+0.5); }
    function sitesFor(m,side){ return side==="B"?[m.mb,m.b,m.db]:[m.ml,m.l,m.dl]; }
    //Order sites left->right so mesial sits toward the arch midline.
    function orderedSites(vals,t){ return mesialIsLeft(t)?[vals[2],vals[1],vals[0]]:vals; }

    function drawPanel(pan,y0){
      const {arch,side,title,facial}=pan;
      let yy=y0;
      svg.appendChild(T(LEFT,yy+11,title,"odc-arch",null)); yy+=TITLEH;
      //Optional mobility / furcation strip (facial panels only).
      if(facial){
        svg.appendChild(T(LEFT-6,yy+10,"Mob","odc-axis","end"));
        arch.forEach((t,i)=>{
          const mob=M[t]&&M[t].Mobility; const fur=M[t]&&M[t].Furcation;
          const cx=LEFT+i*TOOTHW+TOOTHW/2;
          if(mob&&mob.toothValue>0)svg.appendChild(T(cx,yy+10,String(mob.toothValue),"odc-mob","middle"));
          if(fur){ const cls=Math.max(fur.mb,fur.b,fur.db,fur.ml,fur.l,fur.dl);
            if(cls>0){ svg.appendChild(E("path",{d:`M${cx-4},${yy+4} L${cx+4},${yy+4} L${cx},${yy-3} Z`,
              class:cls>=3?"odc-fur-hi":"odc-fur"})); svg.appendChild(T(cx+9,yy+4,roman(cls),"odc-fur-t","middle")); }
          }
        });
        yy+=MOBH;
      }
      const yBOP=yy+BAND.bop-3; yy+=BAND.bop;
      const yPD=yy+BAND.pd-3;   yy+=BAND.pd;
      const yTopoTop=yy, yCEJ=yy+16, yTopoBot=yy+BAND.topo; yy+=BAND.topo;
      const yGM=yy+BAND.gm-3;   yy+=BAND.gm;

      //mm ruler + CEJ reference.
      svg.appendChild(E("line",{x1:LEFT,y1:yCEJ,x2:LEFT+16*TOOTHW,y2:yCEJ,class:"odc-cej"}));
      svg.appendChild(T(LEFT-6,yCEJ+3,"CEJ","odc-axis","end"));
      [3,6,9].forEach(mm=>{ const yl=yCEJ+mm*MMPX;
        if(yl<=yTopoBot){ svg.appendChild(E("line",{x1:LEFT,y1:yl,x2:LEFT+16*TOOTHW,y2:yl,class:"odc-ruler"}));
          svg.appendChild(T(LEFT-6,yl+3,mm+"mm","odc-axis-sm","end")); } });
      svg.appendChild(T(LEFT-6,yPD+3,"PD","odc-axis","end"));
      svg.appendChild(T(LEFT-6,yGM+3,"GM","odc-axis","end"));
      svg.appendChild(T(LEFT-6,yBOP+3,"BOP","odc-axis-sm","end"));

      //Column separators.
      arch.forEach((t,i)=>{ const xs=LEFT+i*TOOTHW;
        svg.appendChild(E("line",{x1:xs,y1:y0+TITLEH+(facial?MOBH:0),x2:xs,y2:yy,class:"odc-col"})); });
      svg.appendChild(E("line",{x1:LEFT+16*TOOTHW,y1:y0+TITLEH,x2:LEFT+16*TOOTHW,y2:yy,class:"odc-col"}));

      //Topography per the Open Dental / standard perio convention:
      //  gingival margin = recession (positive value = margin apical to CEJ; values
      //    >100 encode hyperplasia coronal to CEJ as 100-value); default 0 = at CEJ.
      //  CAL = gingival margin + probing depth, clamped >= 0 ("CAL can't be negative").
      //The gingival-margin line and the pocket-base (CAL) line are drawn as CONTINUOUS
      //polylines across all measured teeth in the panel (broken only at gaps), so every
      //tooth shows the margin baseline — a tooth with no recession simply rides the CEJ
      //instead of looking like a different chart element. gm(+) drops the line below CEJ.
      const gmDecode=v=>v>100?100-v:v;             //>100 = hyperplasia (coronal)
      let gmSeg=[],pbSeg=[]; const gmSegs=[],pbSegs=[];
      const flush=()=>{ if(gmSeg.length>1)gmSegs.push(gmSeg); if(pbSeg.length>1)pbSegs.push(pbSeg); gmSeg=[];pbSeg=[]; };
      arch.forEach((t,i)=>{
        const pd=M[t]&&M[t].Probing, gm=M[t]&&M[t].GingMargin, bleed=M[t]&&(M[t].BleedSupPlaqCalc||M[t].Bleeding);
        const pdV=pd?orderedSites(sitesFor(pd,side),t):null;
        const gmV=gm?orderedSites(sitesFor(gm,side),t):null;
        const bV =bleed?orderedSites(sitesFor(bleed,side),t):null;
        let anySite=false;
        for(let s=0;s<3;s++){
          const p=pdV?pdV[s]:-1;
          if(p<0)continue;
          anySite=true;
          const x=siteX(i,s);
          const gmm=gmDecode(gmV&&gmV[s]>-1?gmV[s]:0);   //recession (mm); 0 = at CEJ
          const yMargin=yCEJ+gmm*MMPX;                   //recession(+) is apical = down
          const yPocket=yMargin+p*MMPX;                  //pocket base = margin + PD
          gmSeg.push([x,yMargin]); pbSeg.push([x,yPocket]);
          //Pocket band, coloured by probing-depth severity.
          svg.appendChild(E("rect",{x:x-SITEW*0.32,y:Math.min(yMargin,yPocket),width:SITEW*0.64,
            height:Math.abs(yPocket-yMargin)||1,class:p>=6?"odc-pkt-hi":p>=4?"odc-pkt-md":"odc-pkt"}));
          //PD numeral (threshold colour) + GM numeral (only when a recession was recorded).
          const tn=T(x,yPD+3,String(p),"odc-pdnum","middle"); tn.setAttribute("fill",pdColor(p)); svg.appendChild(tn);
          if(gmV&&gmV[s]>-1&&gmDecode(gmV[s])!==0)svg.appendChild(T(x,yGM+3,String(gmDecode(gmV[s])),"odc-gmnum","middle"));
          //BOP dot.
          if(bV&&bV[s]>0)svg.appendChild(E("circle",{cx:x,cy:yBOP,r:2.2,class:"odc-bop"}));
        }
        if(!anySite)flush();                             //break the line across a gap
        //Hover target with the full site read-out incl. CAL.
        if(pd){
          const hit=E("rect",{x:LEFT+i*TOOTHW,y:y0+TITLEH+(facial?MOBH:0),width:TOOTHW,height:yy-(y0+TITLEH+(facial?MOBH:0)),fill:"transparent",class:"odc-hit"});
          const names=mesialIsLeft(t)?(side==="B"?["DB","B","MB"]:["DL","L","ML"]):(side==="B"?["MB","B","DB"]:["ML","L","DL"]);
          let rows="";
          for(let s=0;s<3;s++){ const p=pdV?pdV[s]:-1; if(p<0)continue;
            const g=gmDecode(gmV&&gmV[s]>-1?gmV[s]:0); const cal=Math.max(0,p+g);
            rows+=`<div class='odc-tip-row'>${names[s]}: PD <b>${p}</b> · GM ${g} · CAL <b>${cal}</b>${bV&&bV[s]>0?" · <span style='color:#e11'>BOP</span>":""}</div>`; }
          const mob=M[t]&&M[t].Mobility, fur=M[t]&&M[t].Furcation;
          if(mob&&mob.toothValue>0)rows+=`<div class='odc-tip-row odc-tip-sub'>Mobility ${mob.toothValue}</div>`;
          if(fur){ const cls=Math.max(fur.mb,fur.b,fur.db,fur.ml,fur.l,fur.dl); if(cls>0)rows+=`<div class='odc-tip-row odc-tip-sub'>Furcation ${roman(cls)}</div>`; }
          bindTip(hit,`<div class='odc-tip-h'>Tooth ${t} · ${side==="B"?"facial":"lingual"}</div>${rows||"<div class='odc-tip-sub'>no sites recorded</div>"}`);
          svg.appendChild(hit);
        }
      });
      //Draw the continuous gingival-margin and pocket-base (CAL) lines last, on top.
      flush();
      const poly=(pts,cls)=>{ if(pts.length>1)svg.appendChild(E("polyline",{points:pts.map(p=>p.join(",")).join(" "),class:cls})); };
      gmSegs.forEach(s=>poly(s,"odc-gmline")); pbSegs.forEach(s=>poly(s,"odc-pdline"));
    }

    function drawNumberBand(svg,y,left,tw,arch,sys){
      arch.forEach((t,i)=>{ const cx=left+i*tw+tw/2;
        svg.appendChild(T(cx,y+11,label(t,sys),"odc-num","middle")); });
    }

    host.innerHTML="";
    if(exams.length){
      const bar=document.createElement("div"); bar.className="odc-exambar";
      const sel=document.createElement("select"); sel.className="odc-examsel";
      exams.slice().reverse().forEach(ex=>{ const o=document.createElement("option"); o.value=ex.perioExamNum;
        o.textContent=`${ex.examDate}${ex.provAbbr?" · "+ex.provAbbr:""}${ex.note?" · "+ex.note:""}`;
        if(ex.perioExamNum===(state.examNum||d.selectedExamNum))o.selected=true; sel.appendChild(o); });
      sel.addEventListener("change",()=>{ state.examNum=+sel.value; renderPerio(host,patNum,state); });
      const lbl=document.createElement("span"); lbl.className="odc-axis"; lbl.textContent="Exam:";
      bar.appendChild(lbl); bar.appendChild(sel);
      host.appendChild(bar);
    }
    const scroll=document.createElement("div"); scroll.className="odc-scroll"; scroll.appendChild(svg);
    host.appendChild(scroll);
    host.appendChild(perioLegend());
    if(!exams.length){ const em=document.createElement("div"); em.className="odc-empty";
      em.innerHTML="<b>No periodontal exam on file.</b> Record one in the Perio module, or seed demo data."; host.appendChild(em); }
  }

  function perioLegend(){
    const wrap=document.createElement("div"); wrap.className="odc-key";
    const g1=document.createElement("div"); g1.className="odc-key-group";
    g1.appendChild(Object.assign(document.createElement("h4"),{textContent:"Measurements"}));
    function line(cls,text){ const d=document.createElement("div"); d.className="odc-key-item";
      const s=E("svg",{width:26,height:12,class:"odc-key-sw"}); s.appendChild(E("polyline",{points:"1,9 9,4 17,7 25,3",class:cls})); d.appendChild(s);
      const sp=document.createElement("span"); sp.textContent=text; d.appendChild(sp); return d; }
    g1.appendChild(line("odc-pdline","Pocket base (PD from margin)"));
    g1.appendChild(line("odc-gmline","Gingival margin (GM from CEJ)"));
    const dot=document.createElement("div"); dot.className="odc-key-item";
    const ds=E("svg",{width:26,height:12,class:"odc-key-sw"}); ds.appendChild(E("circle",{cx:13,cy:6,r:3,class:"odc-bop"})); dot.appendChild(ds);
    const dsp=document.createElement("span"); dsp.textContent="Bleeding on probing (BOP)"; dot.appendChild(dsp); g1.appendChild(dot);
    wrap.appendChild(g1);
    const g2=document.createElement("div"); g2.className="odc-key-group";
    g2.appendChild(Object.assign(document.createElement("h4"),{textContent:"Probing depth"}));
    [["1–3 mm","odc-sw-healthy","Healthy"],["4–5 mm","odc-sw-caution","Caution"],["≥6 mm","odc-sw-serious","Serious"]].forEach(([mm,cls,name])=>{
      const d=document.createElement("div"); d.className="odc-key-item";
      const c=document.createElement("span"); c.className="odc-swz "+cls; c.textContent=mm.split(" ")[0].split("–")[0]; d.appendChild(c);
      const s=document.createElement("span"); s.textContent=`${mm} — ${name}`; d.appendChild(s); g2.appendChild(d);
    });
    wrap.appendChild(g2);
    const g3=document.createElement("div"); g3.className="odc-key-group";
    g3.appendChild(Object.assign(document.createElement("h4"),{textContent:"Reading the chart"}));
    const n=document.createElement("div"); n.className="odc-key-note";
    n.innerHTML="Six sites per tooth — <b>MB · B · DB</b> (facial) and <b>ML · L · DL</b> (lingual). "+
      "<b>PD</b> probing depth, <b>GM</b> gingival margin from the CEJ, <b>CAL = PD + GM</b> (on hover). "+
      "<b>Mob</b> mobility 0–3; furcation triangles I–IV (filled when class ≥2, crimson at III+). "+
      "The pocket band deepens and reddens with probing depth; the two lines trace gingival topography down the arch.";
    g3.appendChild(n); wrap.appendChild(g3);
    return wrap;
  }

  /* ---- entry -------------------------------------------------------- */
  const state={numbering:"universal",examNum:null};
  async function render(patNum,mode,host){
    if(!host)return;
    host.classList.add("odc-host");
    try{
      if(mode==="perio")await renderPerio(host,patNum,state);
      else await renderRestorative(host,patNum,state);
    }catch(err){ host.innerHTML=`<div class='odc-empty'>Chart unavailable: ${esc(err.message||String(err))}</div>`; }
  }
  function setNumbering(sys){ state.numbering=sys; }
  return {render,setNumbering,get state(){return state;}};
})();

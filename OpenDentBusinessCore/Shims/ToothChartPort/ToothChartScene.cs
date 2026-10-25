/*
 * Scene builder: ToothChartModel -> renderer-neutral primitives (workplan §4).
 * Teeth are drawn from the projected mesh outlines (ToothAnatomy.json) — the
 * same anatomy the desktop renderers use — with a schematic fallback if the
 * outlines are unavailable. Geometry lives in the classic 500x370 chart space;
 * renderers scale to Size. Freehand drawings and floating text use the 410x307
 * space that ToothInitial coordinates are stored in.
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace OpenDentBusiness.ToothChartPort {

	public abstract class ScenePrimitive { }
	public class SceneLine : ScenePrimitive { public float X1,Y1,X2,Y2,Width=1; public Color Color; }
	public class SceneRect : ScenePrimitive { public float X,Y,W,H,Rx,StrokeWidth=1; public Color? Fill,Stroke; }
	public class SceneEllipse : ScenePrimitive { public float X,Y,W,H; public Color? Fill,Stroke; }
	public class ScenePolyline : ScenePrimitive { public List<PointF> Points=new List<PointF>(); public Color Color; public float Width=2; }
	public class ScenePolygon : ScenePrimitive { public List<PointF> Points=new List<PointF>(); public Color? Fill,Stroke; public float StrokeWidth=0.7f; }
	public class SceneText : ScenePrimitive { public string Text; public float X,Y,Size=9; public Color Color; public bool Centered=true; }

	///<summary>Primitives sharing one transform (used for per-tooth rotation/shift).</summary>
	public class SceneGroup {
		public float TranslateX,TranslateY;
		public float RotateDegrees;
		public float RotateCenterX,RotateCenterY;
		public List<ScenePrimitive> Primitives=new List<ScenePrimitive>();
	}

	public class ToothChartScene {
		public Size Size;
		public Color Background;
		public List<SceneGroup> Groups=new List<SceneGroup>();
		public SceneGroup Global() {
			SceneGroup group=new SceneGroup();
			Groups.Add(group);
			return group;
		}
	}

	public static class ToothChartSceneBuilder {
		//Classic chart space.
		public const float CHART_W=500f,CHART_H=370f;
		const float COL_W=CHART_W/16f;//31.25
		//Upper row: teeth 8..130 tall, number ~134. Lower mirrored. In perio mode the
		//tooth spans shrink so probing/CAL site numbers get clear bands at the edges.
		static float UP_TOOTH_TOP=6,UP_TOOTH_BOT=128,UP_NUM_Y=132;
		static float LO_NUM_Y=252,LO_TOOTH_TOP=252,LO_TOOTH_BOT=364;
		static void SetLayout(bool isPerio) {
			UP_TOOTH_TOP=isPerio?26:6;
			UP_TOOTH_BOT=128;
			UP_NUM_Y=132;
			LO_NUM_Y=252;
			LO_TOOTH_TOP=252;
			LO_TOOTH_BOT=isPerio?344:364;
		}
		static readonly Color COLOR_ENAMEL=Color.FromArgb(255,253,209);//matches ToothGraphic default
		static readonly Color COLOR_CEMENTUM=Color.FromArgb(240,229,187);

		static (bool upper,int col) PosOf(int tooth) => tooth<=16?(true,tooth-1):(false,32-tooth);

		///<summary>Primary letter occupying a permanent tooth position (A-J upper on 4..13, K-T lower on 20..29).</summary>
		static string PrimaryLetterFor(int tooth) {
			if(tooth>=4 && tooth<=13) {
				return ((char)('A'+tooth-4)).ToString();
			}
			if(tooth>=20 && tooth<=29) {
				return ((char)('K'+tooth-20)).ToString();//K (lower left 2nd molar, position 20) .. T (lower right, 29)
			}
			return "";
		}

		static string NumberLabel(int tooth,Sparks3D.ToothNumberingNomenclature nomenclature) {
			//Quadrant-relative index from midline (1=central incisor .. 8=third molar).
			int fromMidline=tooth<=8?9-tooth:tooth<=16?tooth-8:tooth<=24?25-tooth:tooth-24;
			switch(nomenclature) {
				case Sparks3D.ToothNumberingNomenclature.FDI:
					if(tooth<=8) return (10+fromMidline).ToString();
					if(tooth<=16) return (20+fromMidline).ToString();
					if(tooth<=24) return (30+fromMidline).ToString();
					return (40+fromMidline).ToString();
				case Sparks3D.ToothNumberingNomenclature.Haderup:
					//Upper "+", lower "-"; sign on the mesial side of the digit per side of mouth.
					if(tooth<=8) return fromMidline+"+";
					if(tooth<=16) return "+"+fromMidline;
					if(tooth<=24) return "-"+fromMidline;
					return fromMidline+"-";
				case Sparks3D.ToothNumberingNomenclature.Palmer:
					//Text approximation of the Palmer quadrant bracket.
					if(tooth<=8) return fromMidline+"┘";  //┘ upper right
					if(tooth<=16) return "└"+fromMidline; //└ upper left
					if(tooth<=24) return "┌"+fromMidline; //┌ lower left
					return fromMidline+"┐";               //┐ lower right
				default:
					return tooth.ToString();
			}
		}

		public static ToothChartScene Build(ToothChartModel model) {
			SetLayout(model.IsPerioMode);
			ToothChartScene scene=new ToothChartScene() { Size=model.Size,Background=model.ColorBackgroundMain };
			for(int tooth=1;tooth<=32;tooth++) {
				BuildTooth(scene,model,tooth);
			}
			BuildDrawings(scene,model);
			if(model.IsPerioMode) {
				BuildPerio(scene,model);
			}
			return scene;
		}

		static ToothState StateOf(ToothChartModel model,string id) =>
			model.Teeth.TryGetValue(id,out ToothState s)?s:null;

		static void BuildTooth(ToothChartScene scene,ToothChartModel model,int tooth) {
			ToothState state=StateOf(model,tooth.ToString())??new ToothState() { ToothId=tooth.ToString() };
			string letter=PrimaryLetterFor(tooth);
			ToothState statePrimary=letter==""?null:StateOf(model,letter);
			bool showPrimary=(state.IsPrimary || statePrimary!=null) && (statePrimary==null || !statePrimary.IsMissing);
			if(state.IsHidden) {
				return;
			}
			(bool upper,int col)=PosOf(tooth);
			float x=col*COL_W;
			float top=upper?UP_TOOTH_TOP:LO_TOOTH_TOP;
			float bottom=upper?UP_TOOTH_BOT:LO_TOOTH_BOT;
			SceneGroup group=new SceneGroup();
			scene.Groups.Add(group);
			float cx=x+COL_W/2f,cy=(top+bottom)/2f;
			bool mesialIsRight=col<8;
			group.TranslateX=(float)(state.ShiftM*3*(mesialIsRight?-1:1));
			group.TranslateY=(float)(state.ShiftO*3*(upper?1:-1));
			group.RotateDegrees=(float)(state.Rotate+state.TipM+state.TipB);//2D approximation
			group.RotateCenterX=cx;
			group.RotateCenterY=cy;
			bool drawBody=!state.IsMissing || state.PonticColor!=null || state.ImplantColor!=null;
			if(model.IsPerioMode && state.IsMissing && state.ImplantColor==null) {
				drawBody=false;
			}
			//Mixed dentition: primary tooth occupies the crown-side portion, permanent the root side.
			float permTop=top,permBot=bottom;
			if(showPrimary && drawBody) {
				float split=upper?top+(bottom-top)*0.52f:bottom-(bottom-top)*0.52f;
				float primaryTop=upper?split:top+ (0);
				if(upper) {
					BuildToothBody(group,model,statePrimary??new ToothState() { ToothId=letter },letter,x,split,bottom,upper);
					permBot=split;
				}
				else {
					BuildToothBody(group,model,statePrimary??new ToothState() { ToothId=letter },letter,x,top,top+(bottom-top)*0.48f,upper);
					permTop=top+(bottom-top)*0.48f;
				}
			}
			if(drawBody && !(showPrimary && (statePrimary?.IsMissing??false))) {
				BuildToothBody(group,model,state,tooth.ToString(),x,permTop,permBot,upper);
			}
			if(state.SpaceMaintainerColor!=null) {
				float smy=(top+bottom)/2;
				Color c=state.SpaceMaintainerColor.Value;
				group.Primitives.Add(new SceneLine() { X1=x+1,Y1=smy,X2=x+COL_W-1,Y2=smy,Width=2.5f,Color=c });
				group.Primitives.Add(new SceneLine() { X1=x+1,Y1=smy-5,X2=x+1,Y2=smy+5,Width=2.5f,Color=c });
				group.Primitives.Add(new SceneLine() { X1=x+COL_W-1,Y1=smy-5,X2=x+COL_W-1,Y2=smy+5,Width=2.5f,Color=c });
			}
			if(state.BigXColor!=null) {
				group.Primitives.Add(new SceneLine() { X1=x+3,Y1=top,X2=x+COL_W-3,Y2=bottom,Width=2,Color=state.BigXColor.Value });
				group.Primitives.Add(new SceneLine() { X1=x+COL_W-3,Y1=top,X2=x+3,Y2=bottom,Width=2,Color=state.BigXColor.Value });
			}
			string label=NumberLabel(tooth,model.Nomenclature);
			if(showPrimary) {
				label+=" "+letter;
			}
			//Labels go in an untransformed group so tooth rotation/shift leaves them in the row.
			SceneGroup groupLabels=new SceneGroup();
			scene.Groups.Add(groupLabels);
			groupLabels.Primitives.Add(new SceneText() { Text=label,X=x+COL_W/2,Y=upper?UP_NUM_Y:LO_NUM_Y-12,Size=9,Color=model.ColorText });
			if(!string.IsNullOrEmpty(state.Mobility)) {
				groupLabels.Primitives.Add(new SceneText() { Text=state.Mobility,X=x+COL_W/2,Y=upper?UP_NUM_Y+11:LO_NUM_Y-23,Size=8,Color=state.MobilityColor });
			}
		}

		///<summary>Draws one tooth body (permanent or primary) from its mesh outlines into
		///the given vertical span of the tooth's column.</summary>
		static void BuildToothBody(SceneGroup group,ToothChartModel model,ToothState state,string toothId,float x,float top,float bottom,bool upper) {
			string meshKey=ToothAnatomy.MeshKeyFor(toothId);
			if(meshKey==null || !ToothAnatomy.Entries.TryGetValue(meshKey,out ToothAnatomyEntry anatomy) || anatomy.Groups.Count==0) {
				BuildSchematicBody(group,model,state,x,top,bottom,upper);
				return;
			}
			bool mirror=ToothAnatomy.IsMirrored(toothId);
			//Fit the mesh bbox into the span, preserving aspect, centered horizontally.
			float availW=COL_W-4,availH=bottom-top;
			float scale=Math.Min(availW/anatomy.W,availH/anatomy.H);
			float w=anatomy.W*scale,h=anatomy.H*scale;
			float ox=x+2+(availW-w)/2f;
			float oy=top+(availH-h)/2f;
			List<PointF> Map(List<float[]> outline) => outline
				.Select(p => new PointF(ox+(mirror?anatomy.W-p[0]:p[0])*scale,oy+p[1]*scale)).ToList();
			bool rootOnly=state.RetainedRootColor!=null;
			bool pontic=state.PonticColor!=null;
			Color outlineColor=model.ColorText;
			//1. Cementum (roots), unless pontic.
			if(!pontic) {
				Color rootFill=state.RetainedRootColor??COLOR_CEMENTUM;
				AddGroupPolygons(group,anatomy,"Cementum",Map,rootFill,outlineColor,0.6f);
				if(state.ImplantColor!=null && ToothAnatomy.Entries.TryGetValue("implant",out ToothAnatomyEntry implant)) {
					//Replace the root visual with the implant screw outline over the root region.
					RectangleF rootBox=GroupBounds(anatomy,"Cementum",Map);
					if(rootBox.Height>0 && implant.Groups.Count>0) {
						float iscale=Math.Min(rootBox.Width/implant.W,rootBox.Height/implant.H)*0.8f;
						float iox=rootBox.X+(rootBox.Width-implant.W*iscale)/2f;
						float ioy=upper?rootBox.Y:rootBox.Bottom-implant.H*iscale;
						foreach(var outlines in implant.Groups.Values) {
							foreach(var outline in outlines) {
								var points=outline.Select(p => new PointF(iox+p[0]*iscale,ioy+(upper?p[1]:implant.H-p[1])*iscale)).ToList();
								group.Primitives.Add(new ScenePolygon() { Points=points,Fill=state.ImplantColor,Stroke=outlineColor,StrokeWidth=0.5f });
							}
						}
					}
				}
				if(state.RctColor!=null) {
					RectangleF rootBox=GroupBounds(anatomy,"Cementum",Map);
					group.Primitives.Add(new SceneLine() { X1=rootBox.X+rootBox.Width/2,Y1=rootBox.Y+2,
						X2=rootBox.X+rootBox.Width/2,Y2=rootBox.Bottom-2,Width=2.5f,Color=state.RctColor.Value });
				}
			}
			if(rootOnly) {
				return;
			}
			//2. Enamel base (crown), then surface groups, then whole-crown paints on top.
			Color? crownPaint=state.PonticColor??state.CrownColor;
			AddGroupPolygons(group,anatomy,"Enamel",Map,crownPaint??COLOR_ENAMEL,outlineColor,0.8f);
			AddGroupPolygons(group,anatomy,"EnamelF",Map,crownPaint??COLOR_ENAMEL,outlineColor,0.4f);
			if(crownPaint==null) {
				//Surface painting per the desktop SetSurfaceColors mapping (M also paints MF, etc).
				foreach(var kv in state.SurfaceColors) {
					foreach(string groupName in SurfaceGroups(kv.Key)) {
						AddGroupPolygons(group,anatomy,groupName,Map,kv.Value,outlineColor,0.4f);
					}
				}
				//Unpainted surface regions still show their outlines faintly.
				foreach(string groupName in new[]{ "M","O","D","B","L","F","I","V","MF","DF","IF" }) {
					if(!state.SurfaceColors.Keys.SelectMany(SurfaceGroupsChars).Contains(groupName)) {
						AddGroupPolygons(group,anatomy,groupName,Map,null,Color.FromArgb(150,outlineColor),0.35f);
					}
				}
			}
			else {
				//Whole-crown paint covers the surface regions too.
				foreach(string groupName in new[]{ "M","O","D","B","L","F","I","V","MF","DF","IF" }) {
					AddGroupPolygons(group,anatomy,groupName,Map,crownPaint,null,0);
				}
			}
			if(state.VeneerColor!=null) {
				foreach(string groupName in new[]{ "EnamelF","B","F","V" }) {
					AddGroupPolygons(group,anatomy,groupName,Map,state.VeneerColor,null,0);
				}
			}
			if(state.BuColor!=null) {
				AddGroupPolygons(group,anatomy,"Buildup",Map,state.BuColor,null,0);
			}
			if(state.SealantColor!=null) {
				RectangleF oBox=GroupBounds(anatomy,"O",Map);
				if(oBox.Width<=0) {
					oBox=GroupBounds(anatomy,"Enamel",Map);
				}
				for(int i=0;i<3;i++) {
					group.Primitives.Add(new SceneEllipse() { X=oBox.X+oBox.Width*(0.2f+0.3f*i),Y=oBox.Y+oBox.Height/2-1.5f,W=3,H=3,Fill=state.SealantColor });
				}
			}
			if(!string.IsNullOrEmpty(state.PaintText)) {
				RectangleF eBox=GroupBounds(anatomy,"Enamel",Map);
				group.Primitives.Add(new SceneText() { Text=state.PaintText,X=eBox.X+eBox.Width/2,Y=eBox.Y+eBox.Height/2-5,Size=8,Color=state.PaintTextColor });
			}
		}

		static IEnumerable<string> SurfaceGroups(char surface) => SurfaceGroupsChars(surface);
		static IEnumerable<string> SurfaceGroupsChars(char surface) {
			switch(surface) {
				case 'M': return new[]{ "M","MF" };
				case 'D': return new[]{ "D","DF" };
				case 'I': return new[]{ "I","IF" };
				case 'O': return new[]{ "O" };
				case 'B': return new[]{ "B" };
				case 'L': return new[]{ "L" };
				case 'F': return new[]{ "F" };
				case 'V': return new[]{ "V" };
				default: return Array.Empty<string>();
			}
		}

		static void AddGroupPolygons(SceneGroup group,ToothAnatomyEntry anatomy,string groupName,
			Func<List<float[]>,List<PointF>> map,Color? fill,Color? stroke,float strokeWidth) {
			if(!anatomy.Groups.TryGetValue(groupName,out var outlines)) {
				return;
			}
			foreach(var outline in outlines) {
				group.Primitives.Add(new ScenePolygon() { Points=map(outline),Fill=fill,Stroke=stroke,StrokeWidth=strokeWidth });
			}
		}

		static RectangleF GroupBounds(ToothAnatomyEntry anatomy,string groupName,Func<List<float[]>,List<PointF>> map) {
			if(!anatomy.Groups.TryGetValue(groupName,out var outlines) || outlines.Count==0) {
				return RectangleF.Empty;
			}
			var points=outlines.SelectMany(o => map(o)).ToList();
			float minx=points.Min(p => p.X),maxx=points.Max(p => p.X);
			float miny=points.Min(p => p.Y),maxy=points.Max(p => p.Y);
			return new RectangleF(minx,miny,maxx-minx,maxy-miny);
		}

		///<summary>Fallback when anatomy outlines are unavailable.</summary>
		static void BuildSchematicBody(SceneGroup group,ToothChartModel model,ToothState state,float x,float top,float bottom,bool upper) {
			float h=bottom-top;
			float crownTop=upper?bottom-h*0.4f:top;
			float crownH=h*0.4f;
			float rootTop=upper?top:top+crownH;
			float rootH=h*0.6f;
			group.Primitives.Add(new SceneRect() { X=x+COL_W*0.25f,Y=rootTop,W=COL_W*0.5f,H=rootH,Rx=6,
				Fill=state.RetainedRootColor??COLOR_CEMENTUM,Stroke=model.ColorText });
			group.Primitives.Add(new SceneRect() { X=x+COL_W*0.08f,Y=crownTop,W=COL_W*0.84f,H=crownH,Rx=7,
				Fill=state.PonticColor??state.CrownColor??COLOR_ENAMEL,Stroke=model.ColorText });
		}

		static void BuildDrawings(ToothChartScene scene,ToothChartModel model) {
			//ToothInitial drawings/text are stored in a 410x307 space.
			float sx=CHART_W/410f,sy=CHART_H/307f;
			SceneGroup group=scene.Global();
			foreach(ChartDrawingSegment segment in model.DrawingSegments) {
				ScenePolyline polyline=new ScenePolyline() { Color=segment.Color,Width=2 };
				foreach(string pointStr in (segment.Segment??"").Split(';')) {
					string[] xy=pointStr.Split(',');
					if(xy.Length==2 && float.TryParse(xy[0],out float px) && float.TryParse(xy[1],out float py)) {
						polyline.Points.Add(new PointF(px*sx,py*sy));
					}
				}
				if(polyline.Points.Count>1) {
					group.Primitives.Add(polyline);
				}
			}
			foreach(ChartText text in model.Texts) {
				group.Primitives.Add(new SceneText() { Text=text.Text,X=text.Location.X*sx,Y=text.Location.Y*sy,Size=9,Color=text.Color,Centered=false });
			}
		}

		static void BuildPerio(ToothChartScene scene,ToothChartModel model) {
			SceneGroup group=scene.Global();
			foreach(var byTooth in model.PerioMeasures.GroupBy(x => x.IntTooth)) {
				int tooth=byTooth.Key;
				if(tooth<1 || tooth>32) {
					continue;
				}
				ToothState state=StateOf(model,tooth.ToString());
				if(state!=null && state.IsMissing && state.ImplantColor==null) {
					continue;
				}
				(bool upper,int col)=PosOf(tooth);
				float x=col*COL_W;
				//Approximate cervical line (crown/root junction) for GM/MGJ anchoring.
				float cej=upper?UP_TOOTH_BOT-(UP_TOOTH_BOT-UP_TOOTH_TOP)*0.4f:LO_TOOTH_TOP+(LO_TOOTH_BOT-LO_TOOTH_TOP)*0.4f;
				float apical=upper?-1:1;//direction from CEJ toward root apex... upper roots are up (-y)
				apical=upper?-1:1;
				foreach(ChartPerioMeasure measure in byTooth) {
					switch(measure.SequenceType) {
						case Sparks3D.PerioSequenceType.Probing:
						case Sparks3D.PerioSequenceType.CAL: {
							Color c=measure.SequenceType==Sparks3D.PerioSequenceType.Probing?model.ColorProbing:model.ColorCAL;
							float rowOffset=measure.SequenceType==Sparks3D.PerioSequenceType.CAL?9f:0f;
							float yB=upper?8+rowOffset:CHART_H-14-rowOffset+8;
							float yL=upper?UP_NUM_Y+22+rowOffset:LO_NUM_Y-32-rowOffset;
							DrawSites(group,x,yB,measure.MB,measure.B,measure.DB,c);
							DrawSites(group,x,yL,measure.ML,measure.L,measure.DL,c);
							break;
						}
						case Sparks3D.PerioSequenceType.Bleeding: {
							float yB=upper?UP_TOOTH_BOT+3:LO_TOOTH_TOP-3;
							int[] sites={ measure.MB,measure.B,measure.DB };
							for(int i=0;i<3;i++) {
								if(sites[i]>0) {
									group.Primitives.Add(new SceneEllipse() { X=x+COL_W*(0.2f+0.3f*i),Y=yB-2,W=4,H=4,Fill=model.ColorBleeding });
								}
							}
							break;
						}
						case Sparks3D.PerioSequenceType.GingMargin:
						case Sparks3D.PerioSequenceType.MGJ: {
							//Polyline across the 3 buccal sites, offset from the cervical line in mm (~2px/mm).
							Color c=measure.SequenceType==Sparks3D.PerioSequenceType.GingMargin?model.ColorGM:model.ColorMGJ;
							float baseOffset=measure.SequenceType==Sparks3D.PerioSequenceType.MGJ?14f:0f;
							int[] sites={ measure.MB,measure.B,measure.DB };
							ScenePolyline line=new ScenePolyline() { Color=c,Width=1.2f };
							for(int i=0;i<3;i++) {
								if(sites[i]==-1) {
									continue;
								}
								float y=cej+apical*(baseOffset+sites[i]*2f);
								line.Points.Add(new PointF(x+COL_W*(0.2f+0.3f*i),y));
							}
							if(line.Points.Count>1) {
								group.Primitives.Add(line);
							}
							break;
						}
						case Sparks3D.PerioSequenceType.Furcation: {
							//Triangle marker at the root furcation area; class III+ uses the red color.
							int cls=Math.Max(Math.Max(measure.MB,measure.B),Math.Max(measure.DB,Math.Max(measure.ML,Math.Max(measure.L,measure.DL))));
							if(cls<=0) {
								break;
							}
							Color c=cls>=3?model.ColorFurcationsRed:model.ColorFurcations;
							float fy=upper?(UP_TOOTH_TOP+cej)/2:(cej+LO_TOOTH_BOT)/2;
							ScenePolygon triangle=new ScenePolygon() { Fill=cls>=2?c:(Color?)null,Stroke=c,StrokeWidth=1.2f };
							triangle.Points.Add(new PointF(x+COL_W/2-4,fy+3));
							triangle.Points.Add(new PointF(x+COL_W/2+4,fy+3));
							triangle.Points.Add(new PointF(x+COL_W/2,fy-4));
							group.Primitives.Add(triangle);
							break;
						}
					}
				}
			}
		}

		static void DrawSites(SceneGroup group,float x,float y,int m,int b,int d,Color color) {
			int[] sites={ m,b,d };
			for(int i=0;i<3;i++) {
				if(sites[i]==-1) {
					continue;//no measurement taken
				}
				group.Primitives.Add(new SceneText() { Text=sites[i].ToString(),X=x+COL_W*(0.2f+0.3f*i),Y=y-4,Size=7,Color=color });
			}
		}
	}
}

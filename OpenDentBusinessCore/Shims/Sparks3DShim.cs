/*
 * Headless stand-in for the closed Sparks3D tooth chart (Direct2D, Windows-only).
 * Presents the exact surface ToothChartHelper.cs consumes, so that file compiles
 * and runs unmodified in the Linux core. Commands are recorded into a
 * ToothChartModel; RenderScene builds a renderer-neutral scene which GetBitmap
 * rasterizes via GDI/libgdiplus and GetSvg serializes as SVG markup.
 */
using System;
using System.Drawing;
using OpenDentBusiness.ToothChartPort;

namespace Sparks3D {

	public enum ToothNumberingNomenclature {
		Universal=0, FDI=1, Haderup=2, Palmer=3
	}

	public enum PerioSequenceType {
		Probing, MGJ, GingMargin, Furcation, CAL, Bleeding
	}

	public class ToothChart : IDisposable {
		private readonly ToothChartModel _model=new ToothChartModel();
		private ToothChartScene _scene;
		///<summary>SVG of the most recent RenderScene in this process; lets callers that
		///only get an Image from ToothChartHelper also retrieve vector output.</summary>
		public static string LastRenderedSvg { get; private set; }

		public ToothChart(bool hasHwnd=true) { }
		public void Dispose() { }

		public Size Size { get { return _model.Size; } set { _model.Size=value; } }
		public Color ColorBackgroundMain { get { return _model.ColorBackgroundMain; } set { _model.ColorBackgroundMain=value; } }
		public Color ColorText { get { return _model.ColorText; } set { _model.ColorText=value; } }
		public Color ColorCAL { get { return _model.ColorCAL; } set { _model.ColorCAL=value; } }
		public Color ColorFurcations { get { return _model.ColorFurcations; } set { _model.ColorFurcations=value; } }
		public Color ColorFurcationsRed { get { return _model.ColorFurcationsRed; } set { _model.ColorFurcationsRed=value; } }
		public Color ColorGM { get { return _model.ColorGM; } set { _model.ColorGM=value; } }
		public Color ColorMGJ { get { return _model.ColorMGJ; } set { _model.ColorMGJ=value; } }
		public Color ColorProbing { get { return _model.ColorProbing; } set { _model.ColorProbing=value; } }
		public Color ColorProbingRed { get { return _model.ColorProbingRed; } set { _model.ColorProbingRed=value; } }
		public Color ColorSuppuration { get { return _model.ColorSuppuration; } set { _model.ColorSuppuration=value; } }
		public Color ColorBleeding { get { return _model.ColorBleeding; } set { _model.ColorBleeding=value; } }

		public void ResetTeeth() => _model.Reset();
		public void SetToothNumberingNomenclature(ToothNumberingNomenclature nomenclature) => _model.Nomenclature=nomenclature;
		public void SetPrimary(string toothId) => _model.Tooth(toothId).IsPrimary=true;
		public void SetMissing(string toothId) => _model.Tooth(toothId).IsMissing=true;
		public void SetHidden(string toothId) => _model.Tooth(toothId).IsHidden=true;
		public void MoveTooth(string toothId,double shiftM,double shiftO,double shiftB,double rotate,double tipM,double tipB) {
			ToothState state=_model.Tooth(toothId);
			state.ShiftM+=shiftM;
			state.ShiftO+=shiftO;
			state.ShiftB+=shiftB;
			state.Rotate+=rotate;
			state.TipM+=tipM;
			state.TipB+=tipB;
		}
		public void SetSurfaceColors(string toothId,string surfaces,Color color) {
			ToothState state=_model.Tooth(toothId);
			foreach(char c in (surfaces??"").ToUpperInvariant()) {
				state.SurfaceColors[c]=color;
			}
		}
		public void SetCrown(string toothId,Color color) => _model.Tooth(toothId).CrownColor=color;
		public void SetPontic(string toothId,Color color) { _model.Tooth(toothId).PonticColor=color; _model.Tooth(toothId).IsMissing=false; }
		public void SetImplant(string toothId,Color color) => _model.Tooth(toothId).ImplantColor=color;
		public void SetRCT(string toothId,Color color) => _model.Tooth(toothId).RctColor=color;
		public void SetBU(string toothId,Color color) => _model.Tooth(toothId).BuColor=color;
		public void SetSealant(string toothId,Color color) => _model.Tooth(toothId).SealantColor=color;
		public void SetRetainedRoot(string toothId,Color color) => _model.Tooth(toothId).RetainedRootColor=color;
		public void SetSpaceMaintainer(string toothId,Color color) => _model.Tooth(toothId).SpaceMaintainerColor=color;
		public void SetVeneer(string toothId,Color color) => _model.Tooth(toothId).VeneerColor=color;
		public void SetBigX(string toothId,Color color) => _model.Tooth(toothId).BigXColor=color;
		public void SetText(string toothId,Color color,string text) { _model.Tooth(toothId).PaintText=text; _model.Tooth(toothId).PaintTextColor=color; }
		public void SetMobility(string toothId,string mobility,Color color) { _model.Tooth(toothId).Mobility=mobility; _model.Tooth(toothId).MobilityColor=color; }
		public void AddDrawingSegment(string segment,Color color) => _model.DrawingSegments.Add(new ChartDrawingSegment() { Segment=segment,Color=color });
		public void AddText(string text,PointF location,Color color,long id) => _model.Texts.Add(new ChartText() { Text=text,Location=location,Color=color,Id=id });
		public void AddPerioMeasure(int intTooth,PerioSequenceType sequenceType,int mb,int b,int db,int ml,int l,int dl) =>
			_model.PerioMeasures.Add(new ChartPerioMeasure() { IntTooth=intTooth,SequenceType=sequenceType,MB=mb,B=b,DB=db,ML=ml,L=l,DL=dl });
		public void SetPerioMode() => _model.IsPerioMode=true;

		public void RenderScene() {
			_scene=ToothChartSceneBuilder.Build(_model);
			LastRenderedSvg=SvgSceneRenderer.Render(_scene);
		}

		public Image GetBitmap() {
			if(_scene==null) {
				RenderScene();
			}
			return GdiSceneRenderer.Render(_scene);
		}

		public string GetSvg() {
			if(_scene==null) {
				RenderScene();
			}
			return SvgSceneRenderer.Render(_scene);
		}
	}
}

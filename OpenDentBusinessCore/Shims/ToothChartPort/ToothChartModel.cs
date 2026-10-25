/*
 * Renderer-neutral tooth chart model for the Linux core (workplan §4 step 2).
 * The Sparks3D.ToothChart shim records every command ToothChartHelper issues
 * into this model; scene building and rendering happen afterwards.
 */
using System;
using System.Collections.Generic;
using System.Drawing;

namespace OpenDentBusiness.ToothChartPort {

	public class ToothState {
		public string ToothId;//"1".."32" or "A".."T"
		public bool IsMissing;
		public bool IsHidden;
		public bool IsPrimary;
		public double ShiftM;
		public double ShiftO;
		public double ShiftB;
		public double Rotate;
		public double TipM;
		public double TipB;
		public Dictionary<char,Color> SurfaceColors=new Dictionary<char,Color>();
		public Color? CrownColor;
		public Color? PonticColor;
		public Color? ImplantColor;
		public Color? RctColor;
		public Color? BuColor;
		public Color? SealantColor;
		public Color? RetainedRootColor;
		public Color? SpaceMaintainerColor;
		public Color? VeneerColor;
		public Color? BigXColor;
		public string PaintText;
		public Color PaintTextColor;
		public string Mobility;
		public Color MobilityColor;
	}

	public class ChartDrawingSegment {
		public string Segment;//"x1,y1;x2,y2;..." in the classic 500x370 chart space
		public Color Color;
	}

	public class ChartText {
		public string Text;
		public PointF Location;//classic chart space
		public Color Color;
		public long Id;
	}

	public class ChartPerioMeasure {
		public int IntTooth;
		public Sparks3D.PerioSequenceType SequenceType;
		public int MB,B,DB,ML,L,DL;
	}

	public class ToothChartModel {
		public Size Size=new Size(500,370);
		public Color ColorBackgroundMain=Color.White;
		public Color ColorText=Color.Black;
		public Color ColorCAL,ColorFurcations,ColorFurcationsRed,ColorGM,ColorMGJ,
			ColorProbing,ColorProbingRed,ColorSuppuration,ColorBleeding;
		public Sparks3D.ToothNumberingNomenclature Nomenclature=Sparks3D.ToothNumberingNomenclature.Universal;
		public bool IsPerioMode;
		public Dictionary<string,ToothState> Teeth=new Dictionary<string,ToothState>();
		public List<ChartDrawingSegment> DrawingSegments=new List<ChartDrawingSegment>();
		public List<ChartText> Texts=new List<ChartText>();
		public List<ChartPerioMeasure> PerioMeasures=new List<ChartPerioMeasure>();

		public ToothState Tooth(string toothId) {
			if(toothId==null) {
				toothId="";
			}
			if(!Teeth.TryGetValue(toothId,out ToothState state)) {
				state=new ToothState() { ToothId=toothId };
				Teeth[toothId]=state;
			}
			return state;
		}

		public void Reset() {
			Teeth.Clear();
			DrawingSegments.Clear();
			Texts.Clear();
			PerioMeasures.Clear();
			IsPerioMode=false;
		}
	}
}

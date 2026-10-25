/*
 * Stubs for UI widget types referenced by business-layer code (mostly the
 * SheetFramework renderers). Headless behavior: grids report zero ideal
 * width and draw nothing, signature boxes return empty signatures, text
 * measurement is approximated without GDI. Sheet PDF rendering that depends
 * on these will degrade; database and business-rule paths do not.
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PdfSharp.Drawing;

namespace System.Windows.Forms {
	public class UserControl : Control {
	}

	public enum BorderStyle {
		None=0, FixedSingle=1, Fixed3D=2
	}

	public enum RichTextBoxScrollBars {
		None=0, Horizontal=1, Vertical=2, Both=3, ForcedHorizontal=17, ForcedVertical=18, ForcedBoth=19
	}

	[Flags]
	public enum Keys {
		None=0, Back=8, Tab=9, Enter=13, Escape=27, Space=32, Insert=45, Delete=46,
		A=65, B=66, C=67, D=68, E=69, F=70, G=71, H=72, I=73, J=74, K=75, L=76, M=77,
		N=78, O=79, P=80, Q=81, R=82, S=83, T=84, U=85, V=86, W=87, X=88, Y=89, Z=90,
		Shift=0x10000, Control=0x20000, Alt=0x40000
	}

	public class KeyEventArgs : EventArgs {
		public KeyEventArgs(Keys keyData) { KeyData=keyData; }
		public Keys KeyData { get; }
		public Keys KeyCode => KeyData & ~(Keys.Shift|Keys.Control|Keys.Alt);
		public bool Control => (KeyData&Keys.Control)==Keys.Control;
		public bool Shift => (KeyData&Keys.Shift)==Keys.Shift;
		public bool Alt => (KeyData&Keys.Alt)==Keys.Alt;
		public bool Handled { get; set; }
		public bool SuppressKeyPress { get; set; }
	}

	public delegate void KeyEventHandler(object sender,KeyEventArgs e);

	public class RichTextBox : TextBox {
		public BorderStyle BorderStyle { get; set; }
		public RichTextBoxScrollBars ScrollBars { get; set; }
		public HorizontalAlignment SelectionAlignment { get; set; }
		public int SelectionStart { get; set; }
		public int SelectionLength { get; set; }
		public string SelectedText { get; set; } = "";
		public string Rtf { get; set; } = "";
		public string[] Lines => (Text??"").Split('\n');
		public bool WordWrap { get; set; } = true;
		public event KeyEventHandler KeyDown;
		public void Paste() { }
		public void Paste(System.Windows.Forms.DataFormats.Format clipFormat) { }
		public int GetFirstCharIndexFromLine(int lineNumber) => 0;
		public int GetLineFromCharIndex(int index) => 0;
		public System.Drawing.Point GetPositionFromCharIndex(int index) => System.Drawing.Point.Empty;
		public int GetCharIndexFromPosition(System.Drawing.Point pt) => 0;
	}

	public class WebBrowser : Control {
		public Version Version => new Version(0,0);
	}

	public class DataFormats {
		public const string Text="Text";
		public const string UnicodeText="UnicodeText";
		public const string Rtf="Rich Text Format";
		public const string Html="HTML Format";
		public const string Bitmap="Bitmap";
		public const string FileDrop="FileDrop";
		public class Format {
			public Format(string name,int id) { Name=name; Id=id; }
			public string Name { get; }
			public int Id { get; }
		}
		public static Format GetFormat(string format) => new Format(format,0);
	}

	public interface IDataObject {
		object GetData(string format);
		object GetData(string format,bool autoConvert);
		bool GetDataPresent(string format);
		string[] GetFormats();
	}

	[Flags]
	public enum TextFormatFlags {
		Default=0, GlyphOverhangPadding=0, Left=0, Top=0, WordBreak=16, TextBoxControl=8192, NoPadding=268435456
	}

	///<summary>Headless text measurement. Uses real GDI metrics via libgdiplus when
	///available (System.Drawing.EnableUnixSupport); falls back to a character-count
	///approximation when GDI is unusable so callers still get plausible sizes.</summary>
	public static class TextRenderer {
		private static Graphics _graphicsMeasure;
		private static bool _triedGdi;

		private static Graphics GetMeasureGraphics() {
			if(!_triedGdi) {
				_triedGdi=true;
				try {
					_graphicsMeasure=Graphics.FromImage(new Bitmap(1,1));
				}
				catch(Exception ex) {
					Console.Error.WriteLine("[TextRenderer] GDI unavailable, using approximate metrics: "+ex.Message);
				}
			}
			return _graphicsMeasure;
		}

		public static System.Drawing.Size MeasureText(string text,Font font) {
			return MeasureText(text,font,new System.Drawing.Size(int.MaxValue,int.MaxValue),TextFormatFlags.Default);
		}
		public static System.Drawing.Size MeasureText(string text,Font font,System.Drawing.Size proposedSize) {
			return MeasureText(text,font,proposedSize,TextFormatFlags.Default);
		}
		public static System.Drawing.Size MeasureText(string text,Font font,System.Drawing.Size proposedSize,TextFormatFlags flags) {
			if(string.IsNullOrEmpty(text)) {
				return System.Drawing.Size.Empty;
			}
			Graphics g=GetMeasureGraphics();
			if(g!=null && font!=null) {
				try {
					lock(g) {
						SizeF sizeF=g.MeasureString(text,font,proposedSize.Width==int.MaxValue?int.MaxValue:proposedSize.Width);
						//WinForms TextRenderer adds ~1px padding per side vs Graphics.MeasureString.
						return new System.Drawing.Size((int)Math.Ceiling(sizeF.Width)+2,(int)Math.Ceiling(sizeF.Height));
					}
				}
				catch { }
			}
			float emSize=8.25f;
			try { emSize=font?.Size ?? 8.25f; } catch { }
			string[] lines=text.Split('\n');
			int width=(int)Math.Ceiling(lines.Max(x => x.Length)*emSize*0.62f);
			int height=(int)Math.Ceiling(lines.Length*emSize*1.6f);
			return new System.Drawing.Size(width,height);
		}
	}
}

namespace OpenDental.UI {
	//GridRow intentionally not stubbed: the real OpenDentBusiness/UI/GridRow.cs compiles cleanly.

	public class GridColumn {
		public GridColumn() { }
		public GridColumn(string heading,int colWidth) { Heading=heading; ColWidth=colWidth; }
		public GridColumn(string heading,int colWidth,System.Windows.Forms.HorizontalAlignment textAlign) {
			Heading=heading; ColWidth=colWidth; TextAlign=textAlign;
		}
		public string Heading { get; set; }
		public int ColWidth { get; set; }
		public System.Windows.Forms.HorizontalAlignment TextAlign { get; set; }
		public bool IsWidthDynamic { get; set; }
	}

	///<summary>Mirrors OpenDental.UI.GridSheetRow (defined in the excluded GridOD.cs).</summary>
	public class GridSheetRow {
		public int YPos;
		public bool IsTitleRow;
		public bool IsHeaderRow;
		public bool IsBottomRow;
		public bool IsFooterRow;

		public GridSheetRow() { }

		///<summary>Matches the real ComputeSheetSupplemental call:
		///new GridSheetRow(yPosCur,drawTitle,drawHeader,false,drawFooter).</summary>
		public GridSheetRow(int yPos,bool isTitleRow,bool isHeaderRow,bool isBottomRow,bool isFooterRow) {
			YPos=yPos;
			IsTitleRow=isTitleRow;
			IsHeaderRow=isHeaderRow;
			IsBottomRow=isBottomRow;
			IsFooterRow=isFooterRow;
		}
	}

	///<summary>Headless port of OpenDental.UI.GridOD's sheet-grid path. Enough of the real grid to
	///lay out and draw a GridSheet field (e.g. a Statement's account-activity table) into a PDF.
	///Only the XGraphics (PDF) drawing path is implemented; the raster Graphics variants stay no-op
	///because SheetDrawingJob draws grids through gx!=null. Column XPos/Width, per-row HeightMain,
	///and ListGridSheetRows are computed in EndUpdate() so SheetDrawingJob's post-EndUpdate loop
	///(which indexes ListGridSheetRows[i] and reads ListGridRows[i].State.HeightMain) works.</summary>
	public class GridOD : System.Windows.Forms.Control {
		public List<GridColumn> Columns { get; } = new List<GridColumn>();
		public List<GridRow> ListGridRows { get; } = new List<GridRow>();
		public List<GridSheetRow> ListGridSheetRows=new List<GridSheetRow>();
		public string Title { get; set; } = "";
		public bool TitleVisible { get; set; }
		public string TranslationName { get; set; } = "";
		public bool VScrollVisible { get; set; }
		public bool IsForSheets;
		public int SheetBottomMargin;
		public int SheetPageHeight;
		public int SheetPrintHeight;
		public int SheetTopMargin;
		public int SheetYPos;
		public Font FontForSheets { get; set; }
		//Title/header heights MUST match SheetDrawingJob's _yAdjCurRow bumps (heightGridTitle=18, +15 for header).
		private int _heightTitle=18;
		private int _heightHeader=15;
		private int _heightTotal;
		//Effective column left edge (XPos) and width, in 100dpi pixels, parallel to Columns.
		private List<int> _colXPos=new List<int>();
		private List<int> _colWidth=new List<int>();
		//Reused GDI graphics for text measurement (96dpi, matching GraphicsHelper.DrawStringX).
		private static Graphics _graphicsMeasure;
		private static bool _triedMeasure;

		public int GetIdealWidth() => 0;
		public void BeginUpdate() { }

		private string SheetFontName => (FontForSheets!=null && !string.IsNullOrEmpty(FontForSheets.Name)) ? FontForSheets.Name : "Microsoft Sans Serif";
		private double SheetFontSize => FontForSheets!=null ? FontForSheets.Size : 8.5;

		private static Graphics GetMeasureGraphics() {
			if(!_triedMeasure) {
				_triedMeasure=true;
				try {
					Bitmap bitmap=new Bitmap(100,100);
					bitmap.SetResolution(96,96);
					_graphicsMeasure=Graphics.FromImage(bitmap);
				}
				catch(Exception ex) {
					Console.Error.WriteLine("[GridOD] GDI unavailable, using approximate row heights: "+ex.Message);
				}
			}
			return _graphicsMeasure;
		}

		///<summary>Mirrors GridOD.LineSpacingForFont (matches RichTextBox spacing).</summary>
		private static float LineSpacingForFont(string fontName) {
			string n=(fontName??"").ToLower();
			if(n=="arial" || n=="courier new") {
				return 1.055f;
			}
			return 1.05f;
		}

		///<summary>Computes ListGridSheetRows and each row's HeightMain, plus column layout.
		///Called by SheetDrawingJob after all columns/rows are added.</summary>
		public void EndUpdate() {
			ComputeColumns();
			ComputeRowHeights();
			if(IsForSheets) {
				ComputeSheetSupplemental();
			}
		}

		///<summary>Ported (simplified) from GridOD.ComputeColumns. For sheets there is no scaling
		///(scaleMS=zoom=1) and no scrollbar. Honors IsWidthDynamic by splitting the leftover Width
		///evenly among dynamic columns (the real grid weights by DynamicWeight, which the shim
		///GridColumn does not carry). Fixed columns keep their ColWidth. No last-column auto-resize
		///because IsForSheets is true.</summary>
		private void ComputeColumns() {
			_colXPos=new List<int>();
			_colWidth=new List<int>();
			int widthFixedSum=0;
			int countDynamic=0;
			for(int i=0;i<Columns.Count;i++) {
				if(Columns[i].IsWidthDynamic) {
					countDynamic++;
				}
				else {
					widthFixedSum+=Columns[i].ColWidth;
				}
			}
			int widthDynamicEach=0;
			if(countDynamic>0) {
				int widthExtra=Width-2-widthFixedSum;
				if(widthExtra<0) {
					widthExtra=0;
				}
				widthDynamicEach=widthExtra/countDynamic;
			}
			int xPos=0;
			for(int i=0;i<Columns.Count;i++) {
				int w=Columns[i].IsWidthDynamic ? widthDynamicEach : Columns[i].ColWidth;
				if(w<0) {
					w=0;
				}
				_colXPos.Add(xPos);
				_colWidth.Add(w);
				xPos+=w;
			}
		}

		///<summary>Ported (simplified) from GridOD.ComputeRowHeightOne for the WrapText, non-editable,
		///IsForSheets case. Measures the tallest wrapped cell in each row against its column width and
		///sets State.HeightMain. Notes are not used by statement grids, so HeightNote stays 0.</summary>
		private void ComputeRowHeights() {
			_heightTotal=0;
			Graphics g=GetMeasureGraphics();
			Font fontNormal=null;
			Font fontBold=null;
			int fontHeightFallback=(int)Math.Ceiling(SheetFontSize*1.6)+2;
			try {
				fontNormal=new Font(SheetFontName,(float)SheetFontSize,FontStyle.Regular);
				fontBold=new Font(SheetFontName,(float)SheetFontSize,FontStyle.Bold);
				fontHeightFallback=fontNormal.Height;
			}
			catch { }
			StringFormat stringFormat=new StringFormat();
			for(int i=0;i<ListGridRows.Count;i++) {
				GridRow row=ListGridRows[i];
				//Determine measuring font: bold if the row is bold or any cell is bold (bold affects wrap width, hence height).
				Font font=fontNormal;
				if(row.Bold) {
					font=fontBold;
				}
				else {
					for(int j=0;j<row.Cells.Count;j++) {
						if(row.Cells[j].Bold==OpenDentBusiness.YN.Yes) {
							font=fontBold;
							break;
						}
					}
				}
				int heightMain=0;
				if(g!=null && font!=null) {
					for(int j=0;j<row.Cells.Count;j++) {
						int colW=(j<_colWidth.Count) ? _colWidth[j] : 20;
						if(colW<1) {
							colW=1;
						}
						float hTemp;
						try {
							lock(g) {
								hTemp=g.MeasureString(row.Cells[j].Text ?? "",font,colW,stringFormat).Height;
							}
						}
						catch {
							hTemp=fontHeightFallback;
						}
						hTemp=LineSpacingForFont(font.Name)*hTemp;//IsForSheets
						int heightCell=(int)Math.Floor(hTemp);
						if(heightCell>heightMain) {
							heightMain=heightCell;
						}
					}
				}
				if(heightMain<4) {
					heightMain=fontHeightFallback;
				}
				row.State.HeightMain=heightMain;
				row.State.HeightNote=0;
				_heightTotal+=heightMain;
			}
			fontNormal?.Dispose();
			fontBold?.Dispose();
			stringFormat.Dispose();
		}

		///<summary>Faithful port of GridOD.ComputeSheetSupplemental. Fills ListGridSheetRows (same
		///length as ListGridRows) with YPos/title/header/footer/page-break flags and sets
		///SheetPrintHeight. The Statement-specific account-splitting and page-break cases are ported
		///because a statement grid is the primary reason this exists.</summary>
		private void ComputeSheetSupplemental() {
			ListGridSheetRows=new List<GridSheetRow>();
			string _title=Title ?? "";
			bool drawTitle=false;
			bool drawHeader=true;
			bool drawFooter=false;
			int yPosCur=SheetYPos;
			int bottomCurPage=SheetPageHeight-SheetBottomMargin;
			while(yPosCur>bottomCurPage) {//advance pages until using correct y values (grid may start on a later page).
				bottomCurPage+=SheetPageHeight-(SheetTopMargin+SheetBottomMargin);
			}
			for(int i=0;i<ListGridRows.Count;i++) {
				#region Split patient accounts on Statement grids.
				if(i==0
					&& (_title.StartsWith("TreatPlanBenefitsFamily")
					|| _title.StartsWith("TreatPlanBenefitsIndividual")
					|| _title.StartsWith("StatementPayPlanOld")
					|| _title.StartsWith("StatementPayPlan")
					|| _title.StartsWith("StatementDynamicPayPlan")
					|| _title.StartsWith("StatementPayPlanGrid")
					|| _title.StartsWith("StatementInvoicePayment")
					|| _title.StartsWith("StatementMain.NotIntermingled")))
				{
					drawTitle=true;
				}
				else if(_title.StartsWith("StatementMain.NotIntermingled")
					&& i>0
					&& (ListGridRows[i].Tag?.ToString())!=(ListGridRows[i-1].Tag?.ToString()))//Tag should be PatNum
				{
					yPosCur+=20;//space out grids.
					ListGridSheetRows[i-1].IsBottomRow=true;
					drawTitle=true;
					drawHeader=true;
				}
				#endregion
				#region Page break logic
				if(i==ListGridRows.Count-1
					&& (_title.StartsWith("StatementPayPlanOld")
						|| _title.StartsWith("StatementPayPlan")
						|| _title.StartsWith("StatementDynamicPayPlan")
						|| _title.StartsWith("StatementPayPlanGrid")
						|| _title.StartsWith("StatementInvoicePayment")))
				{
					drawFooter=true;
				}
				if(yPosCur //start position of row
					+ListGridRows[i].State.HeightMain //+row height
					+(drawTitle?_heightTitle:0) //+title height if needed
					+(drawHeader?_heightHeader:0) //+header height if needed
					+(drawFooter?_heightTitle:0) //+footer height if needed.
					>=bottomCurPage)
				{
					if(i>0) {
						ListGridSheetRows[i-1].IsBottomRow=true;//this row causes a page break. Previous row should be a bottom row.
					}
					yPosCur=bottomCurPage+1;
					bottomCurPage+=SheetPageHeight-(SheetTopMargin+SheetBottomMargin);
					drawHeader=true;
				}
				#endregion
				ListGridSheetRows.Add(new GridSheetRow(yPosCur,drawTitle,drawHeader,false,drawFooter));
				yPosCur+=(drawTitle?_heightTitle:0);
				yPosCur+=(drawHeader?_heightHeader:0);
				yPosCur+=ListGridRows[i].State.HeightMain;
				yPosCur+=(drawFooter?_heightTitle:0);
				drawTitle=drawHeader=drawFooter=false;//reset all flags for next row.
				if(i==ListGridRows.Count-1) {//set print height equal to the bottom of the last row.
					ListGridSheetRows[i].IsBottomRow=true;
					SheetPrintHeight=yPosCur-SheetYPos;
				}
			}
		}

		///<summary>Converts our 100dpi pixels to PdfSharp points (mirrors GridOD.ToPoints/p).</summary>
		private static double P(double pixels) {
			return XUnit.FromInch(pixels/100d).Point;
		}

		//Raster (WinForms Graphics) variants stay no-op: SheetDrawingJob draws grids via gx!=null (PDF path).
		public void SheetDrawRow(int rowI,Graphics g,int x=0,int y=0,bool isBottom=false,bool isSheetGrid=false,bool isPrintingSheet=false,float scaleMS=1) { }
		public void SheetDrawTitle(Graphics g,int x,int y,float scaleMS) { }
		public void SheetDrawHeader(Graphics g,int x,int y,float scaleMS) { }

		///<summary>Ported from GridOD.SheetDrawTitleX. Draws the grid Title bar (bold, centered across
		///Width) with a top/left/right outline. For StatementMain grids SheetDrawingJob draws the
		///patient-name title itself, so this is mainly used by generic report grids.</summary>
		public void SheetDrawTitleX(XGraphics g,int x,int y) {
			g.DrawRectangle(new OpenDentBusiness.XSolidBrush(Color.FromArgb(230,230,235)),P(x),P(y),P(Width),P(_heightTitle));
			XFont fontTitle=new XFont(SheetFontName,10,OpenDentBusiness.XFontStyle.Bold);
			OpenDentBusiness.GraphicsHelper.DrawStringX(g,Title ?? "",fontTitle,XBrushes.Black,
				new RectangleF(x,y+2,Width,_heightTitle),System.Windows.Forms.HorizontalAlignment.Center);
			XPen penOutline=new OpenDentBusiness.XPen(Color.Black);
			g.DrawLine(penOutline,P(x),P(y+_heightTitle),P(x),P(y));//left
			g.DrawLine(penOutline,P(x),P(y),P(x+Width),P(y));//top
			g.DrawLine(penOutline,P(x+Width),P(y),P(x+Width),P(y+_heightTitle));//right
		}

		///<summary>Ported from GridOD.SheetDrawHeaderX. Draws the column-header band: gray background,
		///each column heading centered in its box, vertical separators, and a full outline.</summary>
		public void SheetDrawHeaderX(XGraphics g,int x,int y) {
			Color cOutline=Color.Black;
			g.DrawRectangle(new OpenDentBusiness.XSolidBrush(Color.LightGray),P(x),P(y),P(Width),P(_heightHeader));//background
			g.DrawLine(new OpenDentBusiness.XPen(Color.FromArgb(102,102,122)),P(x),P(y),P(x+Width),P(y));//line above headers
			XFont fontHeader=new XFont(SheetFontName,SheetFontSize,OpenDentBusiness.XFontStyle.Bold);
			for(int i=0;i<Columns.Count;i++) {
				int colX=x+(i<_colXPos.Count ? _colXPos[i] : 0);
				int colW=(i<_colWidth.Count ? _colWidth[i] : 0);
				if(i!=0) {
					g.DrawLine(new OpenDentBusiness.XPen(cOutline),P(colX),P(y),P(colX),P(y+_heightHeader));//vertical separator
				}
				OpenDentBusiness.GraphicsHelper.DrawStringX(g,Columns[i].Heading ?? "",fontHeader,XBrushes.Black,
					new RectangleF(colX,y-3,colW,_heightHeader),System.Windows.Forms.HorizontalAlignment.Center);
			}
			XPen penOutline=new OpenDentBusiness.XPen(cOutline);
			g.DrawRectangle(penOutline,P(x),P(y),P(Width),P(_heightHeader));
			g.DrawLine(penOutline,P(x),P(y+_heightHeader),P(x+Width),P(y+_heightHeader));//bottom rule
		}

		///<summary>Ported from GridOD.SheetDrawRowX (PDF path). Draws row rowI: each cell's text in its
		///column box at the column's alignment, thin gridlines between columns and under the row, the
		///grid's left/right outline, and a heavier bottom border on isBottom rows. Honors per-row/per-cell
		///Bold, ColorText, and ColorLborder.</summary>
		public void SheetDrawRowX(int rowI,XGraphics g,int x=0,int y=0,bool isBottom=false,bool isSheetGrid=false) {
			if(rowI<0 || rowI>=ListGridRows.Count) {
				return;
			}
			GridRow row=ListGridRows[rowI];
			int heightMain=row.State.HeightMain;
			XFont fontNormal=new XFont(SheetFontName,SheetFontSize);
			XFont fontBold=new XFont(SheetFontName,SheetFontSize,OpenDentBusiness.XFontStyle.Bold);
			XPen gridPen=new OpenDentBusiness.XPen(Color.FromArgb(180,180,180));//light interior gridlines
			//lower horizontal line: heavier at bottom of grid, or the row's custom lower-border color.
			XPen penLower=gridPen;
			if(rowI==ListGridRows.Count-1) {
				penLower=new OpenDentBusiness.XPen(Color.FromArgb(120,120,120));
			}
			else if(row.ColorLborder!=Color.Empty) {
				penLower=new OpenDentBusiness.XPen(row.ColorLborder);
			}
			for(int i=0;i<Columns.Count;i++) {
				int colX=x+(i<_colXPos.Count ? _colXPos[i] : 0);
				int colW=(i<_colWidth.Count ? _colWidth[i] : 0);
				int colRight=colX+colW;
				//right vertical gridline
				g.DrawLine(gridPen,P(colRight),P(y+1),P(colRight),P(y+1+heightMain));
				//lower horizontal gridline
				g.DrawLine(penLower,P(colX),P(y+1+heightMain),P(colRight),P(y+1+heightMain));
				//cell text
				if(i>=row.Cells.Count) {
					continue;
				}
				System.Windows.Forms.HorizontalAlignment align=Columns[i].TextAlign;
				XFont font;
				if(row.Cells[i].Bold==OpenDentBusiness.YN.Yes) {
					font=fontBold;
				}
				else if(row.Cells[i].Bold==OpenDentBusiness.YN.No) {
					font=fontNormal;
				}
				else {//unknown: use row bold
					font=row.Bold ? fontBold : fontNormal;
				}
				Color colorText=(row.Cells[i].ColorText!=Color.Empty) ? row.Cells[i].ColorText : row.ColorText;
				//1px horizontal padding inside the cell box so text does not touch the gridlines.
				RectangleF rect=new RectangleF(colX+1,y,Math.Max(colW-2,1),heightMain);
				OpenDentBusiness.GraphicsHelper.DrawStringX(g,row.Cells[i].Text ?? "",font,new OpenDentBusiness.XSolidBrush(colorText),rect,align);
			}
			//left/right outline of the grid, plus bottom line on bottom rows (mirrors real isSheetGrid path).
			XPen penOutline=new OpenDentBusiness.XPen(Color.Black);
			g.DrawLine(penOutline,P(x),P(y),P(x),P(y+heightMain+1));//left
			g.DrawLine(penOutline,P(x+Width),P(y),P(x+Width),P(y+heightMain+1));//right
			if(isBottom) {
				g.DrawLine(penOutline,P(x),P(y+heightMain+1),P(x+Width),P(y+heightMain+1));//bottom
			}
		}
	}

	public class GridOld : System.Windows.Forms.Control {
		public int GetIdealWidth() => 0;
	}

	///<summary>Headless signature box. Decrypts and renders Open Dental's standard
	///point-stream signatures (AES-128, MD5(keyData) key, "x,y;x,y" points, pen lift
	///encoded as "0,0" — mirrors OpenDentBusiness.UI.SignatureBox). Topaz-hardware
	///signatures (SigPlus proprietary encoding) cannot be decoded here and render
	///as invalid.</summary>
	public class SignatureBoxWrapper : System.Windows.Forms.Control {
		public enum SigMode {
			Default=0, TreatPlan=1, DepositSlip=2
		}
		private List<PointF> _listPointFs=new List<PointF>();
		private bool _isValid;
		public SigMode SignatureMode { get; set; }
		public bool GetSigChanged() => false;
		public bool IsValid => _isValid;
		public bool GetIsTypedFromWebForms() => false;
		public int GetNumberOfTabletPoints(bool refreshFirst=false) => _listPointFs.Count;
		public string GetSignature(string keyData) => "";
		public void ClearSignature(bool clearTopazTablet=true) { _listPointFs.Clear(); _isValid=false; }

		public void FillSignature(bool sigIsTopaz,string keyData,string signature) {
			_listPointFs.Clear();
			_isValid=false;
			if(sigIsTopaz || string.IsNullOrEmpty(signature)) {
				return;//Topaz encoding is proprietary to the SigPlus hardware SDK.
			}
			foreach(string keyDataTry in new[] { keyData.Replace("\r\n","\n"),keyData,keyData.Replace("\r\n","\n").Replace("\n","\r\n") }) {
				try {
					string rawString=DecryptSigString(keyDataTry,signature);
					List<PointF> listPointFs=ParsePoints(rawString);
					if(listPointFs.Count>0) {
						_listPointFs=listPointFs;
						_isValid=true;
						return;
					}
				}
				catch { }//wrong key variant or corrupt padding; try the next newline convention
			}
		}

		private static string DecryptSigString(string keyData,string sigString) {
			byte[] key=ODCrypt.MD5.Hash(System.Text.Encoding.UTF8.GetBytes(keyData));
			byte[] bytesEncrypted=Convert.FromBase64String(sigString);
			using var aes=System.Security.Cryptography.Aes.Create();
			aes.KeySize=128;
			aes.Key=key;
			aes.IV=new byte[16];
			using var memoryStream=new System.IO.MemoryStream();
			using(var cryptoStream=new System.Security.Cryptography.CryptoStream(memoryStream,aes.CreateDecryptor(),System.Security.Cryptography.CryptoStreamMode.Write)) {
				cryptoStream.Write(bytesEncrypted,0,bytesEncrypted.Length);
				cryptoStream.FlushFinalBlock();
			}
			return System.Text.Encoding.UTF8.GetString(memoryStream.ToArray());
		}

		private static List<PointF> ParsePoints(string rawString) {
			List<PointF> listPointFs=new List<PointF>();
			foreach(string pointStr in rawString.Split(';')) {
				string[] arrayXY=pointStr.Split(',');
				if(arrayXY.Length!=2) {
					return new List<PointF>();//not a point stream (e.g. "digital" text signature)
				}
				if(!float.TryParse(arrayXY[0],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float x)
					|| !float.TryParse(arrayXY[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float y)) {
					return new List<PointF>();
				}
				if(x==int.MinValue) {
					return new List<PointF>();//digital-text signature marker; not drawable points
				}
				listPointFs.Add(new PointF(x,y));
			}
			return listPointFs;
		}

		///<summary>Renders the signature to a bitmap, mirroring SignatureBox: lines between
		///consecutive points, skipping segments that touch the (0,0) pen-lift sentinel.</summary>
		public System.Drawing.Bitmap GetSigImage() {
			int width=Math.Max(Width,1);
			int height=Math.Max(Height,1);
			var bitmap=new System.Drawing.Bitmap(width,height);
			using var g=Graphics.FromImage(bitmap);
			g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
			g.Clear(Color.White);
			if(_isValid) {
				using var pen=new System.Drawing.Pen(Color.Black,2f);
				for(int i=1;i<_listPointFs.Count;i++) {
					if(_listPointFs[i-1].X==0 && _listPointFs[i-1].Y==0) {
						continue;
					}
					if(_listPointFs[i].X==0 && _listPointFs[i].Y==0) {
						continue;
					}
					g.DrawLine(pen,_listPointFs[i-1],_listPointFs[i]);
				}
			}
			return bitmap;
		}
	}
}

namespace OpenDentBusiness.UI {
	///<summary>Same stub as OpenDental.UI.SignatureBoxWrapper for files importing this namespace.</summary>
	public class SignatureBoxWrapper : OpenDental.UI.SignatureBoxWrapper {
	}
}

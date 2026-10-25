/*
 * Account statements (FormStatementOptions + the statement path).
 * A statement is the printable account-activity/balance summary for a patient's
 * account (guarantor family, or a single patient).  Generation mirrors the desktop
 * dialog's single-account flow: Statements.GenerateStatement assembles the Statement
 * (PatNum from the guarantor unless SinglePatient, date range, BalTotal/InsEst from
 * the guarantor) exactly as FormStatementOptions does, then Statements.Insert persists
 * it (the form's SaveToDb insert branch).
 *
 * Rendering reproduces FormStatementOptions.CreatePdfForSheet's sheet path without the
 * A-to-Z document archival: GetStatementSheetDef -> AccountModules.GetAccount (the
 * statement DataSet) -> SheetUtil.CreateSheet -> attach the "Statement" SheetParameter
 * -> SheetFiller.FillFields -> SheetUtil.CalculateHeights -> SheetPrinting.CreatePdf,
 * the same PDFsharp CreatePdf the consent sheet endpoint uses (works headless on Linux).
 */
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class BillingStatementsModule {
	//Options the single-account "Generate statement" form exposes; every field optional.
	record StatementReqDto(string DateFrom,string DateTo,bool? Intermingled,bool? SinglePatient);

	static object ToPayload(Statement statement) {
		return new {
			statementNum=statement.StatementNum,
			dateSent=statement.DateSent.Year>1880?statement.DateSent.ToString("yyyy-MM-dd"):"",
			balanceTotal=statement.BalTotal,
			insEst=statement.InsEst,
			mode=statement.Mode_.ToString(),
			isSent=statement.IsSent,
			isReceipt=statement.IsReceipt,
			isInvoice=statement.IsInvoice,
			singlePatient=statement.SinglePatient,
			intermingled=statement.Intermingled,
		};
	}

	public static void Map(WebApplication app) {
		//Prior statements for the patient, newest first — the rows the account's
		//Statement history shows.  Statements.GetStatementsForApi filters by PatNum.
		app.MapGet("/api/patients/{patNum:long}/statements",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<Statement> listStatements=Statements.GetStatementsForApi(1000,0,patNum);
			return Results.Json(new {
				patNum=pat.PatNum,
				patient=pat.GetNameLF(),
				statements=listStatements
					.OrderByDescending(x => x.StatementNum)
					.Select(ToPayload),
			});
		});

		//Generate + persist a statement for a single account, the way FormStatementOptions
		//does on a new single statement: GenerateStatement assembles it (guarantor PatNum,
		//date range, BalTotal/InsEst), then Statements.Insert (SaveToDb's insert branch).
		app.MapPost("/api/patients/{patNum:long}/statements",(long patNum,StatementReqDto dto) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			DateTime dateStart=DateTime.MinValue;
			if(!string.IsNullOrWhiteSpace(dto?.DateFrom)) {
				if(!DateTime.TryParse(dto.DateFrom,out dateStart)) {
					return Results.Text("Invalid from date",statusCode:400);
				}
			}
			DateTime dateEnd=DateTime.MinValue;
			if(!string.IsNullOrWhiteSpace(dto?.DateTo)) {
				if(!DateTime.TryParse(dto.DateTo,out dateEnd)) {
					return Results.Text("Invalid to date",statusCode:400);
				}
			}
			bool isSinglePatient=dto?.SinglePatient??false;
			//StatementMode.InPerson: printed/previewed statement (not Email/Electronic),
			//the same substitution the dialog makes before archiving a sheet.
			Statement statement=Statements.GenerateStatement(pat,dateStart,dateEnd,StatementMode.InPerson,isSinglePatient);
			if(dto?.Intermingled!=null) {
				//Intermingled and SinglePatient are mutually exclusive in the dialog.
				statement.Intermingled=dto.Intermingled.Value && !isSinglePatient;
			}
			statement.StatementNum=Statements.Insert(statement);
			Signalods.SetInvalid(InvalidType.BillingList);
			return Results.Json(new {
				statementNum=statement.StatementNum,
				balanceTotal=statement.BalTotal,
			});
		});

		//Render the statement to PDF through the sheet framework — the deliverable.
		//Mirrors FormStatementOptions.CreatePdfForSheet minus the A-to-Z archival:
		//statement sheet def -> account DataSet -> sheet with the "Statement" parameter
		//-> fill/height -> SheetPrinting.CreatePdf (PDFsharp, headless on Linux).
		app.MapGet("/api/statements/{statementNum:long}/pdf",(long statementNum) => {
			Statement statement=Statements.GetStatement(statementNum);
			if(statement==null || statement.StatementNum==0) {
				return Results.NotFound();
			}
			Patient pat=Patients.GetPat(statement.PatNum);
			if(pat==null) {
				return Results.NotFound();
			}
			SheetDef sheetDef=SheetUtil.GetStatementSheetDef(statement);
			long patNumAccount=Statements.GetPatNumForGetAccount(statement);
			DataSet dataSet=AccountModules.GetAccount(patNumAccount,statement,
				doIncludePatLName:false,doShowHiddenPaySplits:statement.IsReceipt,doExcludeTxfrs:false);
			Sheet sheet=SheetUtil.CreateSheet(sheetDef,statement.PatNum,statement.HidePayment);
			sheet.Parameters.Add(new SheetParameter(true,"Statement") { ParamValue=statement });
			SheetFiller.FillFields(sheet,dataSet,statement);
			SheetUtil.CalculateHeights(sheet,dataSet,statement);
			string pdfPath=Path.Combine(Path.GetTempPath(),$"odstatement_{statementNum}.pdf");
			//Business SheetPrinting.CreatePdf(sheet,fullFileName,stmt,dataSet,medLab,pat).
			//Statement sheets contain a GridSheet (account activity), now rendered headlessly
			//by the ported GridOD shim (OpenDentBusinessCore/Shims/WidgetStubs.cs). The
			//try/catch stays as a safety net for any unusual sheet the grid port doesn't cover,
			//degrading to a clean 501 rather than a 500.
			try {
				SheetPrinting.CreatePdf(sheet,pdfPath,statement,dataSet,null,pat);
			}
			catch(Exception ex) {
				return Results.Text("Statement PDF rendering is not yet supported headlessly "
					+"(sheet-grid drawing is stubbed in the Linux core): "+ex.Message,statusCode:501);
			}
			return Results.File(File.ReadAllBytes(pdfPath),"application/pdf");
		});

		//Delete a statement (and any archived image), the dialog's Delete button.
		app.MapDelete("/api/statements/{statementNum:long}",(long statementNum) => {
			Statement statement=Statements.GetStatement(statementNum);
			if(statement==null || statement.StatementNum==0) {
				return Results.NotFound();
			}
			Statements.DeleteStatements(new List<Statement> { statement });
			Signalods.SetInvalid(InvalidType.BillingList);
			return Results.Ok();
		});
	}
}

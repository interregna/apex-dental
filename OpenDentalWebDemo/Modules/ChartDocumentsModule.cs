/*
 * Chart Documents module: the web replacement for the Imaging module's document
 * tree management (FormImageSelectCategory + the doc-info edit panel of
 * ControlImageDisplay). Browses a patient's documents grouped by image category
 * (DefCat.ImageCats), edits a document's category/description/date, and deletes a
 * document. Everything runs through the real business layer:
 *   - Defs.GetDefsForCategory(DefCat.ImageCats) for the category list,
 *   - Documents.GetPatientData / GetByNum / Update for browse + metadata edits,
 *   - ImageStore.DeleteDocuments (file + row + related rows) for deletes,
 * mirroring the desktop's SecurityLogs stamps (EnumPermType.ImageEdit / ImageDelete).
 *
 * The flat GET /api/patients/{n}/documents and GET /api/documents/{n}/file endpoints
 * already live in Program.cs; the file endpoint is REUSED by the front-end (view link)
 * and is intentionally NOT re-registered here. This module adds the category-grouped
 * tree plus the metadata PUT and DELETE.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using OpenDentBusiness;

namespace OpenDentalWebDemo.Modules;

public static class ChartDocumentsModule {
	record UpdateDocDto(long DocCategory,string Description,string DateCreated);

	public static void Map(WebApplication app) {
		//DefCat.ImageCats picker: the same categories the desktop's category tree shows.
		//Defs are already cached/refreshed at startup; GetDefsForCategory reads the cache
		//(no raw SQL). isShort=false so hidden categories are included for completeness of
		//the tree, but we drop hidden ones from the edit dropdown below.
		app.MapGet("/api/image-categories",() => {
			List<Def> listDefs=Defs.GetDefsForCategory(DefCat.ImageCats,false);
			return Results.Json(listDefs.Select(x => new {
				defNum=x.DefNum,
				name=x.ItemName,
				isHidden=x.IsHidden,
			}));
		});

		//The patient's documents grouped by image category — the tree FormImageSelectCategory
		//and the chart's category nodes present. Uses Documents.GetPatientData (real business
		//call, no raw SQL) and groups in memory by DocCategory against the ImageCats defs.
		app.MapGet("/api/patients/{patNum:long}/documents-tree",(long patNum) => {
			Patient pat=Patients.GetPat(patNum);
			if(pat==null) {
				return Results.NotFound();
			}
			List<Def> listDefs=Defs.GetDefsForCategory(DefCat.ImageCats,false);
			List<Document> listDocs=Documents.GetPatientData(patNum);
			List<object> groups=new();
			//One group per image category (in the category's item order), each carrying its docs
			//newest-first, matching the desktop's DateCreated-descending presentation.
			foreach(Def def in listDefs) {
				List<object> docs=listDocs
					.Where(d => d.DocCategory==def.DefNum)
					.OrderByDescending(d => d.DateCreated)
					.Select(DocJson).ToList();
				groups.Add(new {
					catDefNum=def.DefNum,
					catName=def.ItemName,
					isHidden=def.IsHidden,
					docs,
				});
			}
			//Catch-all for documents whose DocCategory no longer maps to a defined category,
			//so nothing is ever invisible/undeletable in the web tree.
			HashSet<long> knownCats=listDefs.Select(x => x.DefNum).ToHashSet();
			List<object> orphans=listDocs
				.Where(d => !knownCats.Contains(d.DocCategory))
				.OrderByDescending(d => d.DateCreated)
				.Select(DocJson).ToList();
			if(orphans.Count>0) {
				groups.Add(new { catDefNum=0L,catName="(Uncategorized)",isHidden=false,docs=orphans });
			}
			return Results.Json(groups);
		});

		//Edit a document's category / description / date, mirroring the doc-info panel of
		//ControlImageDisplay: two-object Documents.Update for the audited diff, then the
		//EnumPermType.ImageEdit SecurityLog the desktop writes ("Document Edited: ...").
		app.MapPut("/api/documents/{docNum:long}",(long docNum,UpdateDocDto dto) => {
			Document doc=Documents.GetByNum(docNum,true);
			if(doc==null) {
				return Results.NotFound();
			}
			Document docOld=doc.Copy();
			doc.DocCategory=dto.DocCategory;
			doc.Description=dto.Description??"";
			if(DateTime.TryParse(dto.DateCreated,out DateTime dateCreated)) {
				doc.DateCreated=dateCreated;
			}
			Documents.Update(doc,docOld);
			Def defCat=Defs.GetDef(DefCat.ImageCats,doc.DocCategory);
			string logText="Document Edited: "+doc.FileName+" with category "+(defCat?.ItemName??doc.DocCategory.ToString());
			SecurityLogs.MakeLogEntry(EnumPermType.ImageEdit,doc.PatNum,logText);
			return Results.Json(new { docNum });
		});

		//Delete a document through the real ImageStore, which removes the underlying file (or
		//the in-DB blob) plus the row and its dependent rows (ImageDraws/EClipboard/Pearl).
		//DetachDocFromStatements first, exactly as the desktop's single-document delete does.
		app.MapDelete("/api/documents/{docNum:long}",(long docNum) => {
			Document doc=Documents.GetByNum(docNum,true);
			if(doc==null) {
				return Results.NotFound();
			}
			Patient pat=Patients.GetPat(doc.PatNum);
			string patFolder=pat==null?"":ImageStore.GetPatientFolder(pat,ImageStore.GetPreferredAtoZpath());
			Def defCat=Defs.GetDef(DefCat.ImageCats,doc.DocCategory);
			Statements.DetachDocFromStatements(docNum);
			try {
				//DeleteDocuments throws if the image is referenced by sheets, or if the file is
				//locked/read-only — surface that message to the caller rather than 500-ing.
				ImageStore.DeleteDocuments(new List<Document>{ doc },patFolder);
			}
			catch(Exception ex) {
				return Results.Text(ex.Message,statusCode:409);
			}
			//ImageStore.DeleteDocuments only writes the ImageDelete log in the LocalAtoZ branch;
			//stamp it here so the audit entry the desktop's Imaging module produces is always
			//present (this demo stores images in-DB, where DeleteDocuments would not log).
			string logText="Document Deleted: "+doc.FileName+" with category "+(defCat?.ItemName??doc.DocCategory.ToString());
			SecurityLogs.MakeLogEntry(EnumPermType.ImageDelete,doc.PatNum,logText);
			return Results.Ok();
		});
	}

	//Shared document-row projection (camelCase, front-end friendly).
	static object DocJson(Document d) => new {
		docNum=d.DocNum,
		fileName=d.FileName,
		description=d.Description,
		dateCreated=d.DateCreated.Year>1880?d.DateCreated.ToString("yyyy-MM-dd"):"",
		imgType=d.ImgType.ToString(),
	};
}

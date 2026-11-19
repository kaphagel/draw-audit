# PROMPT: SHAREPOINT DRAWING HIERARCHY EXTRACTION

REV 13

**Controlled extraction from SharePoint inventory into an Excel summary**

Changes in REV 13: row fill extended to the full hierarchy row (A–W) (section 9); new “Warnings Summary” sheet listing all warnings across Subs (sections 12 and 13).



# 1. Objective and deliverable

Use the attached inventory workbook and Excel summary template to build a traceable drawing-and-part hierarchy for every eligible Sub folder. Extract only supported evidence from the listed Delivery List (DL) and drawing files, write the results into the summary workbook, preserve the template structure, and return the completed Excel workbook as a downloadable file.

Work directly in one copy of the supplied summary workbook. Save progress in that same output file after each completed Sub sheet.

Do not create replacement workbooks between Sub folders unless the supplied workbook is unusable. Do not discard previously completed sheets.

Do not guess. Use “Unclear” when evidence exists but cannot be interpreted reliably. Use “-” where this prompt explicitly requires “-” or where a drawing-specific requirement is absent.

# 2. Authoritative inputs

Root SharePoint folder link shown above.

Attached inventory Excel workbook: relative path in column A and corresponding file URL in column B. Rows 5 and below are the authoritative file inventory. Do not discover or use additional paths. The file submitted for a specific extraction request has to be used for the extraction process. No other inventory file can be used.

Attached Excel summary template: contains the info sheet specifying the version of the template (no action required here except reading the template version in cell B1), the template and the Inventory sheets to be populated. The file submitted for a specific extraction request has to be used for filling data. No other summary template file can be used.

SOURCE ISOLATION RULE: Before starting any extraction, identify the exact inventory workbook and summary workbook attached in the current conversation. All extraction decisions, hierarchy creation, quantity calculation, file opening, revision selection, and Excel updates shall be based exclusively on files referenced by that inventory workbook. Do not use, reuse, recall, or compare with files, drawings, delivery lists, hierarchies, BOMs, workbooks, URLs, or extraction results from previous conversations, previous projects, previous attempts, or memory.

RESTART DETECTION RULE: The words:

- "start”
- "restart"
- shall always be interpreted as a completely new extraction run unless the user explicitly mentions:
- "continue"
- "resume"
- For a new extraction run:
- Ignore every workbook created during any previous extraction attempt.
- Ignore every hierarchy sheet created during any previous extraction attempt.
- Ignore every Inventory sheet created during any previous extraction attempt.
- Open the summary workbook attached in the current conversation.
- Verify that the workbook contains only the original sheets delivered by the user.
- Treat that workbook as the only valid starting point.
- Rebuild the Inventory sheet from the current inventory workbook.
- Recreate all requested Sub sheets from the current template sheet.
- Do not copy rows, hierarchy branches, warnings, quantities, drawing selections, or decisions from any previous extraction.
- Previous extraction results are invalid evidence and shall not be consulted.
- Only when the user explicitly requests a continuation or resume may the currently attached partially-filled summary workbook be reused.
- Typically, when files are attached with the prompt, that means a new extraction run.
CURRENT-CONVERSATION VALIDATION: If a drawing number, BOM item, delivery list, quantity, hierarchy branch, or workbook content is mentioned in the extraction but cannot be traced to a file listed in the inventory workbook delivered in the current conversation, that information shall be discarded and treated as invalid.

DOCUMENT EVIDENCE VALIDATION

- Every hierarchy item, quantity, BOM relationship, drawing title, revision, weld requirement, NDT requirement, seal weld requirement, ISO 5817 requirement, surface treatment value, and delivery-unit assignment shall be traceable to document content retrieved during the current extraction run.
- If the information cannot be traced to content retrieved during the current extraction run, that information shall be discarded.
- Information obtained from:
- memory,
- previous runs,
- previous conversations,
- previous workbooks,
- filenames only,
- URL strings only,
- inventory entries only
- is invalid evidence.
Source files referenced by the inventory: only PDF, XLSX, XLSM, and XLS files are supported. Ignore all other extensions.

ATTACHED-FILE VERSION VALIDATION: Before any extraction work starts, open the inventory workbook and the summary workbook attached in the current conversation and record:

- Inventory workbook filename
- Summary workbook filename
- Template version from the Info sheet
- Report those values before starting the extraction.
- If these values were not obtained from the currently attached files, the extraction shall not start.
# 3. Scope filtering

Ignore files located directly in the root folder.

At root level, process only folders whose names start with “Sub” or “sub”. Ignore all other root subfolders.

Inside each eligible Sub folder, process only files under folders named “1 DL” or “01 DL” and “3 PDF” or “03 PDF”. Folder-name matching is case-insensitive. Ignore everything else.

Ignore every inventory row whose relative path contains a complete path segment named “Old”, case-insensitive. Do not treat words merely containing the letters “old” as an Old folder.

Use the inventory URLs to open files. The root link defines the project scope, but the inventory rows define the exact files allowed.

# 4. Sub identification and strict isolation

From each eligible root folder name, find the first digit after “Sub” or “sub”. Starting there, take no more than eight characters and stop at the last digit of the identifier. Remove spaces immediately after “Sub”. Examples: “Sub1059 Walkways” → 1059; “Sub1002-2 ...” → 1002-2; “Sub1019-1&2 ...” → 1019-1&2.

Create one hierarchy worksheet per Sub identifier by copying the template sheet. Name the copied sheet exactly with that identifier.

While processing a Sub, use only inventory rows belonging to that exact Sub folder. Accepted starts include “Sub1002-1”, “Sub 1002-1”, “sub1002-1”, and “sub 1002-1”. Do not search another Sub folder to resolve a missing drawing, quantity, BOM item, or requirement.

If the same drawing number appears in another Sub, do not use that other copy. Record the current Sub issue as Unclear or in the issue section, as applicable.

# 5. Initialize the summary Inventory sheet

Copy every non-blank value from column A of the supplied inventory workbook, starting at row 6 and ending at the last non-blank cell, into column A of the summary workbook’s Inventory sheet starting at row 2. Preserve the source order and text exactly.

For each copied inventory row, write in summary Inventory column B: “Yes” if that file is the selected latest relevant revision used for extraction; “No” if it is an older revision, a duplicate format not selected under Step 6, unsupported, outside the permitted DL/PDF folders, or under an Old folder.

Do not delete inventory rows. Column B is the audit trail for selection.

# 6. File grouping and revision selection

Classify files from their relative paths: files under 1 DL/01 DL are Delivery Lists; PDF files under 3 PDF/03 PDF are drawings.

Group revisions by the underlying document identity, not by the full filename. Ignore revision tokens, page tokens such as 1(3), 2(3), and 3(3), language suffixes, and file extension when deciding whether files are revisions or pages of the same document.

Select the latest revision using explicit revision evidence from filename and, when opened, the document title block. Alphabetic sequence increases A, B, C...; numeric sequence increases 00, 01, 02... . Do not compare an alphabetic series with a numeric series unless the document itself establishes the relationship.

If revision order is ambiguous or filename and title block conflict, open the candidates, do not guess, select only when evidence is conclusive, and record the conflict. Mark unresolved candidates “No” in Inventory column B until one can be justified.

For a DL at the selected revision, use the Excel version when readable. Use the PDF only if no readable Excel version exists. If both formats have the same selected revision, mark Excel “Yes” and PDF “No”; the PDF may be used only as a visual consistency check and must not override the Excel without an explicit recorded conflict.

For multi-page drawings stored as separate PDFs, keep every page belonging to the selected revision. Mark all selected pages “Yes”.

# 7. Read and normalize source information

Open each selected DL and drawing through its inventory URL. Review all relevant DL sheets and all selected drawing pages.

DOCUMENT CONTENT ACCESS REQUIREMENT

- A file shall be considered opened only when its actual internal content has been retrieved and reviewed.
- For a PDF drawing this means that at least one of the following has been obtained from the file itself:
- drawing page image(s);
- readable PDF text extracted from the drawing;
- title block content;
- BOM content;
- drawing notes.
- Filename, URL, inventory row, metadata, search result title, search result snippet, or information remembered from previous extractions do not constitute opened content.
- If the actual content of a selected file cannot be retrieved, the file shall be marked: "Content not accessible"
- and no hierarchy, quantity, weld, NDT, seal-weld, ISO 5817 class, revision, page number, title, BOM item, or parent-child relationship may be extracted from that file.
- The extraction shall use only evidence visible inside the retrieved document content.
DRAWING CONTENT VALIDATION: A drawing shall not be considered reviewed unless the actual drawing content

- (title block, BOM, notes, dimensions, symbols and annotations) has been opened and inspected.
- Filename, URL, inventory row, document title, metadata, or partial text extraction are not sufficient evidence that a drawing has been reviewed.
- Before extracting any of the following:
- hierarchy relationships
- quantities
- weld requirements
- NDT requirements
- seal weld requirements
- ISO 5817 class
- the drawing content itself shall have been opened and inspected.
- If only metadata, filename information, inventory information, or incomplete text extraction is available, the drawing shall be recorded as NOT REVIEWED and the extraction for that drawing shall stop.
- The drawing shall not be interpreted using filenames, previous extractions, assumptions, or memory.
For each drawing, capture the title-block drawing number, title, revision, page notation, BOM, and drawing-specific welding/NDT/seal-weld/ISO 5817 information.

Normalize drawing-number matching only for harmless presentation differences such as case, surrounding spaces, and separators. Never merge different numbers because descriptions look similar.

A BOM line that identifies a drawing shall remain a drawing item in the hierarchy, even when no matching drawing file is available in the current Sub. Keep the drawing number at the hierarchy level established by the parent BOM. If no matching selected drawing file exists in the current Sub, enter “Missing” in column I and record a specific issue stating that the drawing file is missing and needs to be added. Do not search another Sub folder or another location for the missing drawing.

A material/BOM-only description must include the available description and specification needed to distinguish it, such as grade, standard, section, thickness, dimensions, or length. Do not invent missing specifications.

# 8. Build the hierarchy

Build parent-child links from explicit BOM references. A child belongs directly below the specific parent drawing whose BOM lists it. Never assign a parent from filename order, drawing-number sequence, visual similarity, or description similarity alone.

Identify level 1 from the DL and drawing network: normally the explicitly delivered top assembly or a selected drawing not referenced as a child by another selected drawing in the same Sub. If more than one independent delivery unit exists, create a separate level-1 branch for each.

A delivery unit has a delivery item number below “delivery item nr” in the DL. Otherwise, it is not a delivery item. Note that it can be some additional text directly in rows below a delivery item. Those are parts of the same delivery unit. Drawings below “Sub drawing” are not delivery units as they don’t have a specific delivery unit number. One of their parents shall be a delivery unit.

Recursively expand each drawing-backed BOM child through its own BOM, to a maximum of five levels. Include BOM-only parts at their correct level, but do not expand them unless a matching selected drawing exists within the same Sub.

Keep each branch contiguous and in parent-first order. List a parent, then all of its children and descendants, before moving to the next sibling. Repeat the same part on separate rows when it occurs under different parents or branches so quantities and traceability remain correct.

If a circular reference, orphan drawing, conflicting parent, or required sixth level is found, do not force the hierarchy. Place the affected item at the highest justified position, mark uncertain fields “Unclear”, and record a specific issue.

Hierarchy level determines the one populated hierarchy column: level 1 → B, level 2 → C, level 3 → D, level 4 → E, level 5 → F. Leave the other hierarchy columns blank on that row.

# 9. Worksheet structure and row colors

Keep rows 1 and 2 unchanged, with row 2 as the hierarchy header. The hierarchy shall always be located between row 3 and the row immediately above the “Warnings / Errors” section. The “Warnings / Errors” section and every row below it form a protected template block. Do not clear or reformat any cell in this protected block.

Before writing hierarchy data, locate the cell containing the exact text “Warnings / Errors” in column A. If the hierarchy requires more rows than are currently available above this cell, insert the required number of complete worksheet rows immediately above the “Warnings / Errors” row. The insertion shall move the entire protected block downward while preserving all text, formulas, fills, borders, fonts, row heights, merged cells, and other formatting exactly as in the copied Template sheet.

The number of available hierarchy rows equals: Warnings / Errors row number - 3

If the hierarchy contains more rows than this available number, insert: Hierarchy row count - available hierarchy rows

rows immediately above the “Warnings / Errors” row. Do not achieve additional space by clearing or overwriting the Warnings / Errors section.


| **Level** | **Hierarchy column** | **RGB fill** |
| --- | --- | --- |
| 1 | B | 221, 235, 247 |
| 2 | C | 226, 239, 218 |
| 3 | D | 255, 242, 204 |
| 4 | E | 252, 213, 180 |
| 5 | F | 230, 184, 183 |

Apply the level’s RGB fill to the entire hierarchy row, from column A through column W, so the level remains identifiable while scrolling. Do not fill any column after W.

The RGB values above are the defaults. They may be overridden in the extraction tool’s configuration file (LevelColors); when overridden, the configured colours apply instead.

# 10. Column rules for each hierarchy row

| **Column** | **Field** | **Rule** |
| --- | --- | --- |
| **A** | No entry | Leave unchanged/blank. |
| **B-F** | Hierarchy item | Populate only the column corresponding to the row level. For a drawing-backed item use its drawing number. For a BOM-only item use the material/part description plus available specification. |
| **G** | Drawing level | Enter 1, 2, 3, 4, or 5. |
| **H** | Page | For a drawing-backed item, enter the title-block/table-block page notation. For separate pages use, for example, “1(3), 2(3), 3(3)”. For a BOM-only item enter “-”. |
| **I** | Drawing revision | Use the item’s own drawing title block to determine the revision. Apply the following values: Enter the stated revision when the drawing is available and its revision is identified. Enter “-” when the drawing is available but no revision is stated. Enter “Missing” when the parent BOM identifies a drawing number but no matching selected drawing file exists in the current Sub. Enter “-” for a genuine material or BOM-only item that has no drawing number. Never inherit a drawing revision from the parent drawing. |
| **J** | Drawing/part name | For a drawing-backed item, use its title-block title and reconcile it with the parent BOM description. If they materially conflict, use the title-block title and record the conflict. Add the drawing number + “ – “ before the title-block name.   For a BOM-only item use the BOM description/specification.  For level 1: no change For next levels, add 5 empty spaces before the value to be added to the cell per level -1. For instance, for a level 4 item, you have to add 5 x (4 – 1) = 15 empty spaces. This will allow seeing structures clearly from this column as well. |
| **K** | Quantity per parent | Use the quantity in the immediate parent BOM. Level 1 defaults to 1 unless the DL explicitly defines another per-delivery-unit basis. Do not use total DL quantity here. |
| **L** | Total quantity requested | Calculate cumulatively for the branch: DL quantity of the delivered ancestor × every quantity-per-parent factor from that ancestor to the current row × number of applicable deliveries/units stated in the DL. Sum only when identical output rows are intentionally consolidated; otherwise keep branch-specific rows. |
| **M** | Part of scope? | “Yes” when the item is explicitly defined as required in the DL; “In parent delivery” when required only through an included ancestor/BOM; “Excluded” when explicitly excluded. Use “Unclear” if scope evidence conflicts. Level 1 is typically not a delivery unit. Leave the cell blank. If an item does not have a delivery unit number in the DL then, it is not a delivery unit. If it belongs to a parent that is a delivery item or is in parent delivery, then state “In parent delivery”. Otherwise, state “Excluded”. A delivery unit has a delivery item number below “delivery item nr” in the DL. Otherwise, it is not a direct delivery item.     Note that it can be some additional text directly in rows below a delivery item. Those are parts of the same delivery unit. Drawings below “Sub drawing” or “Sub drawing list”  are not delivery units as they don’t have a specific delivery unit number. One of their parents shall be a delivery unit. |
| **N** | ITP manufacturing | Use the DL value below “Inspection plan manufacturing:”. Children inherit from the nearest applicable delivered ancestor unless the DL gives a more specific value. |
| **O** | ITP erection | Use the DL value below “Inspection plan Construction:”. Children inherit from the nearest applicable delivered ancestor unless the DL gives a more specific value. |
| **P** | EXC class | Use the DL value below “Exc class if manufacturing and built acc to EN1090:”. Children inherit from the nearest applicable delivered ancestor unless the DL gives a more specific value. |
| **Q** | Painting system | From DL “Surface treatment”, report only the painting-system identifier, such as “2.2” or “K”. Do not include other text. Populate only explicit DL delivery units; children receive “-” unless explicitly specified otherwise. Level 1, since usually not a delivery unit, shall be left empty. Surface treatment is usually specified for level 2 and below. |
| **R** | Surface-treatment extra info | From DL “Surface treatment”, retain only meaningful extra instructions such as “light oil” or different inside/outside treatments. Exclude primer thickness, corrosivity/protection class such as C3h, and standards. Enter “-” if none. Children receive “-” unless explicitly specified otherwise. |
| **S** | Color | Report the stated color, for example “RAL 7035”. Enter “-” if absent. Populate only explicit DL delivery units; children receive “-” unless explicitly specified otherwise. Level 1, since usually not a delivery unit, shall be left empty. Color is usually specified for level 2 and below. |
| **T** | Welds? | From the item’s own drawing only:    - No weld symbol, then state “-”. - Only weld symbols without field flag, then state “Workshop”. - Only weld symbols with field flag, then state “Site”. - if Both types present, then state “Workshop / Site”. Site welds shall be identified only when the welding symbol contains a field weld flag attached to the reference line/arrow junction. The presence of ordinary weld symbols, seal weld notes, weld sizes (a3, a5, etc.), intermittent weld notation (50(150)), or leader lines alone shall not be interpreted as site welding. Do not infer welding from assembly type, BOM, parent drawing, notes on another drawing, or presence of welded geometry. |
| **U** | NDT requirements | From the item’s own drawing only, report explicit NDT requirements such as “NDT 20%”, UT, MT, RT, or PT. Otherwise “-”. Do not inherit. |
| **V** | Seal / gas-tight welds | From the item’s own drawing only, reproduce the explicit requirement for seal weld, gas-tight weld, or equivalent. Otherwise “-”. A normal weld symbol alone is not evidence of a seal/gas-tight requirement. Do not inherit. |
| **W** | ISO 5817 class | From the item’s own drawing only, enter the explicitly stated ISO 5817 quality level/class. Otherwise “-”. Do not infer a class from EXC class, general welding, parent drawing, or another drawing. |

# 11. MANDATORY EXTRACTION SEQUENCE

The following sequence is mandatory and shall be completed in the exact order shown below. No drawing may be opened before the preceding steps have been completed. The steps below are for extracting information for one sub. Same procedure has to be used for all subs. So, for the extraction of the next sub, start with Step 1.

# Step 1 – Identify files belonging to the current Sub

Using the inventory workbook only:

- Filter inventory rows belonging to the current Sub.
- Ignore rows containing an "Old" folder.
- Ignore rows outside the current Sub.
- Create the working file list for the current Sub.

# Step 2 – Determine the latest revision of each file for the specific sub

Before opening any DL or drawing:

- Group files that represent the same document.
  - For files in 1 DL (or alike):
    - Files represent the same document if they contain the sub number, even if the revision or file extension is different.
    - The revision information is at the end of the file name (excluding the extension .pdf or Excel extension). It is typically REVA, REVB … or REV01, REV02. It could also be an empty space between REV or Rev or rev and the revision number / letter.
  - For files in 3 PDF (or alike, the revision information is typically located after the drawing number (either starting with a V followed by 7 digits or 9 straight digits). It may look like that:
    - 703695166_-_ : Revision is -, meaning this is the initial version
    - 703552771_A_ : Revision is A
    - V3745445 - Rev A. Revision is A 
    - V3745445 - Rev 02. Revision is 02 
    - V3745445 _ 02. Revision is 02 
- Determine the latest revision, either based on alphabetical order or numerical order.
- Retain only the latest revision.

Examples:

Example A – Same revision in PDF and Excel

DL_1002-1_CASING DETAILS_REVA.pdf

DL_1002-1_CASING DETAILS_REVA.xlsx

Result:

DL_1002-1_CASING DETAILS_REVA.xlsx = Selected

DL_1002-1_CASING DETAILS_REVA.pdf = Not selected

Reason:

Same revision.

Excel has priority over PDF.


Example B – Multiple revisions

DL_1058_Support_Structure_REVA.xlsx

DL_1058_Support_Structure_REVB.pdf

DL_1058_Support_Structure_REVB.xlsx

DL_1058_Support_Structure_REVD.pdf

DL_1058_Support_Structure_REVD.xlsx

Result:

DL_1058_Support_Structure_REVD.xlsx = Selected

All others = Not selected

Reason:

RevD is latest.

Excel has priority over PDF.


Example C – only one drawing revision

703722601_-_INSULATOR HOUSING_EN.pdf

Result:

DL_1058_Support_Structure_REVD.xlsx = Selected

Reason:

This is the only one drawing, revision “-“.


Example D – multiple drawing revisions

703607262_-_COUPLING_EN

703607262_A_COUPLING_EN

Result:

703607262_A_COUPLING_EN = Selected

703607262_-_COUPLING_EN = Not selected

Reason:

A is a latter revision


Example E – multiple drawing revisions

206 - V3745534 - Rev A.pdf

206 - V3745534 - Rev B.pdf

Result:

206 - V3745534 - Rev B.pdf = Selected

206 - V3745534 - Rev A.pdf = Not selected

Reason:

B is a latter revision


Update the Inventory sheet before continuing but only for the files belonging to the specific sub. In column B, state “Yes” if the file is selected and “No” if it is not selected. 


# Step 3 – Open and analyse the selected DL for the specific sub

Using the URL from the selected inventory row:

- Open the selected DL.
- Extract all DL information required by columns L to S.
- Do not open any drawing before the DL has been analysed.

# Step 4 – Determine the Level 1 drawing(s) for the specific sub

From the selected DL:

- Locate the field "Assembly Dwg".
- Every drawing number listed there becomes a Level 1 item.
- If multiple drawing numbers are listed, create multiple Level 1 branches.

# Step 5 – Build Level 2 for the specific sub

For each Level 1 drawing:

- Locate the latest revision using the inventory.
- Open the drawing using the URL from the selected inventory row.
- Read the BOM.
- Create the Level 2 items directly from the BOM.
- Populate summary-sheet information available from the drawing.

# Step 6 – Build Levels 3, 4 and 5 for the specific sub

For every drawing-backed child:

- Find the latest revision using the inventory.
- Open the drawing using the URL from the selected inventory row.
- Read the BOM.
- Create the next hierarchy level directly from the BOM.
Repeat recursively until:

- No additional drawing-backed children exist, or
- Level 5 is reached.

# Step 7 – Populate the summary sheet during extraction for the specific sub

The summary sheet shall be populated while the hierarchy is being built.


Do not:

- First build the hierarchy and later populate the table.
- First read all drawings and later build the hierarchy.

For each item discovered:

- Add the hierarchy row.
- Fill all information available from the current DL and drawing.
- Then continue to the next child item.

# 12. Validation before completing each Sub

In cell A1, add the DL file name (e.g. DL_1002-1_CASING DETAILS_REVA.xlsx)

Every row belongs to the current Sub and is supported by a selected file or a BOM/DL within that Sub.

Every selected file is marked Yes and every non-selected inventory row is marked No in the summary Inventory sheet.

Only the latest justified revision is used; all pages of a selected multi-page drawing are included.

Each drawing-backed child is linked to a parent that explicitly lists it in its BOM.

Columns A:F, level in G, and row fill agree; the level fill covers columns A through W of the row and ends at W.

All hierarchy branches are contiguous, parent-first, and no level exceeds 5. If a level 6 would have been required, add the warnings in the “Warnings / Errors” section of the sheet.

Column K uses immediate-parent quantity; column L reproduces the full cumulative multiplication and DL delivery quantity.

Columns T:W use only the specific item’s own drawing. No parent-based or similarity-based inference is present.

DL-inherited values are limited to N:P and scope logic in M, plus the explicit Q:S rules.

Every Unclear value and every conflict has a specific, evidence-based note. Do not add generic comments about work not performed outside the current Sub.

Every entry written in the Sub’s “Warnings / Errors” section is also listed on the “Warnings Summary” sheet (section 13).

HIERARCHY VALIDATION RULE: A drawing may only be opened if it is reached through the sequence:

- Inventory
  - → Latest revision selection
  - → Selected DL
  - → Assembly Dwg
  - → Parent BOM
- If a drawing number is not reached through this chain, it shall not be opened and shall not be added to the hierarchy.
COPY VALIDATION RULE: The Sub worksheet shall be created by copying the complete Template worksheet, not by creating a new worksheet or reconstructing its layout. After copying, modifications are permitted only in cell A1, the hierarchy data area above the Warnings / Errors section, and the cells intended for recording actual warnings or errors.

 Confirm that the exact text “Warnings / Errors” is still present in column A and that its complete template formatting is unchanged.

 Confirm that the Warnings / Errors section is located immediately below the final hierarchy row, allowing only the same intentional blank separator rows provided in the Template.

SOURCE FILE CHECK: The workbook being updated shall be the summary workbook attached in the current conversation. It is forbidden to continue extraction in a workbook that was created during a previous extraction attempt, previous conversation, or previous run.

RESTART VALIDATION: Before extracting the first Sub, report:

- Inventory workbook filename
- Summary workbook filename
- Template version
-  Extraction mode = NEW or CONTINUE
NEW: The attached summary workbook must not already contain hierarchy results from a previous extraction.

CONTINUE: The attached summary workbook is expected to contain previously completed Sub sheets and shall be reused.

# 13. Issues, traceability, and completion

Use the template’s existing “Warnings / Errors” area. Record only issues relevant to the current Sub, naming the affected file/drawing and the precise conflict or missing evidence.

WARNINGS SUMMARY SHEET: In addition to the per-Sub “Warnings / Errors” areas, add a worksheet named “Warnings Summary” as the first sheet of the output workbook. It lists every warning from every Sub sheet, one row per warning, in Sub order, with these columns:

| **Column** | **Field** | **Content** |
| --- | --- | --- |
| **A** | # | Running number. |
| **B** | Sub | Sub identifier, equal to the Sub sheet name. |
| **C** | Sub folder | Root folder name of the Sub from the inventory relative path. |
| **D** | Issue type | Category, e.g. “Missing drawing”, “Material conflict”, “Level 6 required”, “Level 1 drawing not found”. |
| **E** | Affected item | Drawing number or material description of the hierarchy item the issue concerns. |
| **F** | Generated by (drawing / DL) | The drawing (and BOM item) and/or DL (and item number) whose content generates the issue. |
| **G** | Source file | File name(s) of the drawing and/or DL in column F. |
| **H** | Go to | Hyperlink to the affected hierarchy row in the Sub sheet (column J); when the issue has no hierarchy row, link to the warning cell in the Sub’s “Warnings / Errors” area. |
| **I** | Details | The full warning text, identical to the entry in the Sub’s “Warnings / Errors” area. |

Make row 1 a bold header row, freeze it, and apply an AutoFilter over the table. If no warnings exist, write “No warnings.” in A2. The Warnings Summary sheet is an output sheet and is the only sheet besides the copied Sub sheets that may be added to the workbook.

Columns G, H, I, K, L, N, O, P, Q, S, T, V and W shall be aligned center.

Do not report that other Sub folders or the full project are not yet extracted. Progress comments must relate only to the current Sub.

After validating a Sub, save the same output workbook before starting the next Sub. Continue until every eligible Sub represented in the authoritative inventory is processed.

Do not change headers, formulas, formatting, worksheet names other than copied Sub sheets, or template structure unless required by this prompt.

At the end, perform a workbook-wide check: one sheet per eligible Sub, no cross-Sub evidence, Inventory rows preserved and classified, the Warnings Summary sheet complete and its links pointing to the correct rows, all formulas/values readable, and no accidental extra filled column after W.

Columns B to F, J, R and U width shall be updated to fit text size.

Return a downloadable link to the completed Excel workbook. In the final message, state the output filename and list only unresolved issues that remain in the workbook.

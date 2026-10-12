# AI_FILE_GUIDE.md — InstanciumLens 2.0

Status: single source of truth for the product concept, architecture, data layout, AI contract, design system, and coding standards.
Supersedes the removed documents: Lens_Concept.docx, Technical Specification InstanciumLens 2.0.pdf, Style Guide (UX_UI).pdf, WBS.docx, client-integration-guide.md (condensed).
Current as of: WBS stage 5 completion (Decision Engine + What-If). project_structure.txt may lag behind; cross-check with Section 2 and Appendix A.

## 0. How to Use This Document

1. Code is primary: if this guide contradicts the code, the code wins; the guide is updated in the same commit.
2. Every new architectural decision is recorded here before or together with its implementation.
3. Sections 6 (storage), 9 (Decision Engine), 10 (design system), 11 (coding standards), and 16 (working agreement) are normative, not reference-only.
4. Language rule: all repository artifacts (documents, code, identifiers, comments, commit messages) are written in English. Russian is used only for chat communication with the team.

## 1. Product Concept

InstanciumLens 2.0 (Instancium Lens) is a local intelligent engine for modeling and decision validation:
documents → knowledge graph → natural-language exploration → validation of changes (What-If) against formalized constraints, with traceability of every fact back to its source fragment.

Value: not search and not chat, but understanding of system structure, dependencies, and consequences of changes, plus validation of decisions against regulatory requirements.

Principles:
1. Locality and privacy: confidential documents are processed without external clouds; LLM inference runs on the local InstantAIGate gateway; standalone access to model weights from the application is excluded (removed in WBS stage 1).
2. AI is not the source of truth: conclusions rely on the graph, rules, and sources; assumptions are scored (ConfidenceScore), and facts below threshold are routed to human review.
3. Traceability: every extracted statement is linked to a DocumentNode (file, page, RawText) via SourceDocumentNodeIds; in the UI this becomes clickable footnotes [n] and a drawer with source highlighting.
4. Human-confirmed structure: the ontology is created through Smart Interview and persisted to manifest.json plus {profile}_schema.json before analysis starts.
5. Multiple domain models (profiles) in one working folder; industry-agnostic (equipment, IT, legal, processes).

v1 scope: profiles, PDF import, entity/relationship extraction, Smart Interview, graph storage, graph Q&A, dependency analysis, basic rule validation (ConstraintNode/RequirementNode), What-If branches, source tracking, local processing.
Out of v1 scope: physical simulation, CAD, full regulatory compliance proof, non-Windows platforms, model versioning, OWL/RDF import, vector index.

## 2. Repository and Project Map

Solution: InstanciumLens.slnx.

| Project | TFM | Role |
|---|---|---|
| src/InstanciumLens.Core | net10.0 | Domain: models, interfaces, GraphRAG orchestration, Validation Pipeline, Decision Engine. No external dependencies. |
| src/InstanciumLens.Infrastructure | net10.0-windows10.0.19041.0 | SQLite graph and branch repository, folder analysis, manifest services, Windows PDF/OCR (Windows.Data.Pdf, Windows.Media.Ocr). |
| src/InstanciumLens.AI | net10.0 | InstantAIGate gateway client (SignalR): sessions, streaming, telemetry, extraction and answer prompts. |
| src/InstanciumLens.App | net10.0-windows10.0.19041.0 | .NET MAUI Blazor Hybrid (Windows): UI, routes, CSS design system, DI composition. |
| tests/InstanciumLens.Infrastructure.Tests | net10.0-windows10.0.19041.0 | xUnit: OCR extraction, branch isolation, Decision Engine rules. |

Dependency graph: App → {AI, Infrastructure}; AI → Core; Infrastructure → Core; Tests → Infrastructure. Core is the dependency-free center; cycles are forbidden.

Key packages: InstantAIGate.Core 1.2.1; Microsoft.AspNetCore.SignalR.Client 10.0.12; Microsoft.Data.Sqlite 10.0.12; Microsoft.Maui.Controls 10.x; Microsoft.AspNetCore.Components.WebView.Maui 10.x; xunit 2.9.3.

## 3. Layers and Responsibility Boundaries

1. Presentation (App): razor components, navigation, UI state, gateway telemetry in the UI. No business rules and no direct SQLite access — only injected Core/AI interfaces. Everything is async; the UI is never blocked.
2. Domain and Application (Core): models, contracts, pipeline orchestration (GraphRagManager), pure validation (GraphValidationPipeline), Decision Engine. Framework-agnostic.
3. Infrastructure: storage and OS integration (SQLite, file system, PDF/OCR). Does not reference the AI layer.
4. AI Integration (AI): InstantAIGate hub contract only; LLM prompts and response parsing; session retries.
5. Dependency direction rule: inward only (toward Core). UI-to-data access exclusively via IGraphRepository, IGraphRepositoryFactory, IDecisionEngine.

## 4. Key Contracts and Implementations

| Contract (Core.Interfaces) | Implementation | Purpose |
|---|---|---|
| IGraphRepository | SqLiteGraphRepository | SaveGraphSegmentAsync, LoadFullGraphAsync, GetRelevantSubGraphAsync(keywords), SaveChangesAsync(GraphChange). |
| IGraphRepositoryFactory | SqLiteGraphRepositoryFactory | CreateRepository, CreateBranchRepository, DeleteBranch, GetMasterDatabasePath, GetBranchDatabasePath. |
| IGraphRagManager | GraphRagManager (Core.Services) | BuildGraphForFolderAsync(folder, profile, progressCallback, ct) — Pass 1–3. |
| ILocalModelService | GatewayAiService (AI) | GenerateOntologyProfileAsync, ExtractEntitiesAsync, AnswerQueryAsync, AnswerQueryStreamAsync. |
| IGatewayHubClient | GatewayHubClient (AI) | RunCompletionAsync/RunCompletionStreamAsync, DestroySessionAsync, GetSessionTokenCountAsync, RollbackSessionAsync, Subscribe/UnsubscribeToModelDownloadAsync, GetGatewayStatusAsync; events GatewayStatusReceived, DownloadProgressReceived, ErrorReceived, ContextOverflowReceived, SessionClosedReceived, QueuePositionReceived. |
| IDocumentVision | WindowsDocumentVision | ExtractTextAsync(filePath) → one DocumentNode per page (render + OCR). |
| IFolderAnalyzer / IFolderManifestService / IFolderPickerService / IAppStateService | LocalFolderAnalyzer / LocalFolderManifestService / WindowsFolderPickerService / MauiAppStateService | Folder statistics; manifest.json; Windows folder picker; current folder in Preferences. |
| IDecisionEngine | DecisionEngine (Core.Services) | CreateBranchAsync, ApplyChangeToBranchAsync, DeleteBranchAsync. |
| IGraphValidationPipeline | GraphValidationPipeline (Core.Services) | Validate(graph, change) → ValidationReport; pure domain function without DB access. |

Supporting AI classes: GatewayConnectionService (connect, backoff retries 2/5/10/30 s, LastStatus/LastError), GatewaySessionManager (status facade), GatewayExceptions (GatewayException, GatewaySessionInactiveException, GatewayContextOverflowException). Infrastructure: DocumentAnalyzer (single-document pipeline).

## 5. Domain Models (Core/Models)

1. KnowledgeGraph { Entities: List<GraphEntity>, Relationships: List<GraphRelationship> }.
2. GraphEntity: Id, Name, Type, Description, SourceDocumentNodeIds: List<string>, ConfidenceScore: double (0.0..1.0), NodeKind: GraphNodeKind, RuleExpression: string?, Standard: string?, Severity: ValidationSeverity?, SourceText: string?.
3. GraphRelationship: SourceEntityId, TargetEntityId, RelationType, Description, Weight.
4. GraphNodeKind: Entity = 0, Constraint = 1, Requirement = 2. ConstraintNode/RequirementNode from the specification are implemented through NodeKind without separate tables, keeping a single graph walk and a single load path.
5. ValidationSeverity: Info = 0, Warning = 1, Error = 2.
6. GraphProfile: Id, DisplayName, Description, DatabaseFile, CreatedAt, DocumentCount, Icon (emoji) — the data of the profile business card in the UI.
7. FolderManifest { FolderName, Profiles } → .instancium/manifest.json.
8. DocumentNode: Id, FilePath, FileName, PageNumber, RawText, Summary.
9. CitationContext / CitationFact plus CitationContextBuilder.Build(graph, maxFacts): numbered facts for the answer prompt and [n] footnotes.
10. ProcessingState / ProcessingPhase: Idle, Pass1_TextExtraction, Pass2_EntityResolution, Pass3_GraphBuilding, Completed, Error; IsRunning.
11. Decision models: GraphChange (AddedOrUpdatedNodes, AddedOrUpdatedRelationships, RemovedNodeIds, RemovedRelationships), GraphRelationshipKey(SourceEntityId, TargetEntityId, RelationType), DecisionBranch (BranchId, ProfileId, FolderPath, DatabaseFilePath, CreatedAtUtc), ValidationViolation (RuleNodeId, AffectedNodeId, Severity, Code, Message, SourceDocumentNodeIds), ValidationReport (Violations, UtcTimestamp, HasErrors, IsValid).

## 6. Data Storage (Normative)

The user-selected working folder contains a hidden .instancium directory:

    .instancium/
      manifest.json                      profile passports for the UI (business cards)
      {profileId}_schema.json            profile ontology (Smart Interview output)
      knowledge_graph_{profileId}.db     master graph (SQLite)
      branches/{profileId}/{branchId}.db isolated What-If branches (sandbox)

SQLite schema (actual after stage 5):

    Entities(
      Id TEXT PRIMARY KEY,
      Name TEXT NOT NULL,
      Type TEXT NOT NULL,
      Description TEXT,
      NodeKind TEXT NOT NULL DEFAULT 'Entity',
      RuleExpression TEXT,
      Standard TEXT,
      Severity TEXT,
      SourceText TEXT,
      ConfidenceScore REAL NOT NULL DEFAULT 1.0,
      SourceDocumentNodeIds TEXT NOT NULL DEFAULT '[]'   -- JSON array of string
    );
    Relationships(
      SourceEntityId TEXT NOT NULL,
      TargetEntityId TEXT NOT NULL,
      RelationType TEXT NOT NULL,
      Description TEXT,
      Weight REAL,
      PRIMARY KEY (SourceEntityId, TargetEntityId, RelationType)
    );
    Indexes: Relationships(SourceEntityId), Relationships(TargetEntityId),
             Relationships(RelationType), Entities(Type), Entities(NodeKind).

Rules:
1. Backwards-compatible migrations only: CREATE TABLE IF NOT EXISTS plus PRAGMA table_info(Entities) plus ALTER TABLE ADD COLUMN for databases created by earlier stages; no other migration mechanism.
2. Node and relationship writes use INSERT OR REPLACE inside a transaction; rollback on error.
3. Branches: CreateBranchRepository copies the master file (File.Copy) only when the branch file does not exist; reopening a branch never overwrites the branch or touches the master. DeleteBranch must call SqliteConnection.ClearPool for the branch connection string before File.Delete, because the ADO.NET pool keeps a Windows file lock.
4. File names are sanitized (invalid characters and whitespace → '_', lower-case); branchId = {safe-name}_{guid:N}.
5. Technical database names are never shown to the user (see Section 10).

## 7. AI Layer: InstantAIGate Contract (Condensed)

Transport and authentication: SignalR, endpoint {BaseUrl}/hub/gateway; API key via Authorization Bearer / access_token (HubConnectionBuilder.WithUrl plus AccessTokenProvider); WithAutomaticReconnect(0/2/10/30 s).
Configuration: GatewaySettings { BaseUrl = http://localhost:5000, ApiKey, ActiveRepoId, HubUrl }; persisted in MAUI Preferences under GatewayBaseUrl, GatewayApiKey, GatewayActiveRepoId; changes apply after restart (the UI shows a hint).

Data Plane (mandatory order for pipelines):
1. JoinSession(sessionId, repoId) — repoId = "" means the active model.
2. MarkSessionAsEphemeral(sessionId) — immediately after Join for pipeline sessions (guarantees destroySlot on disconnect).
3. SendPromptDelta(sessionId, ChatMessage) — starts inference; tokens arrive via ReceiveTokenDelta; the final delta has IsDone = true.
4. DestroySession(sessionId) in finally, best-effort (not LeaveSession for completed dialogs).

Session memory: GetSessionTokenCount is the checkpoint base; RollbackSession(sessionId, pos) is the primary truncation path (system prefix reuse); ShiftSessionCache may be unavailable (FlashAttention / GPU offload) with Rollback as fallback; KV-cache does not survive a reconnect — the client is the source of truth for dialog history.

Errors and retries: ReceiveError whose text contains "not active" or "was not found" → GatewaySessionInactiveException → one retry with re-JoinSession plus MarkSessionAsEphemeral before the first token; ReceiveContextOverflow → GatewayContextOverflowException (menu: Rollback → session recreation); ReceiveError is logged and never crashes the UI.

Telemetry (mandatory in UI): ReceiveGatewayStatus (Status: 0 Uninitialized, 1 ModelDownloading, 2 ModelLoading, 3 Ready, 4 Faulted; ProgressPercentage, BytesPerSecond, ErrorMessage); SubscribeToModelDownload(repoId) plus ReceiveDownloadProgress; ReceiveQueuePosition; ReceiveSessionClosed. GatewayConnectionService keeps LastStatus/LastError and runs a retry loop on Closed.

Reconnect: the server keeps nothing for a reconnected ConnectionId → the Reconnected hook clears the local session registry and re-issues Join plus ephemeral marking for required sessions.

Gateway errata (client must tolerate): the ephemeral reaper destroys ephemeral sessions older than about 2 minutes without activity extension; the context pool is not bounded by MaxContexts (VRAM discipline is the client's job); image_file accepts server-local paths only (clients send image_url/image_base64); NativeModelDetails.GpuLayers echoes configuration.

Prompts (GatewayAiService): extraction — strict JSON {Entities, Relationships} against the ontology schema, every fact with ConfidenceScore 0.0..1.0, no markdown; chat answer — grounded only in numbered facts, marker [n] immediately after the statement without a space; ontology — strict JSON {ProfileName, DisplayName, Description, Icon, OntologySchema}. LLM output cleanup: substring from the first '{' to the last '}'.

## 8. Processing Pipeline and GraphRAG

Pass 1 Text Extraction: WindowsDocumentVision — Windows.Data.Pdf (page render) → Windows.Media.Ocr (user profile languages) → one DocumentNode per page; FileNotFoundException when the file is missing.
Pass 2 Entity Resolution: ILocalModelService.ExtractEntitiesAsync(page, ontologySchemaJson) → KnowledgeGraph segment; every entity receives SourceDocumentNodeIds += document.Id.
Pass 3 Graph Building: IGraphRepository.SaveGraphSegmentAsync (transaction, INSERT OR REPLACE).
Orchestration: GraphRagManager.BuildGraphForFolderAsync reads .instancium/{profileId}_schema.json (FileNotFoundException with a hint to recreate the profile), walks *.pdf recursively, reports ProcessingState via progressCallback; on error sets phase Error and rethrows.

Hybrid search and citation (Chat): query keywords (RU/EN stop words, length >= 4, take 12) → GetRelevantSubGraphAsync (LIKE on Name/Description plus incident Relationships plus nodes via IN) → CitationContextBuilder.Build(context, 10) → streamed answer → footnotes [n] → SourceDrawer with the source RawText.
Confidence Score: 0.0..1.0 on entities and relationships; threshold filtering and visual highlighting of "needs review" nodes belong to search/citation and the graph UI.

## 9. Decision Engine and What-If (Normative)

Rule node types: ConstraintNode (GraphNodeKind.Constraint) and RequirementNode (GraphNodeKind.Requirement) live in Entities; a rule-to-entity link is an ordinary GraphRelationship.

RuleExpression grammar (separator ';', prefixes case-insensitive):
1. required_relationship:<REL> — the affected entity has at least one relationship of type REL (incoming or outgoing).
2. forbidden_relationship:<REL> — such relationships must not exist.
3. max_relationships:<REL>:<int> — the count of REL relationships must not exceed the limit.
4. min_confidence:<double> — the entity ConfidenceScore must not fall below the threshold.
5. requires_node_type:<TYPE> — the entity must be related to at least one node of type TYPE.
6. A Requirement without RuleExpression produces a violation with code manual_verification (human review).

Severity: node.Severity when set; otherwise Constraint → Error, Requirement → Warning. Violation codes match rule prefixes; a malformed rule → invalid_rule (Warning); an unknown rule → unsupported_rule (Warning). Every violation message contains the entity name and the rule essence; SourceDocumentNodeIds of a violation are inherited from the rule node (traceability to the requirement source is mandatory).

Graph Walk: a rule node is relevant when it is changed itself or lies within distance <= GraphWalkDepth (2) from a changed node; affected entities are Entity nodes directly connected to the rule; when none exist, the changed Entity nodes from GraphChange are used. Duplicate violations are deduplicated by (RuleNodeId, AffectedNodeId, Code, Message).

What-If lifecycle: IDecisionEngine.CreateBranchAsync(folder, profile, branchName) → DecisionBranch (materializes the branch file from master); ApplyChangeToBranchAsync(branch, change) → SaveChangesAsync into the branch plus LoadFullGraphAsync plus Validate → ValidationReport; the master is never modified; DeleteBranchAsync removes the branch file (with pool cleanup, Section 6).
Extending the rule grammar means adding a prefix in GraphValidationPipeline plus tests in DecisionEngineTests, nothing else.

## 10. Design System "Corporate Premium" (Normative UI)

Form factor: Windows desktop, .NET MAUI Blazor Hybrid. Layout skeleton is CSS Grid with three semantic zones: Sidebar (profiles, gateway status, Settings; collapsible), Main Canvas (content/chat with generous whitespace), Status Bar (Pass 1–3; invisible or minimal in Idle).

UX heuristics: Miller's 7±2 (minimal settings on screen); Progressive Disclosure (Smart Interview instead of ontology forms); Visibility of System Status (Pass indicators, Thinking States, live gateway telemetry, no "dead" states).

Profiles in the UI are business cards: Icon (emoji), DisplayName, Description, meta (creation date, N docs). Technical names knowledge_graph_*.db and .db files are never shown to the user.

Typography: geometric grotesques (Inter / IBM Plex Sans / Geist); strict hierarchy; body text 15–16 px, line-height 1.6 for long legal answers.
Palette: light background Off-white #F9FAFB; dark theme graphite #111111; monochrome plus a single accent (Royal Blue / Corporate Indigo); semantic status colors (ready / error / downloading / offline). No rainbow.
Materials: restrained glassmorphism backdrop-filter: blur() for popovers and fixed headers (Windows 11 Mica effect); smooth animations; streaming text without container jitter.

CSS architecture: pure CSS, Tailwind forbidden; BEM (.chat-message, .chat-message__avatar, .chat-message--user); tokens are CSS Custom Properties in 01-theme.css (--color-*, --spacing-*, --font-*); the dark theme switches variables; Markdown isolation: LLM answers render only inside .markdown-body.
Numbered modules in wwwroot/css (include order in index.html matters): 01-theme, 02-reset, 03-layout, 04-sidebar, 05-buttons, 06-welcome, 07-folder-view, 08-graphs, 09-status-bar, 10-animations, 11-responsive, 12-smart-interview, 13-progress, 14-chat, 15-engine, 16-source-drawer. A new module takes the next free number plus BEM classes plus a link in index.html; edits to foreign modules stay surgical.

Key UX scenarios: Smart Interview (dialog → ontology generation → manifest.json → analysis); Source Tracking (footnote [n] → right-side Drawer with RawText and highlight); What-If (violation report with a mandatory link to the requirement source; output zone — Engine/Chat).

Routes and components: "/" Index (folder selection, profile cards, Start Analysis); "/chat/{ProfileId}" Chat (graph Q&A, footnotes, Drawer); "/engine" Engine (Pass 1–3 visualization, gateway status); "/smart-interview" SmartInterview (onboarding). Layout: MainLayout (status bar), NavMenu, Sidebar. Shared: SourceDrawer. Progress: Progress.razor (Pass steps). JS interop: wwwroot/js/scroll.js (scrollChatToBottom).

## 11. Coding Standards (Normative)

C#: Microsoft standards; PascalCase for types and members, camelCase for locals and parameters; #nullable enable; async/await only (.Result/.Wait/async void forbidden); LINQ preferred where clearer; constructor DI; SRP; empty catch blocks forbidden (handle or log plus rethrow); Console.WriteLine forbidden — ILogger only; yield return inside try-catch forbidden; gateway exception hierarchy — GatewayException and descendants.
Interfaces carry the I prefix in Core.Interfaces; implementations live in their own layer; registrations live in MauiProgram.CreateMauiApp: Singleton for stateful services and connections (GatewayConnectionService, GatewayHubClient, repository factory, DecisionEngine, GraphValidationPipeline), Transient for pipelines and managers (DocumentAnalyzer, GraphRagManager, IFolderManifestService).
Comments: XML /// for public API (what it does); inline comments only for architectural "why"; comments about AI modifications are forbidden in final code (the [AI DIFF] marker is used exclusively in chat output during review).
Identifiers, code file names, and commit messages are English only.
Tests: xUnit; AAA pattern; naming Method_Scenario_Expected; magic numbers forbidden (constants); teardown releases resources (SqliteConnection.ClearAllPools() before deleting the temp directory); temporary data lives in Path.GetTempPath() under a Guid subdirectory.

## 12. DI Composition (MauiProgram.CreateMauiApp)

Registration order: GatewaySettings (from Preferences) → HubConnection (WithUrl plus AccessTokenProvider plus WithAutomaticReconnect) → GatewayConnectionService → GatewaySessionManager → IGatewayHubClient/GatewayHubClient → ILocalModelService/GatewayAiService → IDocumentVision/WindowsDocumentVision → Func<string, IGraphRepository> → DocumentAnalyzer (transient) → IFolderPickerService/WindowsFolderPickerService → IFolderAnalyzer/LocalFolderAnalyzer → IAppStateService/MauiAppStateService → IFolderManifestService/LocalFolderManifestService (transient) → IGraphRagManager/GraphRagManager (transient) → IGraphRepositoryFactory/SqLiteGraphRepositoryFactory → IGraphValidationPipeline/GraphValidationPipeline → IDecisionEngine/DecisionEngine.
Blazor: AddMauiBlazorWebView; in DEBUG — AddBlazorWebViewDeveloperTools plus Logging.AddDebug.

## 13. Testing

Project tests/InstanciumLens.Infrastructure.Tests (xUnit, windows TFM):
1. WindowsDocumentVisionTests: exact text extraction from a generated PDF (MinimalPdfBuilder); FileNotFoundException for a wrong path.
2. DecisionEngineTests: branch isolation (master never contains branch changes); forbidden_relationship → Error; required_relationship → Error; Requirement without a rule → Warning manual_verification; DeleteBranch removes the branch file.
Isolation template: the test constructor creates a Guid temp directory; Dispose calls SqliteConnection.ClearAllPools() and deletes it recursively.
WBS stages 1–5 completion criteria are met: clean build, green tests.

## 14. Known Limitations and Technical Debt

Gateway: ephemeral reaper at about 2 minutes without extension; ShiftSessionCache unavailable with GPU offload/FlashAttention; image_file server-local only; context pool not bounded by MaxContexts.
SQLite: the ADO.NET pool holds Windows file locks — ClearPool/ClearAllPools is mandatory before deleting or copying databases.
Formats: GraphRagManager processes *.pdf only (DOCX/XLSX/TXT/MD/HTML/CSV from the specification are not implemented — roadmap).
OCR: Windows user profile languages only.
Debt: Console.WriteLine in Sidebar.razor and Index.razor (replace with ILogger); empty catch blocks in LocalFolderAnalyzer and GatewayAiService (JsonException); template Counter.razor and Weather.razor to be removed.
Roadmap: vector index (sqlite-vec/FAISS), OWL/RDF/XML/JSON-LD import and export, model versioning and change comparison, AES-256 at rest, RBAC and audit, graph visualization of 10k nodes / 25k relationships within 2 s (zoom, pan, clustering, color differentiation, highlighting of low-ConfidenceScore nodes).

## 15. Glossary

Profile — a named domain model (manifest.json plus schema plus master.db). Master graph — the primary database of a profile. Branch (sandbox) — an isolated graph copy for hypotheses. ConstraintNode / RequirementNode — rule nodes (GraphNodeKind). RuleExpression — a machine-readable rule of Section 9. Graph Walk — traversal from changed nodes with depth <= 2 to find relevant rules. Confidence Score — fact reliability 0.0..1.0. Citation [n] — a numbered prompt fact and its footnote in the answer. SourceDrawer — the panel showing source RawText. Smart Interview — dialog-based ontology onboarding. Pass 1/2/3 — text extraction / entity extraction / graph building. InstantAIGate — the local LLM gateway (SignalR). Ephemeral session — a session guaranteed to free VRAM on disconnect. Checkpoint / Rollback — token position and KV-cache truncation. Corporate Premium — the design system of Section 10.

## 16. AI Assistant Working Agreement

1. Chat language: Russian. Repository artifacts: English only (documents, code, identifiers, XML doc comments, inline comments, commit messages).
2. Code is never truncated in responses (no "// ..." omissions): only 100% compilable blocks.
3. Output format: a full file is printed only when more than three functions changed or on explicit request; otherwise the header "File: path | Func: name | Lines: X-Y" plus the change block; a function is printed in full when more than three of its lines changed, otherwise a ±2-line fragment.
4. No comments about AI modifications in final code; the [AI DIFF] inline marker appears only in chat output during review.
5. New CSS means a new numbered module plus BEM classes; a new Decision Engine rule means a grammar extension in Section 9 plus mandatory tests.
6. Any storage schema change means a migration via PRAGMA/ALTER (Section 6).
7. If this document contradicts the code, the code wins and the document is updated in the same change.

## Appendix A. File Map (actual state after stage 5)

Core: Interfaces/{IAppStateService, IDocumentVision, IFolderAnalyzer, IFolderManifestService, IFolderPickerService, IGraphRagManager, IGraphRepository, IGraphRepositoryFactory, ILocalModelService, IDecisionEngine, IGraphValidationPipeline}.cs; Models/{CitationContext, DocumentNode, FolderManifest, FolderStatistics, GraphProfile, KnowledgeGraph, ProcessingState, GraphNodeKind, DecisionModels}.cs; Services/{GraphRagManager, GraphValidationPipeline, DecisionEngine}.cs.
AI: Abstractions/IGatewayHubClient.cs; Configuration/GatewaySettings.cs; Contracts/GatewayWireDtos.cs; Models/DownloadProgress.cs; Services/{GatewayAiService, GatewayConnectionService, GatewayHubClient, GatewaySessionManager, GatewayExceptions}.cs.
Infrastructure: Data/{SqLiteGraphRepository, SqLiteGraphRepositoryFactory}.cs; Services/{DocumentAnalyzer, LocalFolderAnalyzer, LocalFolderManifestService}.cs; Vision/WindowsDocumentVision.cs.
App: MauiProgram.cs; Components/Layout/{MainLayout, NavMenu, Sidebar}.razor; Components/Pages/{Index, Chat, Engine, SmartInterview}.razor (plus debt: Counter, Weather); Components/Progress/Progress.razor; Components/Shared/SourceDrawer.razor; Models/{ChatMessage, ProfileItem}.cs; Services/{MauiAppStateService, WindowsFolderPickerService}.cs; wwwroot/css/01..16; wwwroot/js/scroll.js.
Tests: {WindowsDocumentVisionTests, DecisionEngineTests, MinimalPdfBuilder}.cs.
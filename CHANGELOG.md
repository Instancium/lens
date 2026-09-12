# Changelog

All notable changes to the "Instancium Lens" project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to Semantic Versioning.


## [0.4.0] - 2026-09-11

### Added
* Implemented `IAppStateService` in `InstanciumLens.Core` and `MauiAppStateService` in `InstanciumLens.App` to persist the user's last selected folder across application restarts using native OS storage (MAUI Preferences).
* Created the `SmartInterview.razor` page with a dedicated, distraction-free UI to facilitate the AI-driven ontology generation process, strictly following the Progressive Disclosure UX principle.
* Added `12-smart-interview.css` incorporating "Corporate Premium" design tokens, glassmorphism (`backdrop-filter`), and an optimized reading width (768px) for complex professional texts.

### Changed
* Refactored `Index.razor` to utilize a unified `HandleFolderSelection()` pipeline, instantly clearing legacy UI states (`folderStats = null`) to prevent "ghost" data when switching directories.
* Replaced URL query string parameters with the singleton `IAppStateService` for safe, offline-first context passing between the Main view and the Smart Interview screen.

### Fixed
* Fixed layout overflow (double scrollbars) in `SmartInterview.razor` by constraining the main layout container to `height: 100%` instead of absolute viewport height (`100vh`).
* Fixed UI reactivity in the Smart Interview chat input: the "Send" button now activates immediately upon the first keystroke by binding to the HTML `oninput` event (`@bind:event="oninput"`) instead of the default `onchange`.

## [0.3.0] - 2026-09-11

### Added
* Defined `IFolderPickerService` contract in `InstanciumLens.Core` and implemented `WindowsFolderPickerService` in `InstanciumLens.Infrastructure` to enable native Windows OS folder selection without relying on HTML file inputs.
* Added `FolderManifest` and `GraphProfile` domain models (including the `Icon` property) to `InstanciumLens.Core` for mapping saved analysis profiles.
* Introduced `FolderStatistics` model and `IFolderAnalyzer` contract in `InstanciumLens.Core` to pre-calculate the volume of documents (file counts, subfolders, total sizes) before starting the LLM analysis.
* Implemented `LocalFolderAnalyzer` in `InstanciumLens.Infrastructure` for high-performance directory scanning.

### Changed
* Refactored `Index.razor` folder selection logic to dynamically read and deserialize profiles from the `.instancium/manifest.json` hidden configuration file.
* Improved the UX of the "Change Folder" action to immediately invoke the OS folder picker dialog in a single click, eliminating unnecessary state resets and "empty" clicks.

### Fixed
* Fixed `UnauthorizedAccessException` crashes during recursive folder scanning by replacing naive `SearchOption.AllDirectories` with resilient `EnumerationOptions` (`IgnoreInaccessible = true`) to safely bypass protected system directories.
* Fixed UI data-binding issues in `Index.razor` (where statistics would render as zeros) by enforcing safe UI thread updates via `await InvokeAsync(StateHasChanged)`.

## [0.2.0] - 2026-09-10

### Added
* Formalized the "Smart Interview" concept: The local LLM now acts as an interactive assistant to dynamically generate JSON ontology schemas based on natural language conversations with the user (e.g., asking an engineer what to search for).
* Updated `SqLiteGraphRepository` constructor to accept a `profileName` parameter, enabling multi-graph support within a single directory.
* The system now generates isolated SQLite database files (e.g., `knowledge_graph_legal.db`, `knowledge_graph_kip.db`) inside the `.instancium` hidden folder based on the AI-generated profile name

wwwroot/
├── css/
│   ├── 01-theme.css        # CSS Variables, Design Tokens
│   ├── 02-reset.css        # Base reset & typography
│   ├── 03-layout.css       # Grid layout, app-container
│   ├── 04-sidebar.css      # Sidebar & profile cards
│   ├── 05-buttons.css      # All button variants
│   ├── 06-welcome.css      # Welcome screen
│   ├── 07-folder-view.css  # Folder loaded state
│   ├── 08-graphs.css       # Graph cards
│   ├── 09-status-bar.css   # Processing indicator
│   ├── 10-animations.css   # Keyframes
│   └── 11-responsive.css   # Media queries

## [0.1.1] - 2026-09-09

### Added
* Defined the `IInferenceProvider` interface in `InstanciumLens.AI.Abstractions` to establish a flexible "Provider" pattern, enabling seamless hot-swapping between execution engines (e.g., llama.cpp, ONNX)[cite: 4, 7].
* Added streaming token generation (`IAsyncEnumerable<string>`) to the inference contract to support real-time, non-blocking UI updates (ChatGPT-like typing effect)[cite: 4, 7].
* Integrated native P/Invoke bindings (`NativeLlamaMethods.cs`, `NativeMtmdMethods.cs`, `NativeGgmlLogLevel.cs`) into the `InstanciumLens.AI.Native` namespace for direct, low-overhead C++ interop[cite: 4].
* Implemented `LlamaCppProvider` in `InstanciumLens.AI.Providers.LlamaCpp` using native bindings to safely manage unmanaged memory (`IntPtr`) for the model, context, and sampler[cite: 3].
* Configured a default context size of `4096` tokens for the Qwen 8B model within the provider[cite: 3].
* Integrated low-temperature sampling (`0.2f`) in the inference loop to ensure strict and factual data extraction[cite: 3].
* Implemented real-time UTF-8 token decoding and streaming using `LlamaTokenize` and `LlamaTokenToPiece` methods[cite: 3].
* Configured automatic Vulkan GPU acceleration with safe CPU fallback (`NGpuLayers = 99`) in the provider to eliminate hardware fragmentation issues on Windows[cite: 3, 4, 7].
* Implemented `LocalAiService` in `InstanciumLens.AI.Services` to fulfill the core `ILocalModelService` contract[cite: 4, 7].
* Integrated strict ChatML prompt engineering in `LocalAiService` to force Qwen 8B into generating valid JSON based on dynamic ontology schemas (Pass 2)[cite: 4, 7].
* Added a resilient JSON extraction helper (`CleanLlmJsonOutput`) to automatically strip markdown formatting and code blocks from LLM responses[cite: 4].
* Implemented automatic source tracking by linking extracted `GraphEntity` objects back to their origin `DocumentNode` IDs, enabling clickable UI proofs[cite: 4, 6].
* Add `ModelProvisioning.cs` under `Downloader/` to handle transparent first-run GGUF weight downloads with real-time UI streaming progress[cite: 4, 7].

### Changed
* Refactored the `InstanciumLens.AI` layer architecture: dropped the hard dependency on the external `InstantAIGate` package in favor of a self-contained, lightweight provider system for direct local execution and better memory control[cite: 4, 7].

## [0.1.0] - 2026-09-08

### Added
* Created the solution from a template .NET MAUI Blazor Hybrid App[cite: 4].
* Initialized the solution structure consisting of 4 isolated projects: `InstanciumLens.App`, `InstanciumLens.Core`, `InstanciumLens.Infrastructure`, and `InstanciumLens.AI`[cite: 4, 9].
* Approved the core technology stack: .NET 10, MAUI Blazor Hybrid, pure HTML/CSS (without Tailwind), and SQLite[cite: 4, 9].
* Established base domain models in `InstanciumLens.Core`: `ProcessingState` (for tracking Pass 1, 2, 3 phases), `DocumentNode`, and `KnowledgeGraph`[cite: 4, 9].
* Created contracts (interfaces) for dependency inversion: `IDocumentVision`, `ILocalModelService`, and `IGraphRepository`[cite: 4, 9].
* Developed a JSON-based ontology schema concept allowing flexible AI adaptation for different domains (e.g., Legal Audit, Engineering Specs)[cite: 4, 5].
* Implemented the `DocumentAnalyzer` orchestrator service in `InstanciumLens.Core` to manage the 3-pass document analysis process and provide real-time UI state updates via dependency injection[cite: 4, 9].
* Implemented `WindowsDocumentVision` in `InstanciumLens.Infrastructure` utilizing native Windows APIs (`Windows.Data.Pdf` and `Windows.Media.Ocr`) for offline Pass 1 text extraction[cite: 4, 9].
* Created the `InstanciumLens.Infrastructure.Tests` project targeting the Windows platform (`net10.0-windows10.0.19041.0`) for integration testing of native components[cite: 4].
* Added `MinimalPdfBuilder` to the test project for autonomous on-the-fly binary generation of test PDF files, eliminating external file dependencies during test execution[cite: 4].
* Implemented the `SqLiteGraphRepository` class as a fundamental component of the infrastructure layer[cite: 4, 9].
* **Interface Implementation:** The repository implements `IGraphRepository`, adhering to the Dependency Inversion principle to ensure core application logic remains agnostic of the underlying SQLite implementation[cite: 4].
* **Targeted Storage Strategy:** It stores the SQLite database (`knowledge_graph.db`) within a hidden subfolder named `.instancium` directly inside the user-selected folder containing the analyzed documents[cite: 4].
* **Portability and Network Readiness:** Co-locating the database with the source documents makes the knowledge graph portable. Users can move the folder or place it on a network drive without needing to re-run the computationally expensive Pass 2 (Entity Resolution)[cite: 4].
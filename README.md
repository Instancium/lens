# Instancium Lens

**Local AI Workspace built on top of Instancium.Vectra**

Instancium Lens is a desktop application for exploring knowledge contained in your documents using natural language.

Built on top of **Instancium.Vectra**, Lens provides a user-friendly interface for document indexing, semantic search, retrieval-augmented generation (RAG), and knowledge exploration without requiring external cloud services.

All processing can be performed locally through InstantAIGate and Vectra, allowing users to work with confidential information while maintaining full control over their data.

---

## Why Lens?

Traditional document search helps you find text.

Lens helps you:

- Understand large document collections
- Ask questions in natural language
- Retrieve relevant source material
- Explore relationships between concepts
- Validate information against original sources
- Keep sensitive data local

Instead of manually navigating hundreds of pages, Lens helps surface relevant context and explain why it matters.

---

## Built on Vectra

Lens uses **Instancium.Vectra** as its retrieval and knowledge foundation.

### Vectra provides

- PDF document ingestion
- Text chunking
- Embedding generation
- In-memory vector storage
- High-performance semantic search
- RAG pipelines
- InstantAIGate integration

### Lens focuses on the user experience layer

- Desktop UI
- Workspace management
- Knowledge exploration
- Source navigation
- AI-assisted investigation
- Future decision-support workflows

---

## Core Features

### Document Intelligence

Import documents and transform them into searchable knowledge.

- PDF processing
- OCR support
- Semantic indexing
- Natural language retrieval

### AI-Assisted Knowledge Exploration

Ask questions about your document collection.

**Examples:**

- "Which sections discuss pressure sensors?"
- "What documents mention this requirement?"
- "Summarize the relationships between these components."

### Source Traceability

Answers should always be connected to source material.

Lens is designed around the principle that **AI is not the source of truth**.

Claims should be traceable back to the original documents whenever possible.

### Local-First Architecture

- Local document storage
- Local indexing
- Local AI inference through InstantAIGate
- No mandatory cloud dependency

---

## Architecture

```text
┌─────────────────────┐
│   Instancium Lens   │
│     Desktop UI      │
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│ Instancium.Vectra   │
│ Search & RAG Engine │
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│   InstantAIGate     │
│  Local AI Gateway   │
└─────────────────────┘
```

---

## Experimental Features

The following capabilities are currently experimental and may evolve significantly:

- Knowledge graph generation
- Relationship discovery
- Decision validation
- Scenario analysis
- What-if simulations

These features are being explored as extensions to the Vectra-powered knowledge workspace and should not be considered stable platform functionality yet.

---

## Vision

The long-term goal of Lens is **not** to become another chat application.

The goal is to create a workspace where users can:

- Understand complex information domains
- Explore knowledge interactively
- Trace conclusions back to evidence
- Build confidence in decisions

**Vectra remains the retrieval foundation. Lens becomes the experience layer built on top of it.**

---

## Technology Stack

- .NET 10
- C#
- Instancium.Vectra
- InstantAIGate
- SignalR
- SQLite
- MAUI Blazor Hybrid

---

## Status

**Current Stage:** MVP / R&D

The primary focus is validating document ingestion, retrieval quality, AI-assisted exploration, and the overall Lens user experience before expanding into advanced modeling and decision-support capabilities.
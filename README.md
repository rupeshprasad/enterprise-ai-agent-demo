# Enterprise AI Agent Demo

A full-stack teaching application that demonstrates how an enterprise AI assistant progresses from a basic LLM to retrieval-augmented generation (RAG), tool calling, workflow actions, and deterministic authorization. All customers and business data are fictional.

## What the demo includes

- Four selectable capability modes: **LLM Only**, **LLM + RAG**, **LLM + RAG + Tools**, and **Full Agent**
- Gemini generation and embedding models
- Internal ordering-policy retrieval with live knowledge-base refresh
- Customer lookup, verification status, order-eligibility investigation, and verification-review requests
- Backend authorization so each application user can access only assigned customers
- UI controls to manage customers and access assignments; changes are persisted immediately to `data/enterprise-data.json`
- Per-user chat history under `data/chat-history` (excluded from Git)
- Enterprise-scope guardrails that reject unrelated questions before calling Gemini
- Swagger/OpenAPI and Serilog console plus rolling-file logging

## Technology

- ASP.NET Core MVC/Web API on .NET 10
- React 19 with Vite
- Google Gemini API
- JSON-based demo data and Markdown policy documents

## Prerequisites

- Visual Studio 2022 Community with the **ASP.NET and web development** workload, or the .NET 10 SDK
- Node.js 20 or later and npm
- A Google Gemini API key with access to the configured generation and embedding models

## Configure or update the Gemini API key

The API key is stored with .NET User Secrets and is not committed to Git. From the repository root, run:

```powershell
dotnet user-secrets set "Gemini:ApiKey" "YOUR_GEMINI_API_KEY" --project src/EnterpriseAgent.Api/EnterpriseAgent.Api.csproj
```

Run the same command with the new value whenever the key needs to be updated. Restart the API after changing it. Do not place the key in `appsettings.json`, the React application, logs, or source control.

The non-secret Gemini model, timeout, retry, and logging settings are in `src/EnterpriseAgent.Api/appsettings.json`. The `GEMINI_API_KEY` environment variable can also supply the key when User Secrets are unavailable.

## Run in Visual Studio

1. Open `EnterpriseAgentDemo.sln`.
2. Select the **FE & BE** multi-project launch profile.
3. Press **F5**.

Visual Studio starts the Vite frontend at `http://localhost:5173` and the API. Swagger opens at `https://localhost:7013/swagger`. If frontend packages have not been restored, run `npm install` once in `src/EnterpriseAgent.Web`.

## Run from terminals

Start the API:

```powershell
dotnet run --project src/EnterpriseAgent.Api
```

In a second terminal, start the frontend:

```powershell
cd src/EnterpriseAgent.Web
npm install
npm run dev
```

The frontend proxies `/api` requests to `http://localhost:5187`.

## Suggested demo flow

1. Compare the same policy question across the four capability modes.
2. Ask `What is the verification status of Jordan Lee?` as Alex Morgan.
3. Ask `Can customer 002 place an order?`
4. Switch to Priya Shah (restricted access) and try to access an unassigned customer.
5. Use **Manage access** to assign that customer, then repeat the question without restarting the application.
6. Use **Manage customers** to add or edit a customer and observe the persisted JSON data.
7. Change `data/policies/customer-ordering-policy.md`, click **Refresh Knowledge Base**, and ask the policy question again.

## Validation and logs

```powershell
dotnet test tests/EnterpriseAgent.Tests
cd src/EnterpriseAgent.Web
npm run lint
npm run build
```

Serilog writes console logs to file and daily rolling files under `C:\logs\EnterpriseAgent.Api`. Secrets and raw prompts are not intentionally logged.

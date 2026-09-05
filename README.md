# Enterprise AI Agent Demo

A small teaching application that demonstrates the progression from LLM-only answers to RAG, tool calling, agent orchestration, actions, and deterministic authorization. All customer data and actions are fictional.

## Prerequisites

- Visual Studio 2022 Community with the ASP.NET and web development workload, or the .NET 10 SDK
- Node.js 20 or later and npm
- A Google Gemini API key with access to the configured generation and embedding models

## Configuration

The backend reads the Gemini key from .NET user-secrets or the `GEMINI_API_KEY` environment variable. Never add it to `appsettings.json` or frontend code.

```powershell
dotnet user-secrets set "Gemini:ApiKey" "<YOUR_GEMINI_API_KEY>" --project src/EnterpriseAgent.Api
```

The committed settings in `src/EnterpriseAgent.Api/appsettings.json` configure:

- provider: Gemini
- generation model: `gemini-3.5-flash-lite`
- embedding model: `gemini-embedding-2`
- request timeout and transient retry behavior
- Serilog console and rolling file output

You may override configuration with environment variables such as `Gemini__Model`, `Gemini__EmbeddingModel`, or `AI__Provider`.

## Run in Visual Studio

1. Open `EnterpriseAgentDemo.sln`.
2. Select the `FE & BE` multi-project launch profile.
3. Press F5.

Visual Studio starts the React/Vite frontend at `http://localhost:5173` and the ASP.NET Core API. Swagger opens from the API launch profile at `https://localhost:7013/swagger`.

If the frontend packages have not been restored, run `npm install` once in `src/EnterpriseAgent.Web`.

## Run from terminals

Backend:

```powershell
dotnet restore
dotnet run --project src/EnterpriseAgent.Api
```

Frontend in a second terminal:

```powershell
cd src/EnterpriseAgent.Web
npm install
npm run dev
```

The Vite development server proxies `/api` requests to `http://localhost:5187`.

## Demo questions

Use `demo-user` unless a question says otherwise.

1. `What does Pending verification mean according to our ordering policy?`
2. `What is the verification status of ABC123?`
3. `Why can't customer ABC123 place an order?`
4. `Can DEF456 place an order?`
5. `Create a verification review request for ABC123.`
6. Select `restricted-user`, then ask: `Show me the verification status for ABC123.`
7. `Can ABC123 place an order?`

The response can display approved tool calls, safe tool inputs/results, retrieved policy sections, and source similarity scores. It does not expose model chain-of-thought.

## Chat history

Chat messages are saved as separate per-user JSON files under `data/chat-history`. Selecting a demo user loads only that user's conversation, and **New chat** clears only the selected user's history. The generated JSON files are excluded from source control.

## Policy-change demonstration

1. Open `data/policies/customer-ordering-policy.md`.
2. Change the Pending Verification rule to: `Customers whose verification status is Pending may place orders up to $5,000 while verification is being completed.`
3. Save the file.
4. Click **Refresh Knowledge Base** in the web app.
5. Ask `Can ABC123 place an order?` again.

The answer should now reflect the $5,000 limit without retraining the model or restarting the application. Restore the original policy and refresh again after the demonstration.

## Tests and logs

```powershell
dotnet test tests/EnterpriseAgent.Tests
cd src/EnterpriseAgent.Web
npm run lint
npm run build
```

Serilog writes console logs and daily rolling files under `C:\logs\EnterpriseAgent.Api`. Gemini keys and raw prompts are not logged.

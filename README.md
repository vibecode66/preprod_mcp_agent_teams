FuncCosmicWwicUat/
├── FuncCosmicWwicUat.csproj
├── Program.cs
├── AgentFunction.cs
├── Services/
│   ├── IntentService.cs
│   ├── McpClientService.cs
│   └── NL2SqlService.cs
├── Models/
│   └── AgentModels.cs
└── local.settings.json




Teams/Copilot  →  POST /api/agent  {"message": "Show top 5 open cases"}
                        ↓
              [1] IntentService → OpenAI → "NL2SQL"
                        ↓
              [2] McpClientService → GET /api/sqlquery/tables
                        ↓  (schema context)
              [3] NL2SqlService → OpenAI → "SELECT TOP 5 * FROM [dbo].[Cases] WHERE Status='Open';"
                        ↓
              [4] Validate SQL (regex guard)
                        ↓
              [5] McpClientService → POST /api/sqlquery/execute
                        ↓  (raw rows)
              [6] NL2SqlService.FormatResults → OpenAI → natural language reply
                        ↓
              Response: { reply, generatedSql, rowCount, rows }
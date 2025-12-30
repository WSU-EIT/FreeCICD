# SignalR Integration

Real-time progress updates during wizard operations.

---

## Connection Setup

```csharp
// In Wizard.razor OnInitializedAsync
hubConnection = new HubConnectionBuilder()
    .WithUrl(Navigation.ToAbsoluteUri("/apphub"))
    .WithAutomaticReconnect()
    .Build();

hubConnection.On<DataObjects.SignalRUpdate>("ReceiveMessage", HandleSignalRUpdate);

await hubConnection.StartAsync();
connectionId = hubConnection.ConnectionId;
```

---

## Update Types

```csharp
public enum SignalRUpdateType
{
    LoadingDevOpsInfoStatusUpdate,  // "Loading projects..."
    SavedPipelineYaml,              // YAML saved successfully
    CreatedPipeline,                // Pipeline created
    Error                           // Error occurred
}
```

---

## SignalRUpdate Model

```csharp
public class SignalRUpdate
{
    public SignalRUpdateType UpdateType { get; set; }
    public string? ConnectionId { get; set; }
    public Guid ItemId { get; set; }
    public string? Message { get; set; }
    public object? Data { get; set; }
}
```

---

## Usage Pattern

### Client (Razor)

```csharp
private void HandleSignalRUpdate(DataObjects.SignalRUpdate update)
{
    if (update.ConnectionId != connectionId) return;
    
    switch (update.UpdateType)
    {
        case SignalRUpdateType.LoadingDevOpsInfoStatusUpdate:
            loadingMessage = update.Message;
            break;
            
        case SignalRUpdateType.SavedPipelineYaml:
            // Handle YAML saved
            break;
            
        case SignalRUpdateType.Error:
            errorMessage = update.Message;
            break;
    }
    
    StateHasChanged();
}
```

### Server (DataAccess)

```csharp
await SignalRUpdate(new DataObjects.SignalRUpdate
{
    UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
    ConnectionId = connectionId,
    ItemId = Guid.NewGuid(),
    Message = $"Loading repository: {repoName}"
});
```

---

## Progress Messages

Typical messages during wizard operations:

| Operation | Messages |
|-----------|----------|
| Load Projects | "Loading projects..." |
| Load Repos | "Loading repositories..." |
| Load Branches | "Loading branches..." |
| Load Files | "Loading files..." |
| Save YAML | "Saving YAML file..." |
| Create Pipeline | "Creating pipeline..." |

---

## Error Handling

```csharp
try
{
    // API call
}
catch (Exception ex)
{
    await SignalRUpdate(new DataObjects.SignalRUpdate
    {
        UpdateType = SignalRUpdateType.Error,
        ConnectionId = connectionId,
        Message = $"Error: {ex.Message}"
    });
}
```

---

*Last Updated: 2024-12-19*

using CoreSyncServer;

var builder = WebApplication.CreateBuilder(args);
builder.AddCoreSyncServer();

var app = builder.Build();
app.UseCoreSyncServer();
app.Run();

// Make the implicit Program class accessible to the test project
public partial class Program;

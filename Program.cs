using Hangfire;
using Hangfire.Mongo;
using Hangfire.Mongo.Migration.Strategies;
using Hangfire.Mongo.Migration.Strategies.Backup;
using Microsoft.AspNetCore.Http.Features;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 524288000; // 500 MB
});

// Configure form options to handle large file uploads
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 524288000; // 500 MB
});

// MongoDB connection string from environment variable or fallback
var mongoConnectionString = Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING")
                            ?? "mongodb://localhost:27017";
Console.WriteLine($"MongoDB Connection String: {mongoConnectionString}");

// Register MongoDB client
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnectionString));

// Configure Hangfire to use MongoDB storage
builder.Services.AddHangfire(config =>
{
    var options = new MongoStorageOptions
    {
        CheckQueuedJobsStrategy = CheckQueuedJobsStrategy.TailNotificationsCollection, // Use tail notifications for monitoring
        MigrationOptions = new MongoMigrationOptions
        {
            MigrationStrategy = new DropMongoMigrationStrategy(),
            BackupStrategy = new NoneMongoBackupStrategy()
        }
    };
    config.UseMongoStorage(mongoConnectionString, "HangfireDb", options); // MongoDB database for Hangfire
});
builder.Services.AddHangfireServer(); // Add Hangfire background job server

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument(config =>
{
    config.DocumentName = "File Manager API";
    config.Title = "FileManagerAPI v1";
    config.Version = "v1";
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseOpenApi();
    app.UseSwaggerUi(config =>
    {
        config.DocumentTitle = "File Manager";
        config.Path = "/swagger";
        config.DocumentPath = "/swagger/{documentName}/swagger.json";
        config.DocExpansion = "list";
    });
}

// Hangfire Dashboard for monitoring jobs
app.UseHangfireDashboard("/hangfire");

// Middleware
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

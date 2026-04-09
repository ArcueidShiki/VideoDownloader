using AnyDownloader.Core;
using AnyDownloader.Engine;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Rate limiting — 10 POST /api/downloads per minute per IP (free tier)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddFixedWindowLimiter("downloads_post", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });
});

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWebApp", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Register download services
builder.Services.AddSingleton<IDownloadService, YtDlpDownloadService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<YtDlpDownloadService>>();
    return new YtDlpDownloadService(logger);
});

builder.Services.AddSingleton<IDownloadService, TorrentDownloadService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<TorrentDownloadService>>();
    return new TorrentDownloadService(logger);
});

// Register queue manager
builder.Services.AddSingleton<IDownloadQueueManager>(sp =>
{
    var downloadServices = sp.GetServices<IDownloadService>();
    var logger = sp.GetRequiredService<ILogger<DownloadQueueManager>>();
    var config = new QueueConfiguration
    {
        GlobalMaxConcurrentDownloads = 20,
        MaxConcurrentDownloadsFree = 2,
        MaxConcurrentDownloadsPremium = 5,
        MaxConcurrentDownloadsVip = 10,
        RateLimitFree = 0,
        RateLimitPremium = 0,
        RateLimitVip = 0,
        EnableQueueProcessing = true
    };
    return new DownloadQueueManager(downloadServices, logger, config);
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowWebApp");

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// Start queue processing
var queueManager = app.Services.GetRequiredService<IDownloadQueueManager>();
_ = queueManager.StartProcessingAsync(CancellationToken.None);

app.Run();

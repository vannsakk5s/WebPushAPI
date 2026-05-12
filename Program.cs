using WebPushAPI.Models;
using WebPush;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCors", policy =>
    {
        policy.WithOrigins(
            "http://localhost:5501",
            "http://127.0.0.1:5501"
            ).AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseCors("FrontendCors");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var subscriptions = new List<PushSubscriptionDto>();

app.MapGet("/", () =>
{
    return Results.Ok(new { Message = "Welcome to the Web Push API!" });
});

app.MapGet("/api/vapid-public-key", (IConfiguration config) =>
{
    var publicKey = config["Vapid:PublicKey"];

    return Results.Ok(new { publicKey });
});

app.MapGet("/api/generate-vapid-keys", () =>
{
    var keys = VapidHelper.GenerateVapidKeys();

    return Results.Ok(new
    {
        publicKey = keys.PublicKey,
        privateKey = keys.PrivateKey
    });
});

app.MapPost("/api/subscribe", (PushSubscriptionDto subscription) =>
{
    if (
    string.IsNullOrWhiteSpace(subscription.Endpoint) ||
    string.IsNullOrWhiteSpace(subscription.Keys.P256dh) ||
    string.IsNullOrWhiteSpace(subscription.Keys.Auth))
    {
        return Results.BadRequest(new { Message = "Invalid subscription data." });
    }

    var alreadyExists = subscriptions.Any(x => x.Endpoint == subscription.Endpoint);

    if (!alreadyExists)
    {
        subscriptions.Add(subscription);
    }

    Console.WriteLine($"New subscription added: {subscription.Endpoint}");

    return Results.Ok(new
    {
        message = "Subscription successful.",
        total = subscriptions.Count
    });
});

app.MapPost("/api/send-notification", async (
    NotificationRequest request,
    IConfiguration config) =>
{
    if (subscriptions.Count == 0)
    {
        return Results.Ok(new
        {
            message = "No subscribers to send notifications to.",
            total = 0
        });
    }

    var publicKey = config["Vapid:PublicKey"];
    var privateKey = config["Vapid:PrivateKey"];
    var subject = config["Vapid:Subject"];

    if (
        string.IsNullOrWhiteSpace(publicKey) ||
        string.IsNullOrWhiteSpace(privateKey) ||
        string.IsNullOrWhiteSpace(subject))
    {
        return Results.BadRequest(new
        {
            message = "VAPID keys or subject are not configured properly."
        });
    }

    var vapidDetails = new VapidDetails(subject, publicKey, privateKey);
    var webPushClient = new WebPushClient();

    var payload = System.Text.Json.JsonSerializer.Serialize(new
    {
        title = string.IsNullOrWhiteSpace(request.Title)
        ? "Hello from .Net"
        : request.Title,

        body = string.IsNullOrWhiteSpace(request.Body)
        ? "This notification was sent from asp.net core"
        : request.Body,

        url = string.IsNullOrWhiteSpace(request.Url)
        ? "/"
        : request.Url

    });

    var results = new List<object>();

    foreach (var item in subscriptions.ToList())
    {
        try
        {
            var pushSubscription = new PushSubscription(
                item.Endpoint,
                item.Keys.P256dh,
                item.Keys.Auth
            );

            await webPushClient.SendNotificationAsync(
                pushSubscription,
                payload,
                vapidDetails
            );

            results.Add(new
            {
                ok = true,
                endpoint = item.Endpoint
            });
        }
        catch (WebPushException ex)
        {
            Console.WriteLine($"Push error: {ex.Message}");

            results.Add(new
            {
                ok = false,
                endpoint = item.Endpoint,
                error = ex.Message
            });
        }
    }

    return Results.Ok(new
    {
        message = "Notification process finished",
        total = subscriptions.Count,
        results
    });
});

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

public class NotificationRequest
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Url { get; set; } = "/";
}

using Microsoft.AspNetCore.RateLimiting;
using Scalar.AspNetCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddRateLimiter(options =>
{
    // Configuration du limiteur global qui s'applique à toutes les requêtes
    //options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(HttpContext =>
    //RateLimitPartition.GetFixedWindowLimiter(
    //    // Clef de partition : identifie de manière unique chaque client
    //    partitionKey: HttpContext.User.Identity?.Name ?? HttpContext.Request.Headers.Host.ToString(),

    //    // Configuration de la fenêtre de limitation
    //    factory: partition => new FixedWindowRateLimiterOptions
    //    {
    //        AutoReplenishment = true, // Renouvellement automatique des jetons
    //        PermitLimit = 4, // Nombre de requête maximum autorisée
    //        QueueLimit = 5, // Nombre de requête en file d'attente
    //        Window = TimeSpan.FromSeconds(10) // Fenêtre de temps 
    //    }));

    // Configuration du limiteur par politique spécifique (ce qui est recommandé)
    //options.AddFixedWindowLimiter("fixed", opt =>
    //{
    //    opt.PermitLimit = 4;
    //    opt.Window = TimeSpan.FromSeconds(5);
    //});

    // Politique par défaut globale (fallback)
    //options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    //    RateLimitPartition.GetSlidingWindowLimiter(
    //        partitionKey: ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
    //        factory: _ => new SlidingWindowRateLimiterOptions
    //        {
    //            PermitLimit = 100,
    //            Window = TimeSpan.FromMinutes(1),
    //            SegmentsPerWindow = 6,
    //            QueueLimit = 0
    //        }));

    // -- Exemple de configuration

    // Politique pour endpoints publics
    options.AddSlidingWindowLimiter("public", opt =>
    {
        opt.PermitLimit = 60;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.SegmentsPerWindow = 6;
    });

    // Politique pour authentification
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(5);
        opt.QueueLimit = 0;
    });

    // Politique pour utilisateurs authentifiés
    options.AddSlidingWindowLimiter("authenticated", opt =>
    {
        opt.PermitLimit = 500;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.SegmentsPerWindow = 6;
    });

    // Gestion personnalisée du rejet
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;// Politique par défaut globale (fallback)
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            }));

    // Gestion personnalisée du rejet
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();

            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                error = "Trop de requêtes",
                message = $"Limite atteinte. Réessayez dans {retryAfter.TotalSeconds:F0} secondes.",
                retryAfterSeconds = (int)retryAfter.TotalSeconds
            }, cancellationToken: token);
        }
        else
        {
            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                error = "Trop de requêtes",
                message = "Vous avez dépassé la limite autorisée. Veuillez réessayer plus tard."
            }, cancellationToken: token);
        }
    };
});

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthentication();

// Activation du middleware de limitation
// * Doit être placé avant les endpoints
// * Doit être placé après l'authentification (pour récupérer l'utilisateur qui se connecte)
app.UseRateLimiter();

app.UseAuthorization();
app.MapControllers();


app.Run();

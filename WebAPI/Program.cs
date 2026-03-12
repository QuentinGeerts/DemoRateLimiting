using Microsoft.AspNetCore.RateLimiting;
using Scalar.AspNetCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();


// Enregistre et configure le middleware de limitation de débit (rate limiting) dans le conteneur d'injection de dépendances.
builder.Services.AddRateLimiter(options =>
{
    // Code de statut HTTP renvoyé quand une requête est rejetée (429 = Too Many Requests)
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Callback global appelé à chaque fois qu'une requête est rejetée.
    // Deux mécanismes pour informer le client du temps restant :
    //   1. Header HTTP standard  : "Retry-After: <secondes>"
    //   2. Corps JSON structuré  : retryAfterSeconds + retryAt (timestamp UTC ISO 8601)
    //
    // ⚠ MetadataName.RetryAfter n'est disponible que pour les limiteurs basés
    //   sur le temps (Fixed Window, Sliding Window, Token Bucket).
    //   Le Concurrency limiter ne connaît pas le délai d'attente (dépend de
    //   quand un slot se libère) → retryAfterSeconds sera null dans ce cas.
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";

        int? retryAfterSeconds = null;
        string? retryAt = null;

        // TryGetMetadata retourne true uniquement si le limiter fournit un délai connu
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterSpan))
        {
            retryAfterSeconds = (int)retryAfterSpan.TotalSeconds;
            retryAt = DateTime.UtcNow.AddSeconds(retryAfterSeconds.Value).ToString("O");

            // Header HTTP standard "Retry-After" : nombre de secondes à attendre
            context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.Value.ToString();
        }

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            status = 429,
            message = "Trop de requêtes. Veuillez patienter avant de réessayer.",
            retryAfterSeconds,   // null si Concurrency limiter (délai inconnu)
            retryAt              // null si Concurrency limiter
        },
        cancellationToken);
    };

    // GLOBAL LIMITER — s'applique à TOUTES les requêtes, AVANT les politiques
    // par endpoint. C'est un filet de sécurité transversal.
    //
    // Différence clé avec les politiques nommées (AddFixedWindowLimiter, etc.) :
    //   • Politiques nommées  → opt-in via [EnableRateLimiting("...")] sur
    //                           chaque contrôleur / action.
    //   • GlobalLimiter       → toujours actif, aucune annotation nécessaire.
    //                           S'applique même aux endpoints sans politique.
    //
    // Les deux niveaux sont CUMULATIFS : une requête doit passer le GlobalLimiter
    // ET la politique de l'endpoint pour être acceptée.
    //
    // Le GlobalLimiter est un PartitionedRateLimiter<HttpContext>, ce qui permet
    // de partitionner les compteurs par IP, utilisateur, clé API, etc.
    //
    // Exemple ci-dessous : 100 requêtes / minute PAR ADRESSE IP.
    // Cas d'usage typique : protection globale contre le flood / DDoS,
    // indépendamment des règles métier de chaque endpoint.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        // Clé de partition = adresse IP du client
        // (pour des IP "null" en test local, on utilise "localhost" comme fallback)
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "localhost";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ipAddress,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1), // Fenêtre d'1 minute
                PermitLimit = 100,                     // 100 requêtes max / minute / IP
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });


    // 1. FIXED WINDOW (Fenêtre fixe)
    // Principe : autorise N requêtes dans une fenêtre de temps FIXE.
    // La fenêtre repart à zéro de manière rigide à chaque expiration,
    // indépendamment du moment où les requêtes ont été émises.
    //
    // Exemple : 5 requêtes autorisées toutes les 10 secondes.
    // Si 5 requêtes arrivent à t=0s, aucune autre ne passe jusqu'à t=10s.
    options.AddFixedWindowLimiter(policyName: "FixedWindow", configureOptions: opt =>
    {
        opt.Window = TimeSpan.FromSeconds(10);    // Durée de la fenêtre
        opt.PermitLimit = 5;                      // Nombre max de requêtes par fenêtre
        opt.QueueLimit = 0;                       // Pas de file d'attente : rejet immédiat
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });


    // 2. SLIDING WINDOW (Fenêtre glissante)
    // Principe : autorise N requêtes sur une fenêtre de temps qui GLISSE
    // continuellement dans le passé. La fenêtre est découpée en segments ;
    // à chaque nouveau segment, les requêtes du segment le plus ancien
    // sont libérées et ajoutées au quota disponible.
    //
    // Avantage vs Fixed Window : évite le "burst" en début de fenêtre.
    //
    // Exemple : 5 requêtes / 10s avec 2 segments (= segment de 5s).
    // Les slots se libèrent progressivement toutes les 5 secondes.
    options.AddSlidingWindowLimiter(policyName: "SlidingWindow", configureOptions: opt =>
    {
        opt.Window = TimeSpan.FromSeconds(10);    // Durée totale de la fenêtre
        opt.PermitLimit = 5;                      // Nombre max de requêtes sur la fenêtre
        opt.SegmentsPerWindow = 2;                // Découpage en 2 segments (chaque segment = 5s)
        opt.QueueLimit = 0;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });


    // 3. TOKEN BUCKET (Seau à jetons)
    // Principe : un seau contient des jetons. Chaque requête consomme 1 jeton.
    // Les jetons sont rechargés périodiquement jusqu'au maximum du seau.
    // Permet un burst initial (si le seau est plein) tout en lissant
    // le débit sur le long terme.
    //
    // Exemple : seau de 5 jetons max, +2 jetons rechargés toutes les 5s.
    // Au démarrage : 5 requêtes immédiates possibles.
    // Ensuite : ~2 requêtes toutes les 5s en rythme de croisière.
    options.AddTokenBucketLimiter(policyName: "TokenBucket", configureOptions: opt =>
    {
        opt.TokenLimit = 5;                                   // Capacité max du seau
        opt.ReplenishmentPeriod = TimeSpan.FromSeconds(5);    // Fréquence de recharge
        opt.TokensPerPeriod = 2;                              // Jetons ajoutés par période
        opt.AutoReplenishment = true;                         // Recharge automatique en arrière-plan
        opt.QueueLimit = 0;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });


    // 4. CONCURRENCY (Limitation de concurrence)
    // Principe : limite le nombre de requêtes traitées SIMULTANÉMENT,
    // indépendamment du temps. Chaque requête acquiert un slot au début
    // de son traitement et le libère à sa fin.
    //
    // Utile pour protéger des ressources sous-jacentes contre la surcharge
    // (ex : pool de connexions DB, appels vers une API tierce).
    //
    // Exemple : max 2 requêtes en parallèle.
    // Si 2 requêtes sont déjà en cours, la 3e reçoit un 429.
    options.AddConcurrencyLimiter(policyName: "Concurrency", configureOptions: opt =>
    {
        opt.PermitLimit = 2;                                  // Slots de traitement simultané max
        opt.QueueLimit = 0;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthentication();
// <!> L'ordre est important :
// le middleware de limitation doit être placé après l'authentification pour pouvoir appliquer des politiques basées sur l'identité de l'utilisateur,
// mais avant l'autorisation pour limiter les requêtes avant même de vérifier les permissions.
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();


app.Run();

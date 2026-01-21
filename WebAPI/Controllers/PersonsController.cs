using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace WebAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PersonsController : ControllerBase
{
    private static readonly List<Person> _persons = [
        new Person() { Id = 1, Name = "Quentin" }
    ];

    [HttpGet]
    public async Task<IActionResult> GetUsers ()
    {
        return Ok(_persons);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetUserById (int id)
    {
        return Ok(_persons.Find(p => p.Id == id));
    }

    // ===== ENDPOINT PUBLIC =====
    // Limite : 5 requêtes / 30 secondes
    /// <summary>
    /// Endpoint PUBLIC - Très restrictif (5 req/30s)
    /// </summary>
    [EnableRateLimiting("public")]
    [HttpGet("public")]
    public IActionResult TestPublic()
    {
        return Ok(new
        {
            politique = "PUBLIC",
            limite = "5 requêtes par 30 secondes",
            timestamp = DateTime.Now,
            message = "Cet endpoint est accessible à tous mais très limité",
            conseil = "Testez en envoyant plus de 5 requêtes en 30 secondes pour voir le 429"
        });
    }

    // ===== ENDPOINT AUTH =====
    // Limite : 3 requêtes / 1 minute
    /// <summary>
    /// Endpoint AUTH - Très strict (3 req/min) - Simule login/register
    /// </summary>
    [EnableRateLimiting("auth")]
    [HttpGet("auth")]
    public IActionResult TestAuth()
    {
        return Ok(new
        {
            politique = "AUTH",
            limite = "3 requêtes par minute",
            timestamp = DateTime.Now,
            message = "Endpoint de type authentification - Très restrictif",
            conseil = "Testez en envoyant plus de 3 requêtes en 1 minute pour voir le 429"
        });
    }

    // ===== ENDPOINT AUTHENTICATED =====
    // Limite : 20 requêtes / 30 secondes + file d'attente de 2
    /// <summary>
    /// Endpoint AUTHENTICATED - Plus souple (20 req/30s + queue de 2)
    /// </summary>
    [EnableRateLimiting("authenticated")]
    [HttpGet("authenticated")]
    public IActionResult TestAuthenticated()
    {
        return Ok(new
        {
            politique = "AUTHENTICATED",
            limite = "20 requêtes par 30 secondes",
            queueLimit = "2 requêtes en file d'attente",
            timestamp = DateTime.Now,
            message = "Endpoint pour utilisateurs authentifiés - Plus permissif",
            conseil = "Testez en envoyant plus de 20 requêtes rapidement. Les 2 suivantes seront en queue."
        });
    }

    // ===== ENDPOINT SANS LIMITATION (pour comparaison) =====
    /// <summary>
    /// Endpoint sans rate limiting - Pour comparaison
    /// </summary>
    [HttpGet("unlimited")]
    public IActionResult TestUnlimited()
    {
        return Ok(new
        {
            politique = "AUCUNE",
            limite = "Illimité",
            timestamp = DateTime.Now,
            message = "Cet endpoint n'a aucune limitation",
            conseil = "Vous pouvez envoyer autant de requêtes que vous voulez"
        });
    }

    // ===== ENDPOINT AVEC INFORMATIONS DE STATUS =====
    /// <summary>
    /// Retourne les informations sur les limites de rate limiting
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            politiques = new[]
            {
                    new { nom = "public", limite = "5 requêtes / 30 secondes", endpoint = "/api/RateLimitTest/public" },
                    new { nom = "auth", limite = "3 requêtes / 1 minute", endpoint = "/api/RateLimitTest/auth" },
                    new { nom = "authenticated", limite = "20 requêtes / 30 secondes + queue de 2", endpoint = "/api/RateLimitTest/authenticated" },
                    new { nom = "unlimited", limite = "Aucune", endpoint = "/api/RateLimitTest/unlimited" }
                },
            conseilsDeTest = new[]
            {
                    "Utilisez Swagger, Postman ou curl pour tester",
                    "Envoyez plusieurs requêtes rapidement pour dépasser les limites",
                    "Observez le code de statut 429 Too Many Requests",
                    "Regardez le header 'Retry-After' dans la réponse 429"
                }
        });
    }

}

public class Person
{
    public int Id { get; set; }
    public string Name { get; set; }
}
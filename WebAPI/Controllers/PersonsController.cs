using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebAPI.Models;

namespace WebAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PersonsController : ControllerBase
{
    private static readonly List<Person> _persons = new()
    {
        new Person() { Id = 1,  Lastname = "Claes",             Firstname = "Alexandre" },
        new Person() { Id = 2,  Lastname = "Beurive",           Firstname = "Aude"      },
        new Person() { Id = 3,  Lastname = "Strimelle",         Firstname = "Aurélien"  },
        new Person() { Id = 4,  Lastname = "Herssens",          Firstname = "Caroline"  },
        new Person() { Id = 5,  Lastname = "Ukanda",            Firstname = "Didier"    },
        new Person() { Id = 6,  Lastname = "Ovyn",              Firstname = "Flavian"   },
        new Person() { Id = 7,  Lastname = "Chaineux",          Firstname = "Gavin"     },
        new Person() { Id = 8,  Lastname = "Vandermeerschen",   Firstname = "Georges"   },
        new Person() { Id = 9,  Lastname = "Ly",                Firstname = "Khun"      },
        new Person() { Id = 10, Lastname = "Fontaine",          Firstname = "Laurent"   },
        new Person() { Id = 11, Lastname = "Fivez",             Firstname = "Mauritcio" },
        new Person() { Id = 12, Lastname = "Geerts",            Firstname = "Mélanie"   },
        new Person() { Id = 13, Lastname = "Person",            Firstname = "Michaël"   },
        new Person() { Id = 14, Lastname = "Haerens",           Firstname = "Philippe"  },
        new Person() { Id = 15, Lastname = "Geerts",            Firstname = "Quentin"   },
        new Person() { Id = 16, Lastname = "Pêcheur",           Firstname = "Robin"     },
        new Person() { Id = 17, Lastname = "Bernard",           Firstname = "Romain"    },
        new Person() { Id = 18, Lastname = "Wauman",            Firstname = "Romain"    },
        new Person() { Id = 19, Lastname = "Legrain",           Firstname = "Samuel"    },
        new Person() { Id = 20, Lastname = "Bya",               Firstname = "Sébastien" },
        new Person() { Id = 21, Lastname = "Morre",             Firstname = "Thierry"   },
        new Person() { Id = 22, Lastname = "Boualem",           Firstname = "Zachary"   }
    };

    // 1. FIXED WINDOW — GET /api/persons/fixed-window

    // Règle : 5 requêtes max par fenêtre de 10 secondes.
    // La fenêtre repart à ZÉRO toutes les 10s, peu importe quand les
    // requêtes ont été effectuées dans l'intervalle.
    //
    // Comment tester :
    //   Envoyer 6 requêtes d'affilée → la 6e reçoit HTTP 429.
    //   Attendre que la fenêtre expire (10s) → les 5 slots sont restaurés
    [EnableRateLimiting("FixedWindow")]
    [HttpGet("fixed-window")]
    public ActionResult<IEnumerable<Person>> GetAllPersonsFixedWindow()
    {
        return Ok(_persons);
    }

    // 2. SLIDING WINDOW — GET /api/persons/sliding-window

    // Règle : 5 requêtes max sur une fenêtre glissante de 10 secondes
    //         découpée en 2 segments (1 segment = 5s).
    // Les slots se libèrent au fil du glissement : à chaque nouveau segment,
    // les requêtes du segment le plus ancien redeviennent disponibles.
    //
    // Différence avec Fixed Window :
    //   • Fixed Window : 5 requêtes à t=0s → recharge brutale à t=10s.
    //   • Sliding Window : les slots se libèrent progressivement (à t=5s,
    //     les requêtes faites avant t=-5s redeviennent disponibles).
    //
    // Comment tester :
    //   Envoyer 5 requêtes → attendre 5s → 2-3 slots disponibles à nouveau.
    //   Vs Fixed Window où il faut attendre les 10s complètes.
    [EnableRateLimiting("SlidingWindow")]
    [HttpGet("sliding-window")]
    public ActionResult<IEnumerable<Person>> GetAllPersonsSlidingWindow()
    {
        return Ok(_persons);
    }

    // 3. TOKEN BUCKET — GET /api/persons/token-bucket

    // Règle : seau de 5 jetons max, rechargé de +2 jetons toutes les 5s.
    // Chaque requête consomme 1 jeton. Si le seau est vide → HTTP 429.
    //
    // Points clés :
    //   • Permet un BURST initial (5 requêtes immédiates si seau plein).
    //   • Lisse le débit sur la durée (~2 req / 5s en régime permanent).
    //   • Les jetons s'accumulent (jusqu'à la limite de 5) pendant les
    //     périodes de silence.
    //
    // Comment tester :
    //   a) Burst : envoyer 5 requêtes d'affilée → toutes passent.
    //      La 6e reçoit un 429.
    //   b) Recharge : attendre 5s → 2 nouveaux jetons → 2 requêtes passent.
    //   c) Accumulation : ne rien envoyer pendant 15s → seau plein (5 jetons).
    [EnableRateLimiting("TokenBucket")]
    [HttpGet("token-bucket")]
    public ActionResult<IEnumerable<Person>> GetAllPersonsTokenBucket()
    {
        return Ok(_persons);
    }

    // 4. CONCURRENCY — GET /api/persons/concurrency

    // Règle : maximum 2 requêtes traitées EN MÊME TEMPS.
    // Contrairement aux autres algorithmes, ce n'est PAS une limite de débit
    // dans le temps, mais une limite du nombre de traitements simultanés.
    // Un slot est libéré dès que la requête se termine.
    //
    // Utilité :
    //   Protéger des ressources partagées (pool DB, API tierce lente)
    //   contre la surcharge, indépendamment du volume temporel.
    //
    // Comment tester :
    //   Envoyer 3 requêtes simultanément (ex: via Postman Runner ou curl -Z) :
    //   → 2 requêtes sont traitées (chacune attend 3s à cause du Task.Delay)
    //   → La 3e reçoit immédiatement un 429
    //   → Une fois qu'une des 2 premières se termine, un nouveau slot se libère.
    //
    // Note : le Task.Delay(3s) simule un traitement lent pour rendre la
    //        démonstration observable (ex : appel BDD, appel API tierce).

    [EnableRateLimiting("Concurrency")]
    [HttpGet("concurrency")]
    public async Task<ActionResult<IEnumerable<Person>>> GetAllPersonsConcurrency()
    {
        // Simulation d'un traitement lent (ex : requête DB, appel API externe)
        // afin de rendre la limite de concurrence observable lors de la démo.
        await Task.Delay(TimeSpan.FromSeconds(3));

        return Ok(_persons);
    }

    // 5. DISABLE RATE LIMITING — GET /api/persons/no-limit

    [HttpGet("global-limiter")]
    public ActionResult<IEnumerable<Person>> GetAllPersonsGlobalLimit()
    {
        return Ok(_persons);
    }

    // [DisableRateLimiting] désactive TOUS les limiteurs pour cet endpoint :
    //   • Le GlobalLimiter (100 req/min/IP) est ignoré.
    //   • Toute politique héritée du contrôleur serait également ignorée.
    //
    // Cas d'usage typiques :
    //   • Endpoint de health check (sonde Kubernetes, load balancer)
    //   • Route interne appelée uniquement depuis un réseau de confiance
    //   • Webhook entrant dont la source est déjà authentifiée et limitée
    //     en amont (ex : GitHub, Stripe)
    //
    // ⚠ À utiliser avec discernement : cet endpoint est entièrement exposé,
    //   sans aucun filet de sécurité côté débit.
    [DisableRateLimiting]
    [HttpGet("no-limit")]
    public ActionResult<IEnumerable<Person>> GetAllPersonsNoLimit()
    {
        return Ok(_persons);
    }
}

using Dragonfire.TraceKit.SampleApp.Clients;
using Microsoft.AspNetCore.Mvc;

namespace Dragonfire.TraceKit.SampleApp.Controllers;

/// <summary>
/// Demo B2B endpoints. Every outbound HttpClient call is captured automatically — no
/// per-call code is required to participate in TraceKit tracing.
/// </summary>
[ApiController]
[Route("api/demo")]
public sealed class DemoApiController : ControllerBase
{
    private readonly JsonPlaceholderClient _placeholder;
    private readonly CatFactsClient _cats;

    public DemoApiController(JsonPlaceholderClient placeholder, CatFactsClient cats)
    {
        _placeholder = placeholder;
        _cats = cats;
    }

    /// <summary>Single outbound call — the simplest case.</summary>
    [HttpGet("posts/{id:int}")]
    public async Task<IActionResult> GetPost(int id, CancellationToken ct)
    {
        var post = await _placeholder.GetPostAsync(id, ct);
        return post is null ? NotFound() : Ok(post);
    }

    /// <summary>Two sequential calls — sequence numbers will be 1, 2.</summary>
    [HttpGet("posts/{id:int}/full")]
    public async Task<IActionResult> GetPostWithAuthor(int id, CancellationToken ct)
    {
        var post = await _placeholder.GetPostAsync(id, ct);
        if (post is null) return NotFound();

        var author = await _placeholder.GetUserAsync(post.UserId, ct);
        return Ok(new { post, author });
    }

    /// <summary>
    /// Parallel fan-out: three outbound calls run concurrently. Sequence numbers (1, 2, 3)
    /// are allocated atomically so the timeline reflects the real start order even under
    /// thread races.
    /// </summary>
    [HttpGet("dashboard/{userId:int}")]
    public async Task<IActionResult> GetDashboard(int userId, CancellationToken ct)
    {
        var user = _placeholder.GetUserAsync(userId, ct);
        var firstPost = _placeholder.GetPostAsync(1, ct);
        var fact = _cats.GetRandomFactAsync(ct);

        await Task.WhenAll(user, firstPost, fact);

        return Ok(new { user = user.Result, firstPost = firstPost.Result, catFact = fact.Result });
    }

    /// <summary>POST that exercises body capture and JSON redaction (note the secret field).</summary>
    [HttpPost("echo")]
    public async Task<IActionResult> Echo([FromBody] EchoRequest body, CancellationToken ct)
    {
        var posted = new JsonPlaceholderClient.Post(0, 1, body.Title, body.Body);
        using var response = await _placeholder.EchoPostAsync(posted, ct);
        return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync(ct));
    }

    public sealed record EchoRequest(string Title, string Body, string? ApiKey, string? Password);
}

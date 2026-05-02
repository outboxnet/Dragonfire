using System.Net.Http.Json;

namespace Dragonfire.TraceKit.SampleApp.Clients;

/// <summary>
/// Typed client for https://jsonplaceholder.typicode.com — a free fake REST API.
/// </summary>
public sealed class JsonPlaceholderClient
{
    private readonly HttpClient _http;

    public JsonPlaceholderClient(HttpClient http) => _http = http;

    public Task<Post?> GetPostAsync(int id, CancellationToken ct = default)
        => _http.GetFromJsonAsync<Post>($"posts/{id}", ct);

    public Task<User?> GetUserAsync(int id, CancellationToken ct = default)
        => _http.GetFromJsonAsync<User>($"users/{id}", ct);

    public Task<HttpResponseMessage> EchoPostAsync(Post post, CancellationToken ct = default)
        => _http.PostAsJsonAsync("posts", post, ct);

    public sealed record Post(int Id, int UserId, string Title, string Body);
    public sealed record User(int Id, string Name, string Email, string Username);
}

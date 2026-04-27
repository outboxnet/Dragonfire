using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker.Http;

namespace InboxNet.AzureFunctions;

/// <summary>
/// Abstract base class for an Azure Functions Isolated Worker class that hosts both
/// the webhook HTTP trigger and the dispatch timer trigger.
/// <para>
/// Inherit this class, inject <see cref="IInboxFunctionHandler"/> into the constructor,
/// and declare your function methods — setting your own route pattern and timer cron:
/// </para>
/// <code>
/// public class MyFunctions : InboxFunctionBase
/// {
///     public MyFunctions(IInboxFunctionHandler handler) : base(handler) { }
///
///     [Function("InboxReceive")]
///     public Task&lt;HttpResponseData&gt; Receive(
///         [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "webhooks/{providerKey}")]
///         HttpRequestData req,
///         string providerKey,
///         CancellationToken ct)
///         => ReceiveAsync(req, providerKey, ct);
///
///     [Function("InboxDispatch")]
///     public Task Dispatch(
///         [TimerTrigger("%InboxNet:DispatchCron%")] TimerInfo _,
///         CancellationToken ct)
///         => DispatchAsync(ct);
/// }
/// </code>
/// <para>
/// Set <c>InboxNet:DispatchCron</c> in <c>local.settings.json</c> (or Azure App Settings)
/// to control the timer frequency, e.g. <c>"*/30 * * * * *"</c> for every 30 seconds.
/// </para>
/// </summary>
public abstract class InboxFunctionBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IInboxFunctionHandler _handler;

    protected InboxFunctionBase(IInboxFunctionHandler handler)
    {
        _handler = handler;
    }

    /// <summary>
    /// Handles an incoming webhook HTTP request. Reads the raw body once, validates the
    /// provider signature, deduplicates, and persists. Call from your <c>[HttpTrigger]</c> method.
    /// </summary>
    protected async Task<HttpResponseData> ReceiveAsync(
        HttpRequestData req,
        string providerKey,
        CancellationToken ct)
    {
        string rawBody;
        using (var reader = new StreamReader(req.Body, Encoding.UTF8, leaveOpen: true))
            rawBody = await reader.ReadToEndAsync(ct);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in req.Headers)
            headers[header.Key] = string.Join(",", header.Value);

        var result = await _handler.ReceiveAsync(providerKey, rawBody, headers, ct);

        return result.Status switch
        {
            InboxReceiveStatus.Accepted =>
                await JsonResponse(req, HttpStatusCode.Accepted,
                    new { messageId = result.MessageId }),

            InboxReceiveStatus.Duplicate =>
                await JsonResponse(req, HttpStatusCode.OK,
                    new { messageId = result.MessageId, duplicate = true }),

            InboxReceiveStatus.Invalid =>
                await JsonResponse(req, HttpStatusCode.BadRequest,
                    new { error = result.InvalidReason ?? "Invalid webhook" }),

            InboxReceiveStatus.ProviderNotFound =>
                await JsonResponse(req, HttpStatusCode.NotFound,
                    new { error = $"Unknown provider '{providerKey}'" }),

            _ => req.CreateResponse(HttpStatusCode.InternalServerError)
        };
    }

    /// <summary>
    /// Runs one dispatch cycle: locks pending inbox messages and runs their handlers.
    /// Call from your <c>[TimerTrigger]</c> method.
    /// </summary>
    protected Task DispatchAsync(CancellationToken ct) =>
        _handler.DispatchAsync(ct);

    private static async Task<HttpResponseData> JsonResponse(
        HttpRequestData req, HttpStatusCode status, object body)
    {
        var response = req.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        var json = JsonSerializer.Serialize(body, JsonOptions);
        await response.Body.WriteAsync(Encoding.UTF8.GetBytes(json));
        return response;
    }
}

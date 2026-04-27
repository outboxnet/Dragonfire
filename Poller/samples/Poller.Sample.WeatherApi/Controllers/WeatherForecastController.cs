using Microsoft.AspNetCore.Mvc;
using Poller.Sample.WeatherApi.Weather;
using Poller.Services;

namespace Poller.Sample.WeatherApi.Controllers
{
    [ApiController]
    [Route("api/weather")]
    [Produces("application/json")]
    public class WeatherForecastController : ControllerBase
    {
        private readonly IPollingOrchestrator _orchestrator;

        public WeatherForecastController(IPollingOrchestrator orchestrator)
            => _orchestrator = orchestrator;

        /// <summary>Start a weather fetch job for the given coordinates.</summary>
        /// <remarks>
        /// Enqueues an asynchronous polling job that fetches current weather from
        /// the free Open-Meteo API. Returns a <c>pollingId</c> immediately — the
        /// actual data arrives when you poll <c>GET /api/weather/{pollingId}</c>.
        ///
        /// **Example locations**
        ///
        /// | City      | Latitude | Longitude |
        /// |-----------|----------|-----------|
        /// | Berlin    | 52.52    | 13.41     |
        /// | London    | 51.51    | -0.13     |
        /// | New York  | 40.71    | -74.01    |
        /// | Tokyo     | 35.68    | 139.69    |
        /// | Sydney    | -33.87   | 151.21    |
        /// </remarks>
        /// <param name="jobRequest">Coordinates and optional location label.</param>
        /// <response code="202">Job accepted — use <c>statusUrl</c> to track progress.</response>
        /// <response code="400">Invalid request body.</response>
        [HttpPost]
        [ProducesResponseType(typeof(StartWeatherJobResponse), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> StartWeatherJob(
            [FromBody] WeatherJobRequest jobRequest,
            CancellationToken cancellationToken)
        {
            var pollingRequest = new WeatherPollingRequest
            {
                Latitude     = jobRequest.Latitude,
                Longitude    = jobRequest.Longitude,
                LocationName = jobRequest.LocationName
                               ?? $"{jobRequest.Latitude},{jobRequest.Longitude}"
            };

            var response = await _orchestrator.StartPollingAsync<WeatherPollingRequest, WeatherPollingResponse>(
                pollingType: "WeatherFetch",
                request: pollingRequest,
                options: new PollingOptions
                {
                    MaxAttempts  = 5,
                    InitialDelay = TimeSpan.FromSeconds(2),
                    MaxDelay     = TimeSpan.FromSeconds(15),
                    Timeout      = TimeSpan.FromMinutes(2)
                },
                cancellationToken: cancellationToken);

            return Accepted(new StartWeatherJobResponse
            {
                PollingId = response.PollingId,
                StatusUrl = $"/api/weather/{response.PollingId}",
                Message   = "Weather fetch job started. Poll the status URL for results."
            });
        }

        /// <summary>Get the status and result of a weather fetch job.</summary>
        /// <remarks>
        /// Keep calling this endpoint until <c>status</c> is <c>Completed</c>, <c>Failed</c>,
        /// or <c>TimedOut</c>. On success the <c>weather</c> field is populated with current
        /// conditions for the requested location.
        ///
        /// **Status values**
        ///
        /// | Value       | Meaning                                   |
        /// |-------------|-------------------------------------------|
        /// | `Pending`   | Job is queued, not yet started            |
        /// | `Polling`   | Actively fetching from Open-Meteo         |
        /// | `Completed` | Data retrieved — read the `weather` field |
        /// | `Failed`    | All retry attempts exhausted              |
        /// | `TimedOut`  | Overall timeout exceeded                  |
        /// | `Cancelled` | Cancelled via `DELETE /api/weather/{id}`  |
        /// </remarks>
        /// <param name="pollingId">The ID returned by <c>POST /api/weather</c>.</param>
        /// <response code="200">Current job status (check <c>status</c> field).</response>
        /// <response code="404">No job found for this ID.</response>
        [HttpGet("{pollingId:guid}")]
        [ProducesResponseType(typeof(WeatherJobStatusResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetJobStatus(
            Guid pollingId,
            CancellationToken cancellationToken)
        {
            var status = await _orchestrator.GetStatusAsync(pollingId, cancellationToken);

            if (status is null)
                return NotFound(new { message = $"Polling job {pollingId} not found." });

            WeatherPollingResponse? weather = status.Result as WeatherPollingResponse;

            return Ok(new WeatherJobStatusResponse
            {
                PollingId = pollingId,
                Status    = status.Status.ToString(),
                Attempts  = status.Attempts,
                Duration  = status.Duration,
                Error     = status.FailureReason,
                Weather   = weather is null ? null : new WeatherResult
                {
                    LocationName              = weather.LocationName,
                    TemperatureCelsius        = weather.TemperatureCelsius,
                    PrecipitationMm           = weather.PrecipitationMm,
                    MaxRainProbabilityPercent = weather.MaxRainProbabilityPercent,
                    Condition                 = weather.Condition,
                    FetchedAt                 = weather.FetchedAt
                }
            });
        }

        /// <summary>Cancel a running weather fetch job.</summary>
        /// <param name="pollingId">The ID returned by <c>POST /api/weather</c>.</param>
        /// <response code="200">Job was cancelled.</response>
        /// <response code="404">Job not found or already completed.</response>
        [HttpDelete("{pollingId:guid}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CancelJob(
            Guid pollingId,
            CancellationToken cancellationToken)
        {
            var cancelled = await _orchestrator.CancelPollingAsync(pollingId, cancellationToken);
            return cancelled
                ? Ok(new { message = "Job cancelled." })
                : NotFound(new { message = $"Job {pollingId} not found or already completed." });
        }

        /// <summary>Stream live polling updates as Server-Sent Events (SSE).</summary>
        /// <remarks>
        /// Opens a long-lived HTTP connection and pushes a JSON event for each polling
        /// attempt until the job finishes. Useful for live UI updates.
        ///
        /// Note: Swagger UI cannot display SSE streams — use `curl` or an EventSource client:
        ///
        ///     curl -N http://localhost:5000/api/weather/{pollingId}/stream
        /// </remarks>
        /// <param name="pollingId">The ID returned by <c>POST /api/weather</c>.</param>
        [HttpGet("{pollingId:guid}/stream")]
        [Produces("text/event-stream")]
        public async Task StreamUpdates(Guid pollingId, CancellationToken cancellationToken)
        {
            Response.Headers["Content-Type"]  = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["Connection"]    = "keep-alive";

            await foreach (var update in _orchestrator.SubscribeToUpdatesAsync(pollingId, cancellationToken))
            {
                var line = $"data: {{\"status\":\"{update.Status}\"," +
                           $"\"attempt\":{update.AttemptNumber}," +
                           $"\"message\":\"{update.Message}\"," +
                           $"\"timestamp\":\"{update.Timestamp:O}\"}}\n\n";

                await Response.WriteAsync(line, cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);

                if (update.Status.ToString() is "Completed" or "Failed" or "TimedOut" or "Cancelled")
                    break;
            }
        }
    }

    // ── Request / Response DTOs ───────────────────────────────────────────────

    /// <summary>Coordinates and optional label for the weather fetch job.</summary>
    public class WeatherJobRequest
    {
        /// <example>52.52</example>
        public double Latitude { get; set; }

        /// <example>13.41</example>
        public double Longitude { get; set; }

        /// <example>Berlin</example>
        public string? LocationName { get; set; }
    }

    /// <summary>Immediate response after starting a job.</summary>
    public class StartWeatherJobResponse
    {
        /// <summary>Use this ID to check status or cancel the job.</summary>
        public Guid PollingId { get; set; }

        /// <summary>Convenience URL to poll for status.</summary>
        public string StatusUrl { get; set; } = "";

        public string Message { get; set; } = "";
    }

    /// <summary>Current state of a weather fetch job.</summary>
    public class WeatherJobStatusResponse
    {
        public Guid PollingId { get; set; }

        /// <summary>Pending | Polling | Completed | Failed | TimedOut | Cancelled</summary>
        public string Status { get; set; } = "";

        /// <summary>Number of polling attempts made so far.</summary>
        public int Attempts { get; set; }

        /// <summary>Total elapsed time (null if still running).</summary>
        public TimeSpan? Duration { get; set; }

        /// <summary>Failure message if <c>Status</c> is <c>Failed</c> or <c>TimedOut</c>.</summary>
        public string? Error { get; set; }

        /// <summary>Populated when <c>Status</c> is <c>Completed</c>.</summary>
        public WeatherResult? Weather { get; set; }
    }

    /// <summary>Current weather conditions from Open-Meteo.</summary>
    public class WeatherResult
    {
        public string LocationName { get; set; } = "";

        /// <summary>Current air temperature in °C.</summary>
        public decimal TemperatureCelsius { get; set; }

        /// <summary>Precipitation in mm for the current hour.</summary>
        public decimal PrecipitationMm { get; set; }

        /// <summary>Maximum rain probability (%) over the next 12 hours.</summary>
        public int MaxRainProbabilityPercent { get; set; }

        /// <summary>Human-readable weather description (e.g. "Partly cloudy").</summary>
        public string Condition { get; set; } = "";

        public DateTime FetchedAt { get; set; }
    }
}

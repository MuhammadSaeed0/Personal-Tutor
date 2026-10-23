using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Riwaq.Api.Infrastructure;
using Riwaq.Application.Common;
using Riwaq.Application.Features.Account;
using Riwaq.Application.Features.Analytics;
using Riwaq.Application.Features.Dashboard;
using Riwaq.Application.Features.Planner;
using Riwaq.Application.Features.Sessions;
using Riwaq.Domain;
using Riwaq.Domain.Entities;

namespace Riwaq.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class DashboardController(DashboardService dashboard, AnalyticsService analytics) : ControllerBase
{
    [HttpGet("dashboard")]
    public Task<DashboardDto> Get(CancellationToken ct) => dashboard.GetAsync(User.GetUserId(), ct);

    [HttpGet("achievements")]
    public Task<IReadOnlyList<AchievementDto>> Achievements(CancellationToken ct) => dashboard.GetAchievementsAsync(User.GetUserId(), ct);

    [HttpGet("analytics/overview")]
    public Task<OverviewDto> Overview([FromQuery] int? subjectId, CancellationToken ct) => analytics.GetOverviewAsync(User.GetUserId(), subjectId, ct);

    [HttpGet("analytics/subjects")]
    public Task<IReadOnlyList<SubjectStatsDto>> Subjects(CancellationToken ct) => analytics.GetSubjectStatsAsync(User.GetUserId(), ct);

    [HttpGet("analytics/topics")]
    public Task<IReadOnlyList<TopicStatDto>> Topics([FromQuery] int? subjectId, CancellationToken ct) => analytics.GetTopicsAsync(User.GetUserId(), subjectId, ct);
}

[ApiController]
[Route("api/plan")]
public sealed class PlanController(PlannerService planner) : ControllerBase
{
    [HttpGet]
    public Task<PlanDto> Get(CancellationToken ct) => planner.GetAsync(User.GetUserId(), ct);

    [HttpPost("generate")]
    public Task<PlanDto> Generate(CancellationToken ct) => planner.GenerateAsync(User.GetUserId(), ct);

    [HttpPatch("tasks/{id:guid}")]
    public Task<StudyTaskDto> UpdateTask(Guid id, UpdateTaskRequest request, CancellationToken ct) => planner.UpdateTaskAsync(User.GetUserId(), id, request, ct);
}

[ApiController]
[Route("api")]
public sealed class SessionsController(SessionsService sessions) : ControllerBase
{
    [HttpGet("sessions")]
    public Task<PagedResult<SessionDto>> List([FromQuery] StudySessionType? type, [FromQuery] int page = 1, [FromQuery] int pageSize = 30, CancellationToken ct = default) =>
        sessions.ListAsync(User.GetUserId(), type, page, pageSize, ct);

    [HttpPost("sessions")]
    public Task<SessionDto> Start(StartSessionRequest request, CancellationToken ct) => sessions.StartAsync(User.GetUserId(), request, ct);

    [HttpPost("sessions/{id:guid}/complete")]
    public Task<SessionDto> Complete(Guid id, CompleteSessionRequest request, CancellationToken ct) => sessions.CompleteAsync(User.GetUserId(), id, request, ct);

    [HttpDelete("sessions/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await sessions.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }

    [HttpGet("notifications")]
    public Task<IReadOnlyList<NotificationDto>> Notifications(CancellationToken ct) => sessions.GetNotificationsAsync(User.GetUserId(), ct);

    [HttpPost("notifications/read")]
    public async Task<IActionResult> MarkRead(CancellationToken ct)
    {
        await sessions.MarkNotificationsReadAsync(User.GetUserId(), ct);
        return NoContent();
    }
}

[ApiController]
[Route("api")]
public sealed class AccountController(AccountService account, UserManager<AppUser> users) : ControllerBase
{
    [HttpPost("search")]
    public Task<SearchResultDto> Search(SearchRequest request, CancellationToken ct) => account.SearchAsync(User.GetUserId(), request, ct);

    [HttpGet("account/export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var data = await account.ExportAsync(User.GetUserId(), ct);
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true });
        return File(json, "application/json", "riwaq-export.json");
    }

    public sealed record DeleteAccountRequest(string Password);

    /// <summary>Permanently deletes the account, all study data and uploaded files. Requires the current password.</summary>
    [HttpPost("account/delete")]
    public async Task<IActionResult> Delete(DeleteAccountRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(User.GetUserId().ToString()) ?? throw new NotFoundException("User");
        if (!await users.CheckPasswordAsync(user, request.Password))
            throw new Application.Common.ValidationException("wrong_password", "The password is incorrect.");
        await account.DeleteAllDataAsync(user.Id, ct);
        await users.DeleteAsync(user);
        Response.Cookies.Delete(Auth.AuthController.RefreshCookie, new CookieOptions { Path = "/api/auth" });
        return NoContent();
    }
}

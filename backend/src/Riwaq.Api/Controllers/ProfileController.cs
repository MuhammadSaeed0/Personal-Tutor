using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Riwaq.Api.Infrastructure;
using Riwaq.Application.Features.Profile;

namespace Riwaq.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class ProfileController(ProfileService profiles) : ControllerBase
{
    [HttpGet("me")]
    public Task<ProfileDto> Me(CancellationToken ct) => profiles.GetAsync(User.GetUserId(), ct);

    [HttpPatch("me")]
    public Task<ProfileDto> UpdateMe(UpdateProfileRequest request, CancellationToken ct) =>
        profiles.UpdateProfileAsync(User.GetUserId(), request, ct);

    [HttpPatch("me/preferences")]
    public Task<PreferencesDto> UpdatePreferences(UpdatePreferencesRequest request, CancellationToken ct) =>
        profiles.UpdatePreferencesAsync(User.GetUserId(), request, ct);

    [HttpPatch("me/academic")]
    public Task<ProfileDto> UpdateAcademic(UpdateAcademicRequest request, CancellationToken ct) =>
        profiles.UpdateAcademicAsync(User.GetUserId(), request, ct);

    [HttpPut("me/onboarding")]
    public Task<ProfileDto> Onboarding(OnboardingRequest request, CancellationToken ct) =>
        profiles.CompleteOnboardingAsync(User.GetUserId(), request, ct);

    [AllowAnonymous]
    [HttpGet("subjects")]
    [ResponseCache(Duration = 300)]
    public Task<IReadOnlyList<SubjectDto>> Subjects(CancellationToken ct) => profiles.GetSubjectsAsync(ct);

    [HttpGet("exams")]
    public Task<IReadOnlyList<ExamDto>> Exams([FromQuery] bool upcoming, CancellationToken ct) =>
        profiles.GetExamsAsync(User.GetUserId(), upcoming, ct);

    [HttpPost("exams")]
    public Task<ExamDto> AddExam(ExamInput input, CancellationToken ct) => profiles.AddExamAsync(User.GetUserId(), input, ct);

    [HttpPut("exams/{id:guid}")]
    public Task<ExamDto> UpdateExam(Guid id, ExamInput input, CancellationToken ct) => profiles.UpdateExamAsync(User.GetUserId(), id, input, ct);

    [HttpDelete("exams/{id:guid}")]
    public async Task<IActionResult> DeleteExam(Guid id, CancellationToken ct)
    {
        await profiles.DeleteExamAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
}

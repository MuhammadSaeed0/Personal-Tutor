using Riwaq.Application.Abstractions;
using Riwaq.Application.Common;

namespace Riwaq.Tests.Support;

/// <summary>Scripted YouTube: one Arabic lesson with captions, one video without captions, everything else unavailable.</summary>
public sealed class FakeVideoSource : IVideoTranscriptSource
{
    public const string LessonId = "AAAAAAAAAAA";
    public const string SilentId = "BBBBBBBBBBB";

    public static readonly IReadOnlyList<VideoCaption> LessonCaptions = Enumerable.Range(0, 40)
        .Select(i => new VideoCaption(i * 10, 10, i < 20
            ? $"المعدن مادة صلبة متجانسة تتكون طبيعيا جملة رقم {i}"
            : $"الصخور النارية تتكون من تبلور الصهارة جملة رقم {i}"))
        .ToList();

    public Task<VideoDetails> GetDetailsAsync(string videoId, CancellationToken cancellationToken) => videoId switch
    {
        LessonId => Task.FromResult(new VideoDetails(videoId, "درس المعادن والصخور", "قناة الجيولوجيا", 400)),
        SilentId => Task.FromResult(new VideoDetails(videoId, "Silent video", "Someone", 60)),
        _ => throw new ValidationException("video_unavailable", "This video is private, removed or cannot be read.")
    };

    public Task<VideoTranscript?> GetTranscriptAsync(string videoId, string preferredLanguage, CancellationToken cancellationToken) =>
        Task.FromResult(videoId == LessonId ? new VideoTranscript("ar", true, LessonCaptions) : null);
}

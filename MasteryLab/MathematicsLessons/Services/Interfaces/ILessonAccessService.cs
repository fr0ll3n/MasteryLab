using MathematicsLessons.Infrastructure.Data.Entities;

namespace MathematicsLessons.Services.Interfaces;

/// Access rules: subscribers open everything; free users get a fixed video quota.
public interface ILessonAccessService
{
    const int FreeVideoLimit = 3;

    /// Subscribers: always true. Free: true if lesson already in quota or quota not full.
    bool CanOpenLesson(bool isSubscribed, int lessonId, IReadOnlySet<int> startedLessonIds);

    /// Levels are never locked by sequence — only by free quota at lesson level.
    bool IsLevelVisible(bool isSubscribed, Level level);
}

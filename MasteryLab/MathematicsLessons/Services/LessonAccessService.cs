using MathematicsLessons.Infrastructure.Data.Entities;
using MathematicsLessons.Services.Interfaces;

namespace MathematicsLessons.Services;

public class LessonAccessService : ILessonAccessService
{
    public bool CanOpenLesson(bool isSubscribed, int lessonId, IReadOnlySet<int> startedLessonIds)
    {
        if (isSubscribed) return true;
        if (startedLessonIds.Contains(lessonId)) return true;
        return startedLessonIds.Count < ILessonAccessService.FreeVideoLimit;
    }

    public bool IsLevelVisible(bool isSubscribed, Level level) => true;
}

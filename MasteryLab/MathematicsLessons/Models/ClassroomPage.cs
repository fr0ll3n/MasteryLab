namespace MathematicsLessons.Models;

public class ClassroomPage
{
    public List<LevelBlock> Levels { get; set; } = new();
    public bool IsSubscribed { get; set; }
    public int TotalCompleted { get; set; }
    public int TotalLessons { get; set; }
    public string? SearchQuery { get; set; }
    public List<SearchHit> SearchHits { get; set; } = new();
}

public class SearchHit
{
    public int LessonId { get; set; }
    public string LessonTitle { get; set; } = "";
    public string LevelName { get; set; } = "";
    public string TopicName { get; set; } = "";
    public bool IsUnlocked { get; set; }
    public bool IsCompleted { get; set; }
}

public class LevelBlock
{
    public MathematicsLessons.Infrastructure.Data.Entities.Level Level { get; set; } = null!;
    public bool IsUnlocked { get; set; }
    public int Completed { get; set; }
    public int Total { get; set; }
    public int Percent => Total == 0 ? 0 : (int)Math.Round(100.0 * Completed / Total);
    public bool HasCertificate { get; set; }
    public List<TopicBlock> Topics { get; set; } = new();
}

public class TopicBlock
{
    public MathematicsLessons.Infrastructure.Data.Entities.Topic Topic { get; set; } = null!;
    public List<LessonRow> Lessons { get; set; } = new();
}

public class LessonRow
{
    public MathematicsLessons.Infrastructure.Data.Entities.Lesson Lesson { get; set; } = null!;
    public bool IsUnlocked { get; set; }
    public bool IsCompleted { get; set; }
}

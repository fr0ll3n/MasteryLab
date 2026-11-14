using MathematicsLessons.Infrastructure.Data.Entities;

namespace MathematicsLessons.Models;

public class TeacherDashboard
{
    public int TotalStudents { get; set; }
    public int LessonsWithoutVideo { get; set; }
    public int UpcomingBookings { get; set; }
    public List<Appointment> RecentBookings { get; set; } = new();
    public List<Lesson> MissingVideos { get; set; } = new();
}

public class TeacherStudentRow
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Plan { get; set; } = "Free";
    public int Points { get; set; }
    public int StreakDays { get; set; }
    public int CompletedLessons { get; set; }
    public int TotalLessons { get; set; }
    public DateTime? LastActivity { get; set; }
}

public class TeacherStudentProgressPage
{
    public TeacherStudentRow Student { get; set; } = new();
    public List<LevelProgressRow> Levels { get; set; } = new();
}

public class LevelProgressRow
{
    public string LevelName { get; set; } = "";
    public int LevelId { get; set; }
    public int Completed { get; set; }
    public int Total { get; set; }
    public int Percent => Total == 0 ? 0 : (int)Math.Round(100.0 * Completed / Total);
    public bool HasCertificate { get; set; }
    public List<LessonProgressRow> Lessons { get; set; } = new();
}

public class LessonProgressRow
{
    public string Title { get; set; } = "";
    public string TopicName { get; set; } = "";
    public bool VideoWatched { get; set; }
    public bool ExercisesPassed { get; set; }
    public bool IsCompleted { get; set; }
    public int WatchedPercent { get; set; }
}

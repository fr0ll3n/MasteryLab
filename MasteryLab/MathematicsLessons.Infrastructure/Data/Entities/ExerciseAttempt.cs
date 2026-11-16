using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MathematicsLessons.Infrastructure.Data.Entities;

/// One try at an exercise (for review of weak spots).
public class ExerciseAttempt
{
    [Key]
    public int Id { get; set; }

    [Required, ForeignKey(nameof(User))]
    public string UserId { get; set; } = "";
    public User User { get; set; } = null!;

    [ForeignKey(nameof(Exercise))]
    public int ExerciseId { get; set; }
    public Exercise Exercise { get; set; } = null!;

    [ForeignKey(nameof(Lesson))]
    public int LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;

    [MaxLength(400)]
    public string GivenAnswer { get; set; } = "";

    public bool IsCorrect { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

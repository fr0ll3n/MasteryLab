using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MathematicsLessons.Infrastructure.Data.Entities;

/// Optional assignment: which learners a teacher follows (many-to-many style).
public class TeacherStudent
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(450)]
    public string TeacherId { get; set; } = "";

    [Required, MaxLength(450)]
    public string StudentId { get; set; } = "";

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}

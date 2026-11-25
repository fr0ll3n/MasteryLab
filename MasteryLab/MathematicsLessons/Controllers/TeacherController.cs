using MathematicsLessons.Infrastructure.Data;
using MathematicsLessons.Infrastructure.Data.Entities;
using MathematicsLessons.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MathematicsLessons.Controllers;

/// Teacher/Admin: dashboard, student progress (uses existing User + LessonProgress + Certificate).
[Authorize(Roles = "Teacher,Admin")]
public class TeacherController : Controller
{
    private readonly ApplicationDbContext _ctx;
    private readonly UserManager<User> _users;

    public TeacherController(ApplicationDbContext ctx, UserManager<User> users)
    {
        _ctx = ctx;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        var missing = await _ctx.Lessons
            .Include(l => l.Level)
            .Where(l => l.Video == null || l.Video == "")
            .OrderBy(l => l.Level!.SortOrder).ThenBy(l => l.SortOrder)
            .Take(20)
            .ToListAsync();

        var upcoming = await _ctx.Appointments
            .Include(a => a.User)
            .Include(a => a.AppointmentPlan)
            .Where(a => a.Date > DateTime.Now && a.Status == "Booked")
            .OrderBy(a => a.Date)
            .Take(15)
            .ToListAsync();

        var studentRole = await _users.GetUsersInRoleAsync("Student");
        // Prefer counting non-staff learners
        var staffIds = (await _users.GetUsersInRoleAsync("Teacher"))
            .Concat(await _users.GetUsersInRoleAsync("Admin"))
            .Select(u => u.Id)
            .ToHashSet();
        var learnerCount = await _ctx.Users.CountAsync(u => !staffIds.Contains(u.Id));

        return View(new TeacherDashboard
        {
            TotalStudents = learnerCount > 0 ? learnerCount : studentRole.Count,
            LessonsWithoutVideo = await _ctx.Lessons.CountAsync(l => l.Video == null || l.Video == ""),
            UpcomingBookings = upcoming.Count,
            RecentBookings = upcoming,
            MissingVideos = missing
        });
    }

    /// All learners with aggregate progress (no new DB tables — derived from LessonProgress).
    [HttpGet]
    public async Task<IActionResult> Students(string? q)
    {
        var totalLessons = await _ctx.Lessons.CountAsync();
        var staffIds = (await _users.GetUsersInRoleAsync("Teacher"))
            .Concat(await _users.GetUsersInRoleAsync("Admin"))
            .Select(u => u.Id)
            .ToHashSet();

        var usersQuery = _ctx.Users.AsNoTracking().Include(u => u.Sub).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            usersQuery = usersQuery.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(term)) ||
                u.FirstName.ToLower().Contains(term) ||
                u.LastName.ToLower().Contains(term));
        }

        var users = await usersQuery.OrderBy(u => u.LastName).ThenBy(u => u.FirstName).Take(200).ToListAsync();
        var progress = await _ctx.LessonProgress.AsNoTracking()
            .Where(p => p.IsCompleted)
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync();
        var completedMap = progress.ToDictionary(x => x.UserId, x => x.Count);

        var rows = users
            .Where(u => !staffIds.Contains(u.Id) || User.IsInRole("Admin"))
            .Select(u => new TeacherStudentRow
            {
                UserId = u.Id,
                DisplayName = u.DisplayName,
                Email = u.Email ?? "",
                Plan = u.Sub?.Type ?? "Free",
                Points = u.Points,
                StreakDays = u.StreakDays,
                CompletedLessons = completedMap.GetValueOrDefault(u.Id),
                TotalLessons = totalLessons,
                LastActivity = u.LastActivityDate
            })
            .ToList();

        ViewBag.Search = q;
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Student(string id)
    {
        var user = await _ctx.Users.AsNoTracking().Include(u => u.Sub).FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();

        var totalLessons = await _ctx.Lessons.CountAsync();
        var completed = await _ctx.LessonProgress.CountAsync(p => p.UserId == id && p.IsCompleted);
        var page = new TeacherStudentProgressPage
        {
            Student = new TeacherStudentRow
            {
                UserId = user.Id,
                DisplayName = user.DisplayName,
                Email = user.Email ?? "",
                Plan = user.Sub?.Type ?? "Free",
                Points = user.Points,
                StreakDays = user.StreakDays,
                CompletedLessons = completed,
                TotalLessons = totalLessons,
                LastActivity = user.LastActivityDate
            }
        };

        var levels = await _ctx.Levels.AsNoTracking().OrderBy(l => l.SortOrder).ToListAsync();
        var topics = await _ctx.Topics.AsNoTracking().ToListAsync();
        var lessons = await _ctx.Lessons.AsNoTracking().OrderBy(l => l.SortOrder).ToListAsync();
        var prog = await _ctx.LessonProgress.AsNoTracking().Where(p => p.UserId == id).ToListAsync();
        var progMap = prog.ToDictionary(p => p.LessonId);
        var certs = (await _ctx.Certificates.AsNoTracking().Where(c => c.UserId == id).Select(c => c.LevelId).ToListAsync()).ToHashSet();

        foreach (var level in levels)
        {
            var levelLessons = lessons.Where(l => l.LevelId == level.Id).ToList();
            var row = new LevelProgressRow
            {
                LevelId = level.Id,
                LevelName = level.Name,
                Total = levelLessons.Count,
                Completed = levelLessons.Count(l => progMap.TryGetValue(l.Id, out var p) && p.IsCompleted),
                HasCertificate = certs.Contains(level.Id)
            };
            foreach (var lesson in levelLessons)
            {
                progMap.TryGetValue(lesson.Id, out var p);
                var topicName = topics.FirstOrDefault(t => t.Id == lesson.TopicId)?.Name ?? "";
                row.Lessons.Add(new LessonProgressRow
                {
                    Title = lesson.Title,
                    TopicName = topicName,
                    VideoWatched = p?.VideoWatched ?? false,
                    ExercisesPassed = p?.ExercisesPassed ?? false,
                    IsCompleted = p?.IsCompleted ?? false,
                    WatchedPercent = p?.WatchedPercent ?? 0
                });
            }
            page.Levels.Add(row);
        }

        return View(page);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignStudent(string studentId)
    {
        var me = await _users.GetUserAsync(User);
        if (me == null || string.IsNullOrEmpty(studentId)) return RedirectToAction(nameof(Students));
        var exists = await _ctx.TeacherStudents.AnyAsync(t => t.TeacherId == me.Id && t.StudentId == studentId);
        if (!exists)
        {
            _ctx.TeacherStudents.Add(new TeacherStudent { TeacherId = me.Id, StudentId = studentId });
            await _ctx.SaveChangesAsync();
            TempData["Success"] = "Student assigned to you.";
        }
        return RedirectToAction(nameof(Student), new { id = studentId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnassignStudent(string studentId)
    {
        var me = await _users.GetUserAsync(User);
        if (me == null) return RedirectToAction(nameof(Students));
        var row = await _ctx.TeacherStudents.FirstOrDefaultAsync(t => t.TeacherId == me.Id && t.StudentId == studentId);
        if (row != null)
        {
            _ctx.TeacherStudents.Remove(row);
            await _ctx.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Students));
    }
}

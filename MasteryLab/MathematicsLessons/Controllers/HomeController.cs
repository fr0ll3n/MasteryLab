using MathematicsLessons.Infrastructure.Data;
using MathematicsLessons.Infrastructure.Data.Entities;
using MathematicsLessons.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace MathematicsLessons.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _ctx;
    private readonly UserManager<User> _users;

    public HomeController(ApplicationDbContext ctx, UserManager<User> users)
    {
        _ctx = ctx;
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var me = await _users.GetUserAsync(User);
        if (me != null)
            me = await _ctx.Users.Include(u => u.Sub).FirstAsync(u => u.Id == me.Id);

        var levels = await _ctx.Levels
            .OrderBy(l => l.SortOrder)
            .Select(l => new CourseStatVm
            {
                Id = l.Id,
                Name = l.Name,
                GradeRange = l.GradeRange,
                Description = l.Description,
                LessonCount = l.Lessons.Count,
                TopicCount = l.Topics.Count,
                SortOrder = l.SortOrder
            })
            .ToListAsync();

        ViewBag.IsLoggedIn = me != null;
        ViewBag.IsSubscribed = me?.SubId != null;
        ViewBag.IsAdmin = User.IsInRole("Admin");
        ViewBag.IsTeacher = User.IsInRole("Teacher");
        ViewBag.IsStudent = User.IsInRole("Student");
        ViewBag.DisplayName = me?.DisplayName ?? me?.Email;

        ViewBag.PlanName = me?.Sub?.Type;
        ViewBag.Levels = levels;
        ViewBag.TotalLessons = levels.Sum(l => l.LessonCount);
        ViewBag.TotalTopics = levels.Sum(l => l.TopicCount);
        ViewBag.TotalCourses = levels.Count;
        ViewBag.Plans = await _ctx.Subs.OrderBy(s => s.PricePerMonth).ToListAsync();

        // Continue learning: in-progress first, else next incomplete by catalog order
        if (me != null)
        {
            var progress = await _ctx.LessonProgress
                .AsNoTracking()
                .Where(p => p.UserId == me.Id)
                .ToListAsync();

            Lesson? continueLesson = null;

            var inProgressId = progress
                .Where(p => !p.IsCompleted && (p.VideoWatched || p.WatchedPercent > 0))
                .OrderByDescending(p => p.UpdatedAt)
                .Select(p => p.LessonId)
                .FirstOrDefault();

            if (inProgressId != 0)
            {
                continueLesson = await _ctx.Lessons.AsNoTracking()
                    .Include(l => l.Level)
                    .Include(l => l.Topic)
                    .FirstOrDefaultAsync(l => l.Id == inProgressId);
            }

            if (continueLesson == null)
            {
                var completed = progress.Where(p => p.IsCompleted).Select(p => p.LessonId).ToHashSet();
                var ordered = await _ctx.Lessons.AsNoTracking()
                    .Include(l => l.Level)
                    .Include(l => l.Topic)
                    .OrderBy(l => l.Level!.SortOrder).ThenBy(l => l.SortOrder)
                    .ToListAsync();

                continueLesson = ordered.FirstOrDefault(l => !completed.Contains(l.Id))
                    ?? ordered.FirstOrDefault();
            }

            if (continueLesson != null)
            {
                ViewBag.ContinueLessonId = continueLesson.Id;
                ViewBag.ContinueLessonTitle = continueLesson.Title;
                ViewBag.ContinueLevelName = continueLesson.Level?.Name;
                ViewBag.ContinueTopicName = continueLesson.Topic?.Name;
                var doneCount = progress.Count(p => p.IsCompleted);
                ViewBag.CompletedLessons = doneCount;
                ViewBag.UserPoints = me.Points;
                ViewBag.UserStreak = me.StreakDays;
                var goal = me.DailyGoalTarget > 0 ? me.DailyGoalTarget : 3;
                var today = DateTime.UtcNow.Date;
                var doneToday = await _ctx.LessonProgress.CountAsync(p =>
                    p.UserId == me.Id && p.IsCompleted && p.CompletedAt != null && p.CompletedAt.Value.Date == today);
                ViewBag.DailyGoalTarget = goal;
                ViewBag.DailyGoalDone = doneToday;

            }
        }

        return View();
    }

    [HttpGet]
    public IActionResult About() => View();

    [HttpGet]
    public IActionResult Privacy() => View();

    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}

public class CourseStatVm
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? GradeRange { get; set; }
    public string? Description { get; set; }
    public int LessonCount { get; set; }
    public int TopicCount { get; set; }
    public int SortOrder { get; set; }
}
